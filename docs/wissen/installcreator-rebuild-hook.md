# Install-Creator-Rebuild-Hook

Regel: `AGENTS.md` Teil B, „Install-Creator“. Entscheidung:
`docs/entscheidungen/0005-installcreator-rebuild-per-hook.md`.

## Was der Hook tut

`tools/InstallCreator/HaelpMi.InstallCreator.exe` ist eine gitignorierte, veröffentlichte Kopie von
`src/HaelpMi.InstallCreator`. Die gemeinsame Logik in `.githooks/lib/rebuild-installcreator.sh`
bildet einen Hash über `src/HaelpMi.InstallCreator`, `src/HaelpMi.UpdateSigner` und
`Directory.Build.props` (auch ein reiner Versions-Bump zählt, weil das Tool seine Version im
Fenstertitel anzeigt) und vergleicht ihn mit `tools/InstallCreator/.source-hash` (gitignoriert).
Nur bei Abweichung läuft `dotnet publish` (Release, `win-x64`, self-contained, ReadyToRun);
danach wird der neue Hash gespeichert.

| Hook | Deckt ab |
|---|---|
| `pre-commit` | normaler `git commit`; darf bei einem erkannten Build-Race den Commit abbrechen (`REBUILD_ALLOW_ABORT=1`) |
| `post-rewrite` | `git commit --amend` und `git rebase` |
| `post-merge` | `git merge`/`git pull` im Haupt-Checkout, auch Fast-Forward |
| `post-checkout` | Branch- und Worktree-Wechsel |

- Die **Quelle** ist immer der gerade committende Checkout (`git rev-parse --show-toplevel`), auch
  in einem Worktree.
- Das **Ziel** ist immer `tools/InstallCreator/` im Haupt-Checkout (über den Pfad des
  Hook-Skripts), damit die Verknüpfung des Nutzers profitiert.
- Ein Hash- und Zeitstempel-Abgleich nach dem Build bricht den Commit ab, statt still ein falsches
  Artefakt zu hinterlassen.
- Ein fehlgeschlagenes `dotnet publish` (Umgebungsproblem) blockiert den Commit nicht, die Kopie
  bleibt dann veraltet.

## Aktivierung

Einmal pro unabhängigem Klon: `git config core.hooksPath .githooks`. Die Einstellung liegt in der
von allen Worktrees geteilten `.git/config` und greift dadurch auch in jedem künftigen Worktree.
Neue oder geänderte Hooks aus einem Feature-Branch wirken erst, wenn sie gemergt und im
Haupt-Checkout ausgecheckt sind.

`pre-commit` prüft die vorgemerkten Änderungen zusätzlich mit gitleaks auf Secrets und bricht
den Commit ab, wenn gitleaks fehlt (Windows: `winget install gitleaks`). Dieselbe Prüfung läuft
über die gesamte Historie in der CI (`.github/workflows/ci.yml`, Job "Secrets"), zusammen mit
Build und den systemneutralen Tests (`HaelpMi.Core.Tests`, `HaelpMi.InstallCreator.Tests`) auf
einem Windows-Runner. Ergebnis eines PRs: `gh pr checks <nr>`.

## Start.cmd / Start.ps1

Zweite, vom Hook unabhängige Absicherung für Menschen: `Start.ps1` holt höchstens alle 10 Minuten
den aktuellen Stand (`git fetch` + `git pull --ff-only`, nur bei sauberem Arbeitsbaum; Marker
`tools/InstallCreator/.last-pull-check`), vergleicht Quelle und Exe und baut bei Bedarf neu, bevor
das Tool startet. Ein direkter Start der `.exe` prüft das nicht. Bei Verdacht auf einen veralteten
Stand zuerst prüfen, ob die Desktop-Verknüpfung auf `Start.cmd` zeigt.

## Vorgeschichte

- 16.08.2026: Hook eingeführt, nachdem die Prosa-Regel mehrfach vergessen wurde (v0.29.2/v0.29.3
  liefen auf dem Stand v0.27.0).
- 17.08.2026: Ziel immer im Haupt-Checkout; `Directory.Build.props` wird mit beobachtet.
- 19.08.2026: Quelle und Ziel getrennt, nachdem aus einem veralteten Haupt-Checkout gebaut wurde.
- 26.08.2026: zustandsbasiert und über vier Hooks, weil Rebase und Amend `pre-commit` nicht
  zuverlässig auslösen.
- 27.08.2026: `Start.ps1` holt selbst den aktuellen Stand, gedrosselt auf alle 10 Minuten (ein
  Fetch kostet 5–10 s).
