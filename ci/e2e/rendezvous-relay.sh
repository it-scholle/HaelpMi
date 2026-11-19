#!/usr/bin/env bash
# Spiegelt während eines E2E-Laufs die Rendezvous-Marken (C:\haelpmi-e2e\rv) zwischen VM A
# und VM B: alle 2 s neue oder geänderte Marken der einen Seite auf die andere. Als ghrunner
# auf dem Orchestrator, per SSH mit je einer ControlMaster-Verbindung pro VM.
#
#   rendezvous-relay.sh [--trocken] [--timeout <minuten>] <vmid-a> <vmid-b>
#
# Ende, sobald die Marken a-ende und b-ende vorliegen (setzt der Workflow nach dem Testlauf).
# --timeout: Default 25 min.
# --trocken: nur ausgeben, was aufgerufen würde; ein Durchlauf, keine SSH-Verbindung.
# Exit 0 = beide Seiten fertig, 1 = Zeitlimit/Fehler, 2 = Aufruf falsch.
set -euo pipefail
# shellcheck source=ci/e2e/pve-lib.sh
. "$(dirname "$(readlink -f "$0")")/pve-lib.sh"

INTERVALL=2
timeout_min=25
RV_ORDNER='C:\haelpmi-e2e\rv'
# Wie Rendezvous.GueltigeMarke im Testprojekt; ".*.tmp" sind halb geschriebene Marken.
MARKE_RE='^[A-Za-z0-9][A-Za-z0-9._-]*$'

vmids=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --trocken) CI_TROCKEN=1 ;;
        --timeout) shift; [[ "${1:-}" =~ ^[0-9]+$ ]] || { echo "--timeout braucht Minuten" >&2; exit 2; }
                   timeout_min="$1" ;;
        -h|--help) sed -n '2,/^set /{/^#/s/^# \{0,1\}//p}' "$0"; exit 0 ;;
        *) [[ "$1" =~ ^[0-9]+$ ]] || { echo "Unbekannter Parameter: $1" >&2; exit 2; }
           vmids+=("$1") ;;
    esac
    shift
