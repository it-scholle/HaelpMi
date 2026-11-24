# shellcheck shell=bash
# Gemeinsame Funktionen der E2E-Orchestrator-Skripte, per "source" eingebunden.
#
# Konfiguration in $HAELPMI_CI_ETC (Default /etc/haelpmi-ci):
#   pve.conf   PVE_API=https://<adresse>:8006, PVE_NODE=<node>
#   pve-token  PVEAPIToken=<id>=<wert>
#   e2e.conf   VM_<vmid>_IP=<adresse>, SSH_USER (Default hmadmin),
#              WACHHALTEN_VMIDS (optional, durch Leerzeichen getrennt)
#
# Das Token hat nur VM.Audit, VM.PowerMgmt und VM.Snapshot.Rollback auf den Test-VMs (#163).
# Deshalb wird ein Rollback nicht über /tasks abgewartet, sondern über die VM-Sperre in
# status/current.

CI_ETC="${HAELPMI_CI_ETC:-/etc/haelpmi-ci}"
CI_STATE=/var/lib/haelpmi-ci
CI_MARKER="$CI_STATE/lauf-aktiv"
CI_SPERRE=/run/lock/haelpmi-ci.lock
SNAPSHOT=clean-base
# 1 = nur ausgeben, was aufgerufen würde; kein API-, SSH- oder Dateizugriff außer den Configs.
CI_TROCKEN="${CI_TROCKEN:-0}"

log() { printf '%s %s\n' "$(date '+%F %T')" "$*" >&2; }
abbruch() { log "FEHLER: $*"; exit 1; }
trocken() { [[ "$CI_TROCKEN" == 1 ]]; }

# Configs gehören ghrunner und werden nie per "source" ausgeführt.
conf_wert() { # <datei> <schluessel>
    [[ -r "$1" ]] || return 0
    sed -n "s/^$2=//p" "$1" | tail -n 1 | tr -d '\r' | sed 's/^"\(.*\)"$/\1/'
}

ci_lade_conf() {
    PVE_API="$(conf_wert "$CI_ETC/pve.conf" PVE_API)"
    PVE_NODE="$(conf_wert "$CI_ETC/pve.conf" PVE_NODE)"
    SSH_USER="$(conf_wert "$CI_ETC/e2e.conf" SSH_USER)"
    SSH_USER="${SSH_USER:-hmadmin}"
    WACHHALTEN_VMIDS="$(conf_wert "$CI_ETC/e2e.conf" WACHHALTEN_VMIDS)"
    [[ -n "$PVE_API" && -n "$PVE_NODE" ]] || abbruch "PVE_API oder PVE_NODE fehlt in $CI_ETC/pve.conf"
    trocken || [[ -r "$CI_ETC/pve-token" ]] || abbruch "$CI_ETC/pve-token nicht lesbar"
}

vm_ip() { # <vmid>
    local ip
    ip="$(conf_wert "$CI_ETC/e2e.conf" "VM_$1_IP")"
    [[ -n "$ip" ]] || { log "VM_$1_IP fehlt in $CI_ETC/e2e.conf"; return 1; }
    echo "$ip"
}

# --- PVE-API ------------------------------------------------------------------------------

# Header per Prozess-Substitution, damit das Token weder in der Prozessliste noch im Log steht.
# -k: der Test-Host hat nur sein selbstsigniertes Zertifikat, eine eigene CA gibt es im Heimnetz nicht.
pve_get() { # <pfad> <ausgabedatei> -> gibt HTTP-Status aus
    curl -ksS --max-time 15 -o "$2" -w '%{http_code}' \
        -H @<(printf 'Authorization: %s\n' "$(cat "$CI_ETC/pve-token")") \
        "$PVE_API/api2/json$1"
}

