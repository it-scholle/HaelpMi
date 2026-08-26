#!/usr/bin/env bash
# Gibt ein GitHub-Issue wieder frei (entfernt status:in-progress), z. B. bei
# Abbruch/Unterbrechung der Arbeit. Optional wird ein Grund als Kommentar
# hinterlassen (z. B. "wartet auf #123"). Schließt das Issue NICHT - das
# passiert nur beim tatsächlichen Abschluss, siehe CLAUDE.md, Abschnitt
# "Issue-Workflow für parallele Sessions".
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: $0 <issue-nummer> [grund-kommentar]" >&2
  exit 1
fi

ISSUE="$1"
GRUND="${2:-}"

gh issue edit "$ISSUE" --remove-label "status:in-progress"

if [ -n "$GRUND" ]; then
  gh issue comment "$ISSUE" --body "$GRUND"
fi

echo "Issue #$ISSUE wieder freigegeben (status:in-progress entfernt)."
