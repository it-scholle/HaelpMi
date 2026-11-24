#!/usr/bin/env bash
# Richtet den Orchestrator-LXC (Debian 12, haelpmi-orch) der E2E-Umgebung ein.
#
# Idempotent: jeder Schritt prüft zuerst, ob er schon erfüllt ist. Als root ausführen.
#
# Voraussetzungen im frischen LXC:
#   - Container mit "features: nesting=1" anlegen, sonst warnt systemd 252 und der
#     SSH-Login dauert ~25 s.
#   - Debian 12 bringt kein curl mit, zum Holen dieses Skripts vorher:
#       apt-get update && apt-get install -y curl ca-certificates
#   - pve-lib.sh, pve-reset.sh, pve-stop.sh und wachhalten.sh aus ci/e2e ins selbe
#     Verzeichnis wie dieses Skript legen; sie werden nach /opt/haelpmi-ci kopiert.
#
#   orchestrator-einrichten.sh [--pve-host <adresse>]   einrichten
#   orchestrator-einrichten.sh --pruefen                nur prüfen, Exit 1 bei Abweichung
#   orchestrator-einrichten.sh --api-test               PVE-Token gegen die API testen
#
# Umgebungsvariablen (alle optional):
#   RUNNER_TOKEN  Registrierungstoken für den GitHub-Runner (gilt 1 h), nur beim ersten Lauf nötig
#   PVE_HOST      Adresse des Test-Hosts (pve-nzxt: 10.10.32.0), alternativ zu --pve-host
#   PVE_NODE      Node-Name; ohne Angabe per API ermittelt
#
# Die PVE-API hat keinen DNS-Namen, daher die Adresse immer per --pve-host oder PVE_HOST.
#
# Das PVE-Token wird als pve-token.json (Ausgabe von "pveum user token add ...
# --output-format json") nach /etc/haelpmi-ci gelegt und beim nächsten Lauf umgewandelt.
set -euo pipefail

RUNNER_VERSION=2.338.2
RUNNER_SHA256=baebf204307bac6193480852732f0aba2ae97b6c5597c244f4d5558390147c40
RUNNER_URL="https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz"
REPO_URL=https://github.com/it-scholle/HaelpMi

NUTZER=ghrunner
NUTZER_HOME=/home/ghrunner
SSH_SCHLUESSEL="$NUTZER_HOME/.ssh/id_ed25519"
RUNNER_DIR=/opt/actions-runner
# Hält fest, welches Archiv nach SHA-Prüfung entpackt wurde. Spätere Selbst-Updates des
# Runners ändern bin/, nicht diese Datei.
RUNNER_MARKER="$RUNNER_DIR/.haelpmi-archiv-sha256"
CI_ETC=/etc/haelpmi-ci
CI_OPT=/opt/haelpmi-ci
CI_STATE=/var/lib/haelpmi-ci
WACHHALTEN="$CI_OPT/wachhalten.sh"
QUELLE="$(dirname "$(readlink -f "$0")")"
CI_SKRIPTE=(pve-lib.sh pve-reset.sh pve-stop.sh wachhalten.sh)
SYSTEMD_DIR=/etc/systemd/system
UNIT=haelpmi-e2e-wachhalten
PAKETE=(curl jq openssh-client mosquitto-clients ca-certificates)
# Das Token hat nur VM.Audit, VM.PowerMgmt und VM.Snapshot.Rollback auf diesen VMs (#163),
# kein Pool.Audit; geprüft wird deshalb VM-weise statt über den Pool.
TEST_VMIDS=(9101 9102 9103 9110)
FREMDE_VMID=100

pruefen=0
api_test=0
pve_host="${PVE_HOST:-}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --pruefen) pruefen=1 ;;
        --api-test) api_test=1 ;;
        --pve-host) pve_host="${2:?--pve-host braucht eine Adresse}"; shift ;;
        -h|--help) sed -n '2,/^set /{/^#/s/^# \{0,1\}//p}' "$0"; exit 0 ;;
        *) echo "Unbekannter Parameter: $1" >&2; exit 2 ;;
    esac
    shift
