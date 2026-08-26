#!/usr/bin/env bash
# Markiert ein GitHub-Issue als "wird gerade bearbeitet" (status:in-progress),
# damit keine andere parallele Session am selben Issue arbeitet. Entfernt dabei
# ein eventuell noch vorhandenes status:review (Fall: Nutzer fordert Nacharbeit
# an einem bereits als fertig gemeldeten Issue an). Siehe CLAUDE.md, Abschnitt
# "Issue-Workflow für parallele Sessions".
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: $0 <issue-nummer>" >&2
  exit 1
fi

ISSUE="$1"

gh issue edit "$ISSUE" --remove-label "status:review" --add-label "status:in-progress"
echo "Issue #$ISSUE als in Bearbeitung markiert (status:in-progress gesetzt)."
