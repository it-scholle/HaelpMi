# 0004 – Branch-Modell mit rotierenden Release-Branches

- Datum: 2026-08-15 (Branch-Pflicht), 2026-08-20 (Auto-Merge ausgesetzt), 2026-08-24 (rotierender Default-Branch, Ticket-Kopplung, ein Ticket = ein Commit)
- Status: angenommen; Angleichung an die MasterAgents-Vorlage vorgeschlagen in 0010
- Entschieden von: Repo-Owner

## Kontext
- Direkte Commits auf `main` mitten in unfertiger Arbeit haben das laufende System gefährdet.
- Ein im Worktree fertiggestellter Fix (v0.7.6) wurde nie zurückgeführt, während eine andere
  Session auf demselben Vorgänger weiterarbeitete; der Fix fehlte danach auf `main`.
- Sessions fragten trotz bestehender Regel immer wieder nach, ob gemergt werden soll.
- Nach dem Neustart (0001) sollte `main` nur noch geprüfte Stände enthalten.
- `Fixes #N` schließt ein Issue nur bei Merge in den GitHub-Default-Branch.
- Bei Rollbacks über mehrere Versionen müssen sich einzelne Ticket-Änderungen getrennt
  zurücknehmen oder übernehmen lassen.

## Entscheidung
- Jede Aufgabe, interaktiv wie im Hintergrund, beginnt auf einem eigenen Branch; nie ein Commit
  direkt auf `main`. Granularität ist die Aufgabe, nicht der einzelne Commit.
- Integration per Rebase auf den Ziel-Branch und `merge --ff-only` (lineare Historie). Bis
  20.08.2026 geschah das automatisch bei grünen Tests inklusive Push; seitdem erst nach Freigabe
  durch den Repo-Owner.
- Pro Release-Zyklus wird `release-X.Y[-Kurzname]` von `main` abgezweigt und für die Dauer des
  Zyklus zum GitHub-Default-Branch (`gh repo edit --default-branch`). Zyklusende: FF-Merge in
  `main`, Tag, Default-Branch auf den nächsten Release-Branch bzw. zurück auf `main`.
- Branches werden aus dem Issue erzeugt (`gh issue develop`); jedes Ticket landet als eigener,
  unvermischter Commit.
- Kein automatischer Force-Push; schlägt ein Push fehl, wird nachgefragt.

## Folgen
- `main` ist durchgehend ein getesteter Stand, aber nicht durchgehend der Default-Branch.
- Mechanik im Detail: `docs/wissen/git-workflow.md`.
- Weicht von Teil A der MasterAgents-Vorlage ab (`develop`, Squash, Conventional Commits); siehe
  `AGENTS.md` Teil B, „Ausnahmen von Teil A“.
