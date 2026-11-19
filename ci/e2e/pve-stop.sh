#!/usr/bin/env bash
# Fährt Test-VMs herunter (ACPI, 120 s), notfalls harter Stopp, und entfernt den Lauf-Marker.
# Als ghrunner auf dem Orchestrator, im Workflow mit "if: always()".
#
#   pve-stop.sh [--trocken] <vmid>...
#
# --trocken: nur ausgeben, was aufgerufen würde.
# Exit 0 = alle VMs aus (auch wenn sie schon aus waren), 1 = mindestens eine läuft noch.
set -euo pipefail
# shellcheck source=ci/e2e/pve-lib.sh
. "$(dirname "$(readlink -f "$0")")/pve-lib.sh"

vmids=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --trocken) CI_TROCKEN=1 ;;
        -h|--help) sed -n '2,/^set /{/^#/s/^# \{0,1\}//p}' "$0"; exit 0 ;;
        *) [[ "$1" =~ ^[0-9]+$ ]] || { echo "Unbekannter Parameter: $1" >&2; exit 2; }
           vmids+=("$1") ;;
    esac
    shift
done
[[ ${#vmids[@]} -gt 0 ]] || { echo "Aufruf: pve-stop.sh [--trocken] <vmid>..." >&2; exit 2; }

# Abschalten von PVE 2 nach einem Lauf (MQTT-Sperre, Übergabe an das Backup) folgt mit dem
# privaten Backup-Ticket; bis dahin bleibt der Host an.
host_aus_falls_erwuenscht() {
    log "Host-Abschaltung noch nicht umgesetzt, PVE 2 bleibt an"
}

ci_lade_conf
ci_sperren

fehler=0
laufend=()
for id in "${vmids[@]}"; do
    if ! zustand="$(vm_status "$id")"; then
        fehler=1
        continue
    fi
    read -r ist _ <<< "$zustand"
    if [[ "$ist" == stopped ]]; then
        log "VM $id ist schon aus"
        continue
    fi
    log "VM $id: Herunterfahren"
    vm_aktion "$id" status/shutdown timeout=120 || true
    laufend+=("$id")
done

for id in "${laufend[@]}"; do
    warte_status "$id" stopped 150 && continue
    log "VM $id: harter Stopp"
    if ! { vm_aktion "$id" status/stop && warte_status "$id" stopped 60; }; then
        fehler=1
    fi
done

marker_entfernen
host_aus_falls_erwuenscht
exit "$fehler"