pve_post() { # <pfad> <ausgabedatei> [param=wert]... -> gibt HTTP-Status aus
    local pfad="$1" aus="$2" p daten=()
    shift 2
    if trocken; then
        log "TROCKEN: POST $pfad${*:+ $*}"
        echo 200
        return 0
    fi
    for p in "$@"; do daten+=(--data-urlencode "$p"); done
    [[ ${#daten[@]} -gt 0 ]] || daten=(--data '')
    curl -ksS --max-time 30 -X POST -o "$aus" -w '%{http_code}' \
        -H @<(printf 'Authorization: %s\n' "$(cat "$CI_ETC/pve-token")") \
        "${daten[@]}" "$PVE_API/api2/json$pfad"
}

vm_aktion() { # <vmid> <pfad unter der VM, z. B. status/start> [param=wert]...
    local id="$1" pfad="$2" tmp status
    shift 2
    tmp="$(mktemp)"
    status="$(pve_post "/nodes/$PVE_NODE/qemu/$id/$pfad" "$tmp" "$@" || true)"
    if [[ "$status" != 200 ]]; then
        log "VM $id: POST $pfad -> HTTP $status $(head -c 300 "$tmp" 2>/dev/null)"
        rm -f "$tmp"
        return 1
    fi
    rm -f "$tmp"
}

vm_status() { # <vmid> -> "<status> [<sperre>]", Exit 1 bei API-Fehler
    local tmp status
    if trocken; then
        # "running", damit der Trockenlauf auch den Stopp-Zweig zeigt.
        log "TROCKEN: GET /nodes/$PVE_NODE/qemu/$1/status/current"
        echo running
        return 0
    fi
    tmp="$(mktemp)"
    status="$(pve_get "/nodes/$PVE_NODE/qemu/$1/status/current" "$tmp" || true)"
    if [[ "$status" != 200 ]]; then
        log "VM $1: status/current -> HTTP $status"
        rm -f "$tmp"
        return 1
    fi
    jq -r '[.data.status, (.data.lock // empty)] | join(" ")' "$tmp"
    rm -f "$tmp"
}

warte_status() { # <vmid> <soll, "-" = beliebig> <timeout_s>; wartet zusätzlich, bis keine Sperre mehr besteht
    local id="$1" soll="$2" ende=$((SECONDS + $3)) ist sperre
    if trocken; then
        log "TROCKEN: warte bis VM $id ${soll/#-/beliebig} ohne Sperre, max $3 s"
        return 0
    fi
    while :; do
        read -r ist sperre <<< "$(vm_status "$id" || true)"
        if [[ -n "$ist" && -z "$sperre" && ( "$soll" == - || "$ist" == "$soll" ) ]]; then
            return 0
        fi
        if (( SECONDS >= ende )); then
            log "VM $id: nach $3 s '${ist:-?}'${sperre:+ (Sperre $sperre)}, erwartet $soll"
            return 1
        fi
        sleep 3
    done
}

pve_erreichbar() { # <timeout_s>
    local ende=$((SECONDS + $1)) tmp status
    if trocken; then
        log "TROCKEN: GET /version bis HTTP 200, max $1 s"
        return 0
    fi
    tmp="$(mktemp)"
    while :; do
        status="$(pve_get /version "$tmp" 2>/dev/null || true)"
        [[ "$status" == 200 ]] && { rm -f "$tmp"; return 0; }
        (( SECONDS >= ende )) && { rm -f "$tmp"; return 1; }
        sleep 10
    done
}

# --- Runner auf den Test-VMs --------------------------------------------------------------

# Läuft auf der VM (Standard-Shell pwsh, siehe vorlage-einrichten.ps1). Der Runner legt pro
# Start ein neues Runner_*.log an; maßgeblich ist das jüngste seit dem letzten Boot.
# LastWriteTime allein reicht nicht, NTFS aktualisiert sie bei offenen Dateien verzögert.
# Ausgabe: ONLINE | ABGEMELDET <log> | WARTEN <grund>
RUNNER_PRUEFUNG_PS='$ErrorActionPreference = "Stop"
$boot = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
$log = Get-ChildItem "C:\actions-runner\_diag\Runner_*.log" -ErrorAction SilentlyContinue |
    Where-Object { $_.CreationTime -ge $boot -or $_.LastWriteTime -ge $boot } |
    Sort-Object CreationTime -Descending | Select-Object -First 1
if (-not $log) { "WARTEN kein Runner-Log seit dem letzten Boot"; exit 0 }
$fs = [IO.File]::Open($log.FullName, "Open", "Read", "ReadWrite, Delete")
try { $text = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Close() }
if ($text -match "(?i)registration\b[^\r\n]{0,80}\bdeleted|not configured\. run config") { "ABGEMELDET $($log.Name)"; exit 0 }
if ($text -match "Listening for Jobs") { "ONLINE"; exit 0 }
"WARTEN $($log.Name) noch ohne Listening for Jobs"'

runner_online() { # <vmid> <timeout_s>; Exit 0 online, 1 Zeitlimit/Fehler, 3 Registrierung gelöscht
    local id="$1" ende=$((SECONDS + $2)) ip kodiert antwort
    ip="$(vm_ip "$id")" || return 1
    if trocken; then
        log "TROCKEN: ssh $SSH_USER@$ip: jüngstes Runner_*.log seit Boot mit 'Listening for Jobs', max $2 s"
        return 0
    fi
    # -EncodedCommand umgeht das Quoting durch sshd und die Standard-Shell der VM.
    kodiert="$(printf '%s' "$RUNNER_PRUEFUNG_PS" | iconv -f UTF-8 -t UTF-16LE | base64 -w 0)"
    while :; do
        antwort="$(timeout 40 ssh -n -o BatchMode=yes -o StrictHostKeyChecking=accept-new \
            -o ConnectTimeout=10 "$SSH_USER@$ip" \
            "pwsh -NoProfile -NonInteractive -EncodedCommand $kodiert" 2>/dev/null \
            | tr -d '\r' | tail -n 1 || true)"
        case "$antwort" in
            ONLINE)
                log "VM $id: Runner online"
                return 0 ;;
            ABGEMELDET*)
                log "VM $id: Runner-Registrierung gelöscht oder fehlt (${antwort#ABGEMELDET }). Runner neu registrieren (GitHub löscht Registrierungen nach 14 Tagen ohne Verbindung)."
                return 3 ;;
        esac
        if (( SECONDS >= ende )); then
            log "VM $id: Runner nach $2 s nicht online (${antwort:-keine SSH-Antwort})"
            return 1
        fi
        sleep 10
    done
}

# --- Sperre und Lauf-Marker ---------------------------------------------------------------

# Hält die Sperre bis Prozessende. Kindprozesse erben fd 9 und die Variable, damit
# wachhalten.sh Reset und Stopp unter einer Sperre aufrufen kann.
ci_sperren() {
    [[ "${HAELPMI_CI_GESPERRT:-0}" == 1 ]] && return 0
    if trocken; then
        log "TROCKEN: flock $CI_SPERRE"
        return 0
    fi
    exec 9>>"$CI_SPERRE"
    if ! flock -n 9; then
        log "Warte auf Sperre $CI_SPERRE"
        flock -w 1800 9 || abbruch "Sperre $CI_SPERRE nach 30 min nicht frei"
    fi
    export HAELPMI_CI_GESPERRT=1
}

marker_setzen() { # <lauf-id>
    if trocken; then
        log "TROCKEN: Marker $CI_MARKER (lauf=$1)"
        return 0
    fi
    printf 'lauf=%s\nseit=%s\n' "$1" "$(date -Iseconds)" > "$CI_MARKER.neu"
    mv "$CI_MARKER.neu" "$CI_MARKER"
}

marker_entfernen() {
    if trocken; then
        log "TROCKEN: rm $CI_MARKER"
        return 0
    fi
    rm -f "$CI_MARKER"
}

marker_juenger_als() { # <minuten>
    [[ -n "$(find "$CI_MARKER" -mmin -"$1" 2>/dev/null)" ]]
}
