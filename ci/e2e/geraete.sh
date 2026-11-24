#!/usr/bin/env bash
# Löst die Geräteauswahl eines E2E-Laufs (z. B. "a,b,c") über geraete.json auf.
#
#   geraete.sh pruefen <auswahl>   gibt die bereinigte Auswahl aus ("a,b,c"), leer = alle Geräte
#   geraete.sh vmids <auswahl>     VMIDs, durch Leerzeichen getrennt
#   geraete.sh relay <auswahl>     Argumente für rendezvous-relay.sh ("a=9101 b=9102 …")
#   geraete.sh matrix <auswahl>    JSON-Array der Geräte-Objekte für die Workflow-Matrix
#
# Jedes Gerät höchstens einmal, nur Geräte aus geraete.json, Reihenfolge wie dort.
# Exit 0 = ok, 2 = Aufruf oder Auswahl ungültig.
set -euo pipefail

GERAETE_JSON="${HAELPMI_GERAETE_JSON:-$(dirname "$(readlink -f "$0")")/geraete.json}"

aufruf() { echo "Aufruf: geraete.sh pruefen|vmids|relay|matrix <auswahl>" >&2; exit 2; }
[[ $# -eq 2 ]] || aufruf
modus="$1"
auswahl="${2// /}"

[[ -r "$GERAETE_JSON" ]] || { echo "$GERAETE_JSON nicht lesbar" >&2; exit 2; }
if [[ -z "$auswahl" ]]; then
    auswahl="$(jq -r '[.[].geraet] | join(",")' "$GERAETE_JSON")"
fi
[[ "$auswahl" =~ ^[a-z](,[a-z])*$ ]] || { echo "Ungültige Geräteauswahl: '$2' (erwartet z. B. a,b,c)" >&2; exit 2; }

gewaehlt="$(jq -c --arg auswahl "$auswahl" '
    ($auswahl | split(",")) as $w
    | if ($w | length) != ($w | unique | length) then error("Gerät doppelt gewählt")
      elif ($w - [.[].geraet] | length) > 0 then error("Unbekanntes Gerät: " + (($w - [.[].geraet]) | join(",")))
      else [.[] | select(.geraet as $g | $w | index($g))]
      end' "$GERAETE_JSON" 2>&1)" || { echo "${gewaehlt#jq: error (at *): }" >&2; exit 2; }

case "$modus" in
    pruefen) jq -r '[.[].geraet] | join(",")' <<< "$gewaehlt" ;;
    vmids) jq -r '[.[].vmid | tostring] | join(" ")' <<< "$gewaehlt" ;;
    relay) jq -r '[.[] | "\(.geraet)=\(.vmid)"] | join(" ")' <<< "$gewaehlt" ;;
    matrix) printf '%s\n' "$gewaehlt" ;;
    *) aufruf ;;
esac
