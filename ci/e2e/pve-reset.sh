#!/usr/bin/env bash
# Setzt Test-VMs auf den Snapshot clean-base zurück, startet sie und wartet, bis ihre
# GitHub-Runner online sind (zusammen max. 10 min). Als ghrunner auf dem Orchestrator.
#
#   pve-reset.sh [--trocken] <vmid>...
#
# --trocken: nur ausgeben, was aufgerufen würde.
# Exit 0 = alle Runner online, 1 = Fehler/Zeitlimit, 2 = Aufruf falsch.
set -euo pipefail
# shellcheck source=ci/e2e/pve-lib.sh
. "$(dirname "$(readlink -f "$0")")/pve-lib.sh"

RUNNER_TIMEOUT=600

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
[[ ${#vmids[@]} -gt 0 ]] || { echo "Aufruf: pve-reset.sh [--trocken] <vmid>..." >&2; exit 2; }

ci_lade_conf
for id in "${vmids[@]}"; do vm_ip "$id" >/dev/null || exit 1; done

ci_sperren
marker_setzen "${GITHUB_RUN_ID:-wachhalten}"

for id in "${vmids[@]}"; do
    zustand="$(vm_status "$id")" || exit 1
    read -r ist _ <<< "$zustand"
    if [[ "$ist" == running ]]; then
        log "VM $id läuft, harter Stopp vor dem Rollback"
        vm_aktion "$id" status/stop || exit 1
    fi
done
for id in "${vmids[@]}"; do warte_status "$id" stopped 120 || exit 1; done

for id in "${vmids[@]}"; do
    log "VM $id: Rollback auf $SNAPSHOT"
    vm_aktion "$id" "snapshot/$SNAPSHOT/rollback" || exit 1
done
# Der Rollback-Task setzt die Sperre "rollback" erst kurz nach der Antwort.
trocken || sleep 3
for id in "${vmids[@]}"; do warte_status "$id" - 600 || exit 1; done

for id in "${vmids[@]}"; do
    zustand="$(vm_status "$id")" || exit 1
    read -r ist _ <<< "$zustand"
    # Ein Snapshot mit RAM-Zustand läuft nach dem Rollback schon.
    if trocken || [[ "$ist" != running ]]; then
        log "VM $id: Start"
        vm_aktion "$id" status/start || exit 1
    fi
done
for id in "${vmids[@]}"; do warte_status "$id" running 120 || exit 1; done

ende=$((SECONDS + RUNNER_TIMEOUT))
for id in "${vmids[@]}"; do
    rest=$((ende - SECONDS))
    (( rest > 0 )) || rest=1
    rc=0
    runner_online "$id" "$rest" || rc=$?
    case "$rc" in
        0) ;;
        3) abbruch "VM $id: Runner neu registrieren" ;;
        *) abbruch "Runner nicht innerhalb von $((RUNNER_TIMEOUT / 60)) min online" ;;
    esac
done
log "Alle Runner online: ${vmids[*]}"
