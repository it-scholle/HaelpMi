#!/usr/bin/env bash
# Montags 02:30 per systemd-Timer als ghrunner: startet die Test-VMs einmal im Backup-Fenster,
# damit sich ihre Runner bei GitHub melden (GitHub löscht Registrierungen nach 14 Tagen ohne
# Verbindung), und fährt sie danach wieder herunter.
#
#   wachhalten.sh [--trocken]
#
# VMs aus WACHHALTEN_VMIDS in e2e.conf, Default "9101 9102 9103". Ist mqtt.conf ausgefüllt, hält
# eine retained MQTT-Sperre (Topic MQTT_TOPIC, Default siehe unten) den Test-Host während des
# Laufs wach.
# Exit 0 auch wenn der Test-Host nicht erreichbar ist; Exit 1, wenn ein Runner nicht online kam.
set -euo pipefail
DIR="$(dirname "$(readlink -f "$0")")"
# shellcheck source=ci/e2e/pve-lib.sh
. "$DIR/pve-lib.sh"

MQTT_TOPIC_STANDARD=homelab/pve-nzxt/lock/haelpmi-e2e
SPERRE_MINUTEN=45

trocken_arg=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --trocken) CI_TROCKEN=1; trocken_arg=(--trocken) ;;
        -h|--help) sed -n '2,/^set /{/^#/s/^# \{0,1\}//p}' "$0"; exit 0 ;;
        *) echo "Unbekannter Parameter: $1" >&2; exit 2 ;;
    esac
    shift
done

ci_lade_conf
read -ra vmids <<< "${WACHHALTEN_VMIDS:-9101 9102 9103}"

MQTT_HOST="$(conf_wert "$CI_ETC/mqtt.conf" MQTT_HOST)"
MQTT_PORT="$(conf_wert "$CI_ETC/mqtt.conf" MQTT_PORT)"
MQTT_USER="$(conf_wert "$CI_ETC/mqtt.conf" MQTT_USER)"
MQTT_PASS="$(conf_wert "$CI_ETC/mqtt.conf" MQTT_PASS)"
MQTT_TOPIC="$(conf_wert "$CI_ETC/mqtt.conf" MQTT_TOPIC)"
MQTT_TOPIC="${MQTT_TOPIC:-$MQTT_TOPIC_STANDARD}"

mqtt_aktiv() {
    [[ -n "$MQTT_HOST" && -n "$MQTT_USER" && -n "$MQTT_PASS" ]]
}

mqtt_senden() { # <mosquitto_pub-Argumente>...
    local tmp rc=0
    if trocken; then
        log "TROCKEN: mosquitto_pub -h $MQTT_HOST -p ${MQTT_PORT:-1883} -t $MQTT_TOPIC $*"
        return 0
    fi
    # Passwort nicht in argv: mosquitto_pub liest Standardoptionen aus
    # $XDG_CONFIG_HOME/mosquitto_pub. Die Datei trennt Optionen an Leerzeichen, daher dürfen
    # Benutzer und Passwort keine enthalten.
    if [[ "$MQTT_USER$MQTT_PASS" =~ [[:space:]] ]]; then
        log "MQTT_USER/MQTT_PASS enthalten Leerzeichen, MQTT-Sperre übersprungen"
        return 1
    fi
    tmp="$(mktemp -d)"
    (umask 077 && printf -- '-u %s\n-P %s\n' "$MQTT_USER" "$MQTT_PASS" > "$tmp/mosquitto_pub")
    XDG_CONFIG_HOME="$tmp" mosquitto_pub -h "$MQTT_HOST" -p "${MQTT_PORT:-1883}" -q 1 \
        -t "$MQTT_TOPIC" "$@" || rc=$?
    rm -rf "$tmp"
    return "$rc"
}

sperre_gesetzt=0
mqtt_sperre_setzen() {
    local bis
    mqtt_aktiv || { log "mqtt.conf nicht ausgefüllt, ohne MQTT-Sperre"; return 0; }
    bis="$(date -Iseconds -d "+$SPERRE_MINUTEN min")"
    if mqtt_senden -r -m "$(printf '{"bis":"%s","grund":"wachhalten"}' "$bis")"; then
        sperre_gesetzt=1
        log "MQTT-Sperre bis $bis gesetzt"
    else
        log "MQTT-Sperre konnte nicht gesetzt werden, weiter ohne"
    fi
}

mqtt_sperre_loesen() {
    [[ $sperre_gesetzt -eq 1 ]] || return 0
    if mqtt_senden -r -n; then
        log "MQTT-Sperre gelöst"
    else
        log "MQTT-Sperre konnte nicht gelöst werden"
    fi
}

if marker_juenger_als 180; then
    log "E2E-Lauf aktiv ($(tr '\n' ' ' < "$CI_MARKER")), Wachhalten übersprungen"
    exit 0
fi

if ! pve_erreichbar 300; then
    log "Test-Host nach 5 min nicht erreichbar, Wachhalten übersprungen"
    exit 0
fi

ci_sperren
mqtt_sperre_setzen

aufraeumen() {
    "$DIR/pve-stop.sh" "${trocken_arg[@]}" "${vmids[@]}" || log "Herunterfahren unvollständig"
    mqtt_sperre_loesen
}
trap aufraeumen EXIT

log "Wachhalten: ${vmids[*]}"
"$DIR/pve-reset.sh" "${trocken_arg[@]}" "${vmids[@]}"
