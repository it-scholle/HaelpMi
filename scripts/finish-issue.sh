#!/usr/bin/env bash
# Meldet ein GitHub-Issue als fertig aus Sicht der Session: entfernt
# status:in-progress, setzt status:review. Schließt das Issue NICHT - das
# passiert erst nach Bestätigung/Abnahme durch den Nutzer, siehe CLAUDE.md,
# Abschnitt "Issue-Workflow für parallele Sessions".
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: $0 <issue-nummer> [zusammenfassung]" >&2
  exit 1
fi

ISSUE="$1"
ZUSAMMENFASSUNG="${2:-}"

gh issue edit "$ISSUE" --remove-label "status:in-progress" --add-label "status:review"

if [ -n "$ZUSAMMENFASSUNG" ]; then
  gh issue comment "$ISSUE" --body "$ZUSAMMENFASSUNG"
fi

echo "Issue #$ISSUE auf status:review gesetzt - wartet auf Bestätigung/Abnahme."