done
[[ ${#vmids[@]} -eq 2 ]] || { echo "Aufruf: rendezvous-relay.sh [--trocken] [--timeout <min>] <vmid-a> <vmid-b>" >&2; exit 2; }

# Nur e2e.conf; die PVE-API braucht dieses Skript nicht.
SSH_USER="$(conf_wert "$CI_ETC/e2e.conf" SSH_USER)"
SSH_USER="${SSH_USER:-hmadmin}"
declare -A ziel
ziel[a]="$SSH_USER@$(vm_ip "${vmids[0]}")" || exit 1
ziel[b]="$SSH_USER@$(vm_ip "${vmids[1]}")" || exit 1

master_da() { ssh "${ssh_opts[@]}" -O check "${ziel[$1]}" 2>/dev/null; }

master_starten() { # <seite>
    master_da "$1" && return 0
    ssh "${ssh_opts[@]}" -o ControlMaster=yes -o ControlPersist=yes -fN "${ziel[$1]}" 2>/dev/null
}

# -EncodedCommand umgeht das Quoting durch sshd und die Standard-Shell der VM (wie runner_online).
ps_auf() { # <seite> <powershell-skript>
    local kodiert
    kodiert="$(printf '%s' "$2" | iconv -f UTF-8 -t UTF-16LE | base64 -w 0)"
    timeout 30 ssh "${ssh_opts[@]}" -o ControlMaster=no -n "${ziel[$1]}" \
        "pwsh -NoProfile -NonInteractive -EncodedCommand $kodiert"
}

# Ausgabe je Marke: "<name><TAB><inhalt als base64>".
LISTE_PS="\$d = '$RV_ORDNER'
if (Test-Path -LiteralPath \$d) {
    Get-ChildItem -LiteralPath \$d -File | Where-Object { \$_.Name -notlike '.*' } | ForEach-Object {
        \$_.Name + [char]9 + [Convert]::ToBase64String([IO.File]::ReadAllBytes(\$_.FullName))
    }
}"

declare -A stand_a stand_b gespiegelt_nach_a gespiegelt_nach_b

liste_lesen() { # <seite> -> füllt stand_<seite>; Exit 1, wenn die VM nicht antwortet
    local ausgabe name inhalt
    ausgabe="$(ps_auf "$1" "$LISTE_PS" | tr -d '\r')" || return 1
    if [[ "$1" == a ]]; then stand_a=(); else stand_b=(); fi
    while IFS=$'\t' read -r name inhalt; do
        [[ "$name" =~ $MARKE_RE && "$inhalt" =~ ^[A-Za-z0-9+/=]*$ ]] || continue
        if [[ "$1" == a ]]; then stand_a[$name]="$inhalt"; else stand_b[$name]="$inhalt"; fi
    done <<< "$ausgabe"
}

# Schreibt wie Rendezvous.Signal erst eine Temp-Datei und benennt sie dann um.
schreiben() { # <seite> <name> <inhalt-base64>...
    local seite="$1" skript="\$d = '$RV_ORDNER'
New-Item -ItemType Directory -Force -Path \$d | Out-Null"
    shift
    while [[ $# -gt 0 ]]; do
        skript+="
\$t = Join-Path \$d ('.$1.' + [guid]::NewGuid().ToString('N') + '.tmp')
[IO.File]::WriteAllBytes(\$t, [Convert]::FromBase64String('$2'))
Move-Item -LiteralPath \$t -Destination (Join-Path \$d '$1') -Force"
        shift 2
    done
    ps_auf "$seite" "$skript" >/dev/null
}

spiegeln() { # <von> <nach>
    local von="$1" nach="$2" name inhalt neu=()
    local -n quelle="stand_$von" ziel_stand="stand_$nach" zurueck="gespiegelt_nach_$von" hin="gespiegelt_nach_$nach"
    for name in "${!quelle[@]}"; do
        inhalt="${quelle[$name]}"
        # Von der Gegenseite gespiegelt oder dort schon identisch vorhanden: nichts zu tun.
        # Leere Marken (z. B. b-quittiert) haben leeren Inhalt, deshalb erst auf Vorhandensein prüfen.
        [[ -n "${zurueck[$name]+x}" && "${zurueck[$name]}" == "$inhalt" ]] && continue
        if [[ -n "${ziel_stand[$name]+x}" && "${ziel_stand[$name]}" == "$inhalt" ]]; then
            hin[$name]="$inhalt"
            continue
        fi
        neu+=("$name" "$inhalt")
    done
    [[ ${#neu[@]} -gt 0 ]] || return 0
    schreiben "$nach" "${neu[@]}" || { log "Schreiben nach $nach fehlgeschlagen, nächster Durchlauf"; return 0; }
    local i
    for ((i = 0; i < ${#neu[@]}; i += 2)); do
        hin[${neu[i]}]="${neu[i + 1]}"
        # Sonst spiegelt die Gegenrichtung im selben Durchlauf den veralteten Stand zurück.
        ziel_stand[${neu[i]}]="${neu[i + 1]}"
        log "Marke ${neu[i]}: $von -> $nach"
    done
}

ende_erreicht() {
    [[ -n "${stand_a[a-ende]+x}${stand_b[a-ende]+x}" && -n "${stand_a[b-ende]+x}${stand_b[b-ende]+x}" ]]
}

if trocken; then
    for s in a b; do
        log "TROCKEN: ssh -M ${ziel[$s]}, danach alle ${INTERVALL} s Marken in $RV_ORDNER auflisten und zur Gegenseite kopieren"
    done
    log "TROCKEN: Ende bei a-ende und b-ende, sonst nach $timeout_min min Exit 1"
    exit 0
fi

sockel="$(mktemp -d "${TMPDIR:-/tmp}/haelpmi-rv.XXXXXX")"
ssh_opts=(-o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10
          -o ServerAliveInterval=15 -o ControlPath="$sockel/%C")

aufraeumen() {
    local s
    for s in a b; do
        ssh "${ssh_opts[@]}" -O exit "${ziel[$s]}" 2>/dev/null || true
    done
    rm -rf "$sockel"
}
trap aufraeumen EXIT

frist=$((SECONDS + timeout_min * 60))
log "Relay ${vmids[0]} <-> ${vmids[1]} gestartet, Zeitlimit $timeout_min min"
while :; do
    if (( SECONDS >= frist )); then
        log "Zeitlimit: nach $timeout_min min fehlt a-ende oder b-ende"
        exit 1
    fi
    bereit=1
    for s in a b; do
        master_starten "$s" || { bereit=0; continue; }
        liste_lesen "$s" || bereit=0
    done
    if [[ $bereit == 1 ]]; then
        spiegeln a b
        spiegeln b a
        if ende_erreicht; then
            log "a-ende und b-ende liegen vor, Relay beendet"
            exit 0
        fi
    fi
    sleep "$INTERVALL"
done