done

if [[ $EUID -ne 0 ]]; then
    echo "Bitte als root ausführen." >&2
    exit 2
fi

export DEBIAN_FRONTEND=noninteractive

SERVICE_INHALT="[Unit]
Description=HaelpMi E2E: Test-VMs im Montags-Backup-Fenster wachhalten
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
User=$NUTZER
ExecStart=$WACHHALTEN
"

TIMER_INHALT="[Unit]
Description=HaelpMi E2E: Wachhalten montags 02:30

[Timer]
OnCalendar=Mon *-*-* 02:30:00
Persistent=true

[Install]
WantedBy=timers.target
"

# Frühere Fassungen legten statt wachhalten.sh nur einen Platzhalter mit diesem Text ab.
WACHHALTEN_PLATZHALTER='Wachhalte-Skript folgt mit #164'

E2E_CONF_VORLAGE='# Adressen der Test-VMs (SSH mit dem Schlüssel des Orchestrators)
VM_9101_IP=
VM_9102_IP=
VM_9103_IP=
VM_9110_IP=
SSH_USER=hmadmin
# Optional, durch Leerzeichen getrennt; ohne Angabe "9101 9102 9103"
WACHHALTEN_VMIDS=
'

# --- Hilfsfunktionen ----------------------------------------------------------------------

rechte_ok() { # <pfad> <oktalmodus> <besitzer>
    [[ -e "$1" && "$(stat -c '%a %U %G' "$1")" == "$2 $3 $3" ]]
}

setze_rechte() { # <pfad> <oktalmodus> <besitzer>
    chown "$3:$3" "$1"
    chmod "$2" "$1"
}

# Konfigdateien gehören ghrunner; als root nur lesen, nie per "source" ausführen.
conf_wert() { # <datei> <schluessel>
    [[ -r "$1" ]] || return 0
    sed -n "s/^$2=//p" "$1" | tail -n 1
}

inhalt_gleich() { # <datei> <soll-inhalt>
    [[ -f "$1" ]] && cmp -s "$1" <(printf '%s' "$2")
}

# Header per Prozess-Substitution, damit das Token weder in der Prozessliste noch im Log steht.
# -k: der Test-Host hat nur sein selbstsigniertes Zertifikat, eine eigene CA gibt es im Heimnetz nicht.
pve_get() { # <api-basis> <pfad> <ausgabedatei> -> gibt HTTP-Status aus
    curl -ksS --max-time 15 -o "$3" -w '%{http_code}' \
        -H @<(printf 'Authorization: %s\n' "$(cat "$CI_ETC/pve-token")") \
        "$1/api2/json$2"
}

# --- Schritte -----------------------------------------------------------------------------
# ist_*: Exit 0 = erfüllt. setze_*: Exit 0 = gesetzt, Exit 2 = nur von Hand möglich (Hinweis).

von_hand() { return 2; }

namen=(); ist_cmds=(); setze_cmds=(); hinweise=()
schritt() { # <name> <ist-befehl> <setze-befehl> [hinweis]
    namen+=("$1"); ist_cmds+=("$2"); setze_cmds+=("$3"); hinweise+=("${4:-}")
}

ist_paket() { dpkg-query -W -f='${Status}' "$1" 2>/dev/null | grep -q 'install ok installed'; }
setze_paket() {
    apt-get install -y -q "$1" || { apt-get update -q && apt-get install -y -q "$1"; }
}

ist_nutzer() { [[ "$(getent passwd "$NUTZER" | cut -d: -f6,7)" == "$NUTZER_HOME:/bin/bash" ]]; }
setze_nutzer() {
    if id "$NUTZER" >/dev/null 2>&1; then
        usermod -d "$NUTZER_HOME" -s /bin/bash "$NUTZER"
        mkdir -p "$NUTZER_HOME"
        setze_rechte "$NUTZER_HOME" 750 "$NUTZER"
    else
        useradd --system --user-group --create-home --home-dir "$NUTZER_HOME" --shell /bin/bash "$NUTZER"
    fi
}

