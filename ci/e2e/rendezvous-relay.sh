#!/usr/bin/env bash
# Spiegelt während eines E2E-Laufs die Rendezvous-Marken (C:\haelpmi-e2e\rv) zwischen den
# Test-VMs: alle 2 s jede neue oder geänderte Marke eines Geräts auf alle anderen. Als
# ghrunner auf dem Orchestrator, per SSH mit je einer ControlMaster-Verbindung pro VM.
#
#   rendezvous-relay.sh [--trocken] [--timeout <minuten>] <gerät>=<vmid> <gerät>=<vmid>...
#
# Gerät ist der Kleinbuchstabe aus ci/e2e/geraete.json (z. B. a=9101 b=9102 c=9103),
# mindestens zwei. Ende, sobald für jedes Gerät die Marke <gerät>-ende vorliegt (setzt der
# Workflow nach dem Testlauf).
# --timeout: Default 25 min.
# --trocken: nur ausgeben, was aufgerufen würde; ein Durchlauf, keine SSH-Verbindung.
# Exit 0 = alle Geräte fertig, 1 = Zeitlimit/Fehler, 2 = Aufruf falsch.
set -euo pipefail
# shellcheck source=ci/e2e/pve-lib.sh
. "$(dirname "$(readlink -f "$0")")/pve-lib.sh"

INTERVALL=2
timeout_min=25
RV_ORDNER='C:\haelpmi-e2e\rv'
# Wie Rendezvous.GueltigeMarke im Testprojekt; ".*.tmp" sind halb geschriebene Marken.
MARKE_RE='^[A-Za-z0-9][A-Za-z0-9._-]*$'

aufruf() {
    echo "Aufruf: rendezvous-relay.sh [--trocken] [--timeout <min>] <gerät>=<vmid> <gerät>=<vmid>..." >&2
    exit 2
}

seiten=()
declare -A vmid_von
while [[ $# -gt 0 ]]; do
    case "$1" in
        --trocken) CI_TROCKEN=1 ;;
        --timeout) shift; [[ "${1:-}" =~ ^[0-9]+$ ]] || { echo "--timeout braucht Minuten" >&2; exit 2; }
                   timeout_min="$1" ;;
        -h|--help) sed -n '2,/^set /{/^#/s/^# \{0,1\}//p}' "$0"; exit 0 ;;
        *) [[ "$1" =~ ^([a-z])=([0-9]+)$ ]] || { echo "Unbekannter Parameter: $1" >&2; aufruf; }
           [[ -z "${vmid_von[${BASH_REMATCH[1]}]+x}" ]] || { echo "Gerät ${BASH_REMATCH[1]} doppelt" >&2; exit 2; }
           seiten+=("${BASH_REMATCH[1]}")
           vmid_von[${BASH_REMATCH[1]}]="${BASH_REMATCH[2]}" ;;
    esac
    shift
done
[[ ${#seiten[@]} -ge 2 ]] || aufruf

# Nur e2e.conf; die PVE-API braucht dieses Skript nicht.
SSH_USER="$(conf_wert "$CI_ETC/e2e.conf" SSH_USER)"
SSH_USER="${SSH_USER:-hmadmin}"
declare -A ziel
for s in "${seiten[@]}"; do
    ip="$(vm_ip "${vmid_von[$s]}")" || exit 1
    ziel[$s]="$SSH_USER@$ip"
done

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

# Schlüssel "<seite>|<marke>". stand: zuletzt gelesener Inhalt auf der Seite.
# gespiegelt: was das Relay selbst auf die Seite geschrieben hat; eine solche Marke ist dort
# keine Quelle und wird weder zurück- noch weitergespiegelt.
declare -A stand gespiegelt

liste_lesen() { # <seite>; Exit 1, wenn die VM nicht antwortet
    local seite="$1" ausgabe name inhalt schluessel
    ausgabe="$(ps_auf "$seite" "$LISTE_PS" | tr -d '\r')" || return 1
    for schluessel in "${!stand[@]}"; do
        if [[ "$schluessel" == "$seite|"* ]]; then unset "stand[$schluessel]"; fi
    done
    while IFS=$'\t' read -r name inhalt; do
        [[ "$name" =~ $MARKE_RE && "$inhalt" =~ ^[A-Za-z0-9+/=]*$ ]] || continue
        stand["$seite|$name"]="$inhalt"
    done <<< "$ausgabe"
}

# Leere Marken (z. B. b-quittiert) haben leeren Inhalt, deshalb immer erst auf Vorhandensein prüfen.
ist_quelle() { # <seite> <marke>
    local k="$1|$2"
    [[ -n "${stand[$k]+x}" ]] || return 1
    ! [[ -n "${gespiegelt[$k]+x}" && "${gespiegelt[$k]}" == "${stand[$k]}" ]]
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

spiegeln_nach() { # <seite>: Quell-Marken aller anderen Geräte auf dieses
    local nach="$1" von schluessel name inhalt k i
    local -a neu=() herkunft=()
    local -A geplant=()
    for von in "${seiten[@]}"; do
        [[ "$von" == "$nach" ]] && continue
        for schluessel in "${!stand[@]}"; do
            [[ "$schluessel" == "$von|"* ]] || continue
            name="${schluessel#*|}"
            ist_quelle "$von" "$name" || continue
            [[ -n "${geplant[$name]+x}" ]] && continue
            inhalt="${stand[$schluessel]}"
            k="$nach|$name"
            # Eine eigene, abweichende Quell-Marke des Zielgeräts wird nie überschrieben.
            if ist_quelle "$nach" "$name" && [[ "${stand[$k]}" != "$inhalt" ]]; then
                continue
            fi
            if [[ -n "${stand[$k]+x}" && "${stand[$k]}" == "$inhalt" ]]; then
                gespiegelt[$k]="$inhalt"
                continue
            fi
            geplant[$name]=1
            neu+=("$name" "$inhalt")
            herkunft+=("$von")
        done
    done
    [[ ${#neu[@]} -gt 0 ]] || return 0
    schreiben "$nach" "${neu[@]}" || { log "Schreiben nach $nach fehlgeschlagen, nächster Durchlauf"; return 0; }
    for ((i = 0; i < ${#neu[@]}; i += 2)); do
        k="$nach|${neu[i]}"
        gespiegelt[$k]="${neu[i + 1]}"
        # Sonst gilt die geschriebene Marke im selben Durchlauf auf dem Zielgerät als neue Quelle.
        stand[$k]="${neu[i + 1]}"
        log "Marke ${neu[i]}: ${herkunft[i / 2]} -> $nach"
    done
}

ende_erreicht() {
    local s t gefunden
    for s in "${seiten[@]}"; do
        gefunden=0
        for t in "${seiten[@]}"; do
            if [[ -n "${stand[$t|$s-ende]+x}" ]]; then gefunden=1; break; fi
        done
        [[ $gefunden == 1 ]] || return 1
    done
}

ende_marken="$(printf '%s-ende ' "${seiten[@]}")"
ende_marken="${ende_marken% }"

if trocken; then
    for s in "${seiten[@]}"; do
        log "TROCKEN: ssh -M ${ziel[$s]} (Gerät $s, VM ${vmid_von[$s]})"
    done
    log "TROCKEN: danach alle ${INTERVALL} s Marken in $RV_ORDNER auf allen Geräten auflisten und auf die jeweils anderen kopieren"
    log "TROCKEN: Ende bei $ende_marken, sonst nach $timeout_min min Exit 1"
    exit 0
fi

sockel="$(mktemp -d "${TMPDIR:-/tmp}/haelpmi-rv.XXXXXX")"
ssh_opts=(-o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10
          -o ServerAliveInterval=15 -o ControlPath="$sockel/%C")

aufraeumen() {
    local s
    for s in "${seiten[@]}"; do
        ssh "${ssh_opts[@]}" -O exit "${ziel[$s]}" 2>/dev/null || true
    done
    rm -rf "$sockel"
}
trap aufraeumen EXIT

frist=$((SECONDS + timeout_min * 60))
vm_liste="$(for s in "${seiten[@]}"; do printf '%s=%s ' "$s" "${vmid_von[$s]}"; done)"
log "Relay ${vm_liste% } gestartet, Zeitlimit $timeout_min min"
while :; do
    if (( SECONDS >= frist )); then
        log "Zeitlimit: nach $timeout_min min fehlt mindestens eine Marke von $ende_marken"
        exit 1
    fi
    bereit=1
    for s in "${seiten[@]}"; do
        master_starten "$s" || { bereit=0; continue; }
        liste_lesen "$s" || bereit=0
    done
    if [[ $bereit == 1 ]]; then
        for s in "${seiten[@]}"; do
            spiegeln_nach "$s"
        done
        if ende_erreicht; then
            log "$ende_marken liegen vor, Relay beendet"
            exit 0
        fi
    fi
    sleep "$INTERVALL"
done