ist_ssh() {
    [[ -f "$SSH_SCHLUESSEL" && -f "$SSH_SCHLUESSEL.pub" ]] && rechte_ok "$SSH_SCHLUESSEL" 600 "$NUTZER"
}
setze_ssh() {
    install -d -m 700 -o "$NUTZER" -g "$NUTZER" "$NUTZER_HOME/.ssh"
    if [[ ! -f "$SSH_SCHLUESSEL" ]]; then
        runuser -u "$NUTZER" -- ssh-keygen -q -t ed25519 -N '' -C haelpmi-orchestrator -f "$SSH_SCHLUESSEL"
    fi
    setze_rechte "$SSH_SCHLUESSEL" 600 "$NUTZER"
}

ist_runner() {
    [[ -x "$RUNNER_DIR/config.sh" && -x "$RUNNER_DIR/bin/Runner.Listener" ]] \
        && [[ "$(cat "$RUNNER_MARKER" 2>/dev/null)" == "$RUNNER_SHA256" ]] \
        && [[ "$(stat -c %U "$RUNNER_DIR")" == "$NUTZER" ]]
}
setze_runner() {
    local tmp
    tmp="$(mktemp -d)"
    trap 'rm -rf "$tmp"' EXIT
    curl -fsSL --retry 3 -o "$tmp/runner.tar.gz" "$RUNNER_URL"
    if ! echo "$RUNNER_SHA256  $tmp/runner.tar.gz" | sha256sum -c --quiet -; then
        echo "SHA256 des Runner-Archivs weicht ab, Abbruch." >&2
        return 1
    fi
    install -d -m 755 "$RUNNER_DIR"
    tar -xzf "$tmp/runner.tar.gz" -C "$RUNNER_DIR"
    echo "$RUNNER_SHA256" > "$RUNNER_MARKER"
    chown -R "$NUTZER:$NUTZER" "$RUNNER_DIR"
}

ist_runner_abh() {
    dpkg-query -W -f='${Status}\n' 'libicu[0-9]*' 2>/dev/null | grep -q 'install ok installed' \
        && ist_paket libkrb5-3
}
setze_runner_abh() {
    [[ -x "$RUNNER_DIR/bin/installdependencies.sh" ]] || return 1
    "$RUNNER_DIR/bin/installdependencies.sh"
}

ist_registriert() { [[ -f "$RUNNER_DIR/.runner" ]]; }
setze_registriert() {
    [[ -n "${RUNNER_TOKEN:-}" ]] || return 2
    cd "$RUNNER_DIR"
    runuser -u "$NUTZER" -- ./config.sh --url "$REPO_URL" --token "$RUNNER_TOKEN" \
        --name haelpmi-orch --labels pve-orchestrator --work _work --unattended
}

ist_dienst() {
    [[ -f "$RUNNER_DIR/.service" ]] && systemctl is-active --quiet "$(cat "$RUNNER_DIR/.service")"
}
setze_dienst() {
    ist_registriert || return 2
    cd "$RUNNER_DIR"
    [[ -f .service ]] || ./svc.sh install "$NUTZER"
    ./svc.sh start
}

ist_ci_etc() { rechte_ok "$CI_ETC" 700 "$NUTZER"; }
setze_ci_etc() {
    install -d "$CI_ETC"
    setze_rechte "$CI_ETC" 700 "$NUTZER"
}

ist_pve_token() {
    [[ ! -e "$CI_ETC/pve-token.json" ]] \
        && rechte_ok "$CI_ETC/pve-token" 600 "$NUTZER" \
        && grep -Eq '^PVEAPIToken=[^=]+![^=]+=[0-9a-f-]+$' "$CI_ETC/pve-token"
}
setze_pve_token() {
    local json="$CI_ETC/pve-token.json" id wert
    if [[ -f "$json" ]]; then
        id="$(jq -er '."full-tokenid"' "$json")"
        wert="$(jq -er '.value' "$json")"
        (umask 077 && printf 'PVEAPIToken=%s=%s\n' "$id" "$wert" > "$CI_ETC/pve-token.neu")
        mv "$CI_ETC/pve-token.neu" "$CI_ETC/pve-token"
        shred -u "$json" 2>/dev/null || rm -f "$json"
    fi
    [[ -f "$CI_ETC/pve-token" ]] || return 2
    setze_rechte "$CI_ETC/pve-token" 600 "$NUTZER"
}

ist_mqtt() { rechte_ok "$CI_ETC/mqtt.conf" 600 "$NUTZER"; }
setze_mqtt() {
    if [[ ! -e "$CI_ETC/mqtt.conf" ]]; then
        (umask 077 && printf 'MQTT_HOST=10.10.31.9\nMQTT_PORT=1883\nMQTT_USER=\nMQTT_PASS=\n' > "$CI_ETC/mqtt.conf")
    fi
    setze_rechte "$CI_ETC/mqtt.conf" 600 "$NUTZER"
}

ist_mqtt_gefuellt() {
    [[ -n "$(conf_wert "$CI_ETC/mqtt.conf" MQTT_USER)" && -n "$(conf_wert "$CI_ETC/mqtt.conf" MQTT_PASS)" ]]
}

soll_api() {
    local host="${pve_host:-}"
    [[ -n "$host" ]] || { conf_wert "$CI_ETC/pve.conf" PVE_API; return; }
    [[ "$host" == *:* && "$host" != \[* ]] && host="[$host]"
    echo "https://$host:8006"
}

ist_pve_conf() {
    local api
    api="$(conf_wert "$CI_ETC/pve.conf" PVE_API)"
    [[ "$api" =~ ^https://[^/]+:8006$ && "$api" == "$(soll_api)" ]] \
        && [[ -n "$(conf_wert "$CI_ETC/pve.conf" PVE_NODE)" ]] \
        && rechte_ok "$CI_ETC/pve.conf" 600 "$NUTZER"
}
setze_pve_conf() {
    local api node tmp status k kandidaten=()
    api="$(soll_api)"
    [[ -n "$api" ]] || return 2
    node="${PVE_NODE:-$(conf_wert "$CI_ETC/pve.conf" PVE_NODE)}"
    if [[ -z "$node" && -f "$CI_ETC/pve-token" ]]; then
        tmp="$(mktemp)"
        trap 'rm -f "$tmp"' EXIT
        status="$(pve_get "$api" /nodes "$tmp" || true)"
        if [[ "$status" == 200 ]]; then
            mapfile -t kandidaten < <(jq -r '.data[].node' "$tmp")
            if [[ ${#kandidaten[@]} -eq 1 ]]; then
                node="${kandidaten[0]}"
            else
                # Cluster: der Node, auf dem die erste Test-VM liegt.
                for k in "${kandidaten[@]}"; do
                    status="$(pve_get "$api" "/nodes/$k/qemu/${TEST_VMIDS[0]}/status/current" "$tmp" || true)"
                    [[ "$status" == 200 ]] && { node="$k"; break; }
                done
            fi
        fi
    fi
    (umask 077 && printf 'PVE_API=%s\nPVE_NODE=%s\n' "$api" "$node" > "$CI_ETC/pve.conf")
    setze_rechte "$CI_ETC/pve.conf" 600 "$NUTZER"
    [[ -n "$node" ]] || return 2
}

ist_skript() { # <name>
    local ziel="$CI_OPT/$1"
    [[ -f "$ziel" ]] || return 1
    ! grep -qF "$WACHHALTEN_PLATZHALTER" "$ziel" || return 1
    [[ "$(stat -c '%a %U' "$ziel")" == "755 root" ]] || return 1
    # Ohne Quelle neben diesem Skript (z. B. --pruefen aus einem anderen Verzeichnis)
    # lässt sich nur das Vorhandensein prüfen.
    [[ ! -f "$QUELLE/$1" ]] || cmp -s "$QUELLE/$1" "$ziel"
}
setze_skript() { # <name>
    [[ -f "$QUELLE/$1" ]] || return 2
    install -d -m 755 "$CI_OPT"
    install -m 755 -o root -g root "$QUELLE/$1" "$CI_OPT/$1"
}

ist_ci_state() { rechte_ok "$CI_STATE" 750 "$NUTZER"; }
setze_ci_state() {
    install -d "$CI_STATE"
    setze_rechte "$CI_STATE" 750 "$NUTZER"
}

ist_e2e_conf() { rechte_ok "$CI_ETC/e2e.conf" 600 "$NUTZER"; }
setze_e2e_conf() {
    if [[ ! -e "$CI_ETC/e2e.conf" ]]; then
        (umask 077 && printf '%s' "$E2E_CONF_VORLAGE" > "$CI_ETC/e2e.conf")
    fi
    setze_rechte "$CI_ETC/e2e.conf" 600 "$NUTZER"
}

ist_e2e_conf_gefuellt() {
    local id
    for id in 9101 9102 9103; do
        [[ -n "$(conf_wert "$CI_ETC/e2e.conf" "VM_${id}_IP")" ]] || return 1
    done
}

ist_unit() { inhalt_gleich "$SYSTEMD_DIR/$UNIT.$1" "$2"; }
setze_unit() {
    printf '%s' "$2" > "$SYSTEMD_DIR/$UNIT.$1"
    systemctl daemon-reload
}
ist_service() { ist_unit service "$SERVICE_INHALT"; }
setze_service() { setze_unit service "$SERVICE_INHALT"; }
ist_timer() { ist_unit timer "$TIMER_INHALT"; }
setze_timer() { setze_unit timer "$TIMER_INHALT"; }

ist_timer_aktiv() { systemctl is-enabled --quiet "$UNIT.timer" && systemctl is-active --quiet "$UNIT.timer"; }
setze_timer_aktiv() { systemctl enable --now "$UNIT.timer"; }

for p in "${PAKETE[@]}"; do
    schritt "Paket $p" "ist_paket $p" "setze_paket $p"
done
schritt "Benutzer $NUTZER (Home $NUTZER_HOME, bash)" ist_nutzer setze_nutzer
schritt "SSH-Schlüssel $SSH_SCHLUESSEL" ist_ssh setze_ssh
schritt "Runner $RUNNER_VERSION in $RUNNER_DIR (SHA256 geprüft)" ist_runner setze_runner
schritt "Runner-Abhängigkeiten (installdependencies.sh)" ist_runner_abh setze_runner_abh
schritt "Runner registriert (haelpmi-orch, pve-orchestrator)" ist_registriert setze_registriert \
    "RUNNER_TOKEN setzen (gh api -X POST repos/it-scholle/HaelpMi/actions/runners/registration-token --jq .token) und erneut starten"
schritt "Runner-Dienst aktiv" ist_dienst setze_dienst "erst nach der Registrierung möglich"
schritt "$CI_ETC (700, $NUTZER)" ist_ci_etc setze_ci_etc
schritt "$CI_ETC/pve-token (600, $NUTZER)" ist_pve_token setze_pve_token \
    "auf dem Test-Host: pveum user token add haelpmi-ci@pve orchestrator --privsep 1 --output-format json, Ausgabe nach $CI_ETC/pve-token.json legen, erneut starten"
schritt "$CI_ETC/mqtt.conf (600, $NUTZER)" ist_mqtt setze_mqtt
schritt "$CI_ETC/mqtt.conf ausgefüllt" ist_mqtt_gefuellt von_hand \
    "MQTT_USER und MQTT_PASS von Hand eintragen"
schritt "$CI_ETC/pve.conf (PVE_API, PVE_NODE)" ist_pve_conf setze_pve_conf \
    "Adresse per --pve-host oder PVE_HOST angeben; Node notfalls per PVE_NODE"
schritt "$CI_ETC/e2e.conf (600, $NUTZER)" ist_e2e_conf setze_e2e_conf
schritt "$CI_ETC/e2e.conf: VM_9101_IP bis VM_9103_IP" ist_e2e_conf_gefuellt von_hand \
    "Adressen der Test-VMs von Hand eintragen"
schritt "$CI_STATE (750, $NUTZER)" ist_ci_state setze_ci_state
for s in "${CI_SKRIPTE[@]}"; do
    schritt "$CI_OPT/$s (755, aktuell)" "ist_skript $s" "setze_skript $s" \
        "$s aus ci/e2e neben dieses Skript legen und erneut starten"
done
schritt "$UNIT.service" ist_service setze_service
schritt "$UNIT.timer" ist_timer setze_timer
schritt "$UNIT.timer aktiviert und aktiv" ist_timer_aktiv setze_timer_aktiv

# --- API-Test -----------------------------------------------------------------------------

api_testen() {
    local api node tmp status fehler=0
    api="$(conf_wert "$CI_ETC/pve.conf" PVE_API)"
    node="$(conf_wert "$CI_ETC/pve.conf" PVE_NODE)"
    if [[ -z "$api" || -z "$node" || ! -f "$CI_ETC/pve-token" ]]; then
        echo "pve.conf (PVE_API, PVE_NODE) oder pve-token fehlt, erst einrichten." >&2
        exit 1
    fi
    tmp="$(mktemp)"
    trap 'rm -f "$tmp"' EXIT

    for vmid in "${TEST_VMIDS[@]}"; do
        status="$(pve_get "$api" "/nodes/$node/qemu/$vmid/status/current" "$tmp" || true)"
        if [[ "$status" == 200 ]]; then
            printf 'ok             Test-VM %s: 200 (%s, %s)\n' "$vmid" \
                "$(jq -r '.data.name // "-"' "$tmp")" "$(jq -r '.data.status // "-"' "$tmp")"
        else
            echo "ABWEICHUNG     Test-VM $vmid: HTTP $status, erwartet 200"
            fehler=1
        fi
    done

    status="$(pve_get "$api" "/nodes/$node/qemu/$FREMDE_VMID/status/current" "$tmp" || true)"
    if [[ "$status" == 403 ]]; then
        echo "ok             fremde VM $FREMDE_VMID: 403"
    else
        echo "ABWEICHUNG     fremde VM $FREMDE_VMID: HTTP $status, erwartet 403"
        fehler=1
    fi
    exit "$fehler"
}

if [[ $api_test -eq 1 ]]; then
    api_testen
fi

# --- Ausführen ----------------------------------------------------------------------------

erfuellt=0; gesetzt=0; offen=0
for i in "${!namen[@]}"; do
    read -ra ist <<< "${ist_cmds[$i]}"
    read -ra setze <<< "${setze_cmds[$i]}"
    if "${ist[@]}" 2>/dev/null; then
        status=ok
    elif [[ $pruefen -eq 1 ]]; then
        status=ABWEICHUNG
    else
        # Eigene Subshell mit set -e, sonst bricht ein fehlschlagender Befehl in setze_* nicht ab.
        set +e
        ( set -e; "${setze[@]}" )
        rc=$?
        set -e
        if [[ $rc -eq 2 ]]; then status='VON HAND'
        elif [[ $rc -ne 0 ]]; then status=FEHLER
        elif "${ist[@]}" 2>/dev/null; then status=gesetzt
        else status=FEHLER
        fi
    fi

    case "$status" in
        ok) erfuellt=$((erfuellt + 1)); text='war schon so' ;;
        gesetzt) gesetzt=$((gesetzt + 1)); text=gesetzt ;;
        *) offen=$((offen + 1)); text="$status" ;;
    esac
    printf '%-14s %s\n' "$text" "${namen[$i]}"
    if [[ "$status" == 'VON HAND' || "$status" == ABWEICHUNG ]] && [[ -n "${hinweise[$i]}" ]]; then
        printf '               → %s\n' "${hinweise[$i]}"
    fi
done

echo
printf '%d Schritte: %d schon erfüllt, %d gesetzt, %d offen\n' "${#namen[@]}" "$erfuellt" "$gesetzt" "$offen"

if [[ -f "$SSH_SCHLUESSEL.pub" ]]; then
    echo
    echo "Öffentlicher SSH-Schlüssel des Orchestrators:"
    cat "$SSH_SCHLUESSEL.pub"
fi

[[ $offen -eq 0 ]] || exit 1
