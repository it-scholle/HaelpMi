# Git-Ablauf in HälpMi

Anleitung zur Mechanik. Die gültigen Regeln stehen in `AGENTS.md` (Teil A und Teil B, „Ausnahmen
von Teil A“), die Begründung in `docs/entscheidungen/0004-branch-modell-release-zyklen.md`.

## Versionierung

Format `MAJOR.MINOR.PATCH`, ein einziger Strom für das ganze Produkt. Quelle: `<Version>` in
`Directory.Build.props`, gespiegelt in `MyAppVersion` in `installer/HaelpMiCommon.iss.inc`; beide
bei jedem Sprung von Hand synchron halten.

- PATCH: Bugfix, Doku, Refactoring ohne Verhaltensänderung
- MINOR: neues Feature oder dringender Produktions-Fix
- MAJOR: Breaking Change oder offizielles Release

Der Grad wird beim Commit entschieden; die Versionsnummer im Titel (`vX.Y.Z: Beschreibung (#N)`)
codiert ihn bereits.

## Ablauf pro Ticket

1. Issue prüfen (kein `status:in-progress`/`status:review`), `scripts/claim-issue.sh <nr>`.
2. Branch aus dem Issue: `gh issue develop <nr> --base <release-branch> --name <präfix>/<kurz>`,
   dann eigener Worktree (`EnterWorktree` oder `git worktree add`).
3. Arbeiten, committen, die für den Grad passende Test-Stufe ausführen (`AGENTS.md`,
   „Test-Stufen“). Berührt ein Commit `src/HaelpMi.InstallCreator/`, `src/HaelpMi.UpdateSigner/`
   oder `Directory.Build.props`, baut der Hook den Install-Creator neu
   (`docs/wissen/installcreator-rebuild-hook.md`).
4. Branch nach jedem Commit, mindestens einmal pro Aufgabe, pushen: `git push -u origin <branch>`.
5. Auf den aktuellen Ziel-Branch rebasen: `git rebase <release-branch>` (alle Worktrees teilen
   dieselben Refs). Konflikte hier lösen, siehe „Versionsnummer-Kollisionen“.
6. Nach dem Rebase erneut testen; ein konfliktfreier Rebase garantiert nicht, dass zwei
   Änderungen zusammen funktionieren.
7. PR öffnen, `scripts/finish-issue.sh <nr>`; auf OK des Repo-Owners warten.
8. Nach dem OK: prüfen, ob sich der Ziel-Branch inzwischen bewegt hat (dann zurück zu 5), Ticket
   als ein Commit integrieren (FF bzw. PR-Merge), Tag `vX.Y.Z` setzen, pushen.
9. Aufräumen: `git worktree remove <pfad>`, `git branch -d <branch>`,
   `git push origin --delete <branch>`.

`main` wird nur am Ende eines Release-Zyklus nach Freigabe des Repo-Owners per Fast-Forward aus
dem Release-Branch aktualisiert.

## Versionsnummer-Kollisionen

Haben zwei Branches parallel dieselbe Nummer gewählt, behält der zuerst gelandete Commit seine
Nummer, der später integrierte rückt auf die nächste freie (inklusive Tag). Nur den eigenen, gerade
erst angelegten Tag löschen, nie einen Tag, der schon vor dem eigenen Rebase existierte.

Ein bereits laufender, teilweise aufgelöster Merge wird zu Ende gebracht statt auf Rebase
umgestellt.

## Fehlschläge

- Tests rot: bis zu 3 Korrekturversuche, danach nachfragen statt raten.
- Push abgelehnt (Auth, nicht fast-forward): nie automatisch force-pushen, nachfragen.

## Status-Tabelle im Chat

Bei aktiver Branch-, Versions- oder Git-Arbeit (nicht bei reinen Rückfragen), ohne Fettdruck:

```
----------------------------------------------------------------------
| Feld            | Wert                                             |
----------------------------------------------------------------------
| Bereich         | <Feature/Bereich>                                |
| Branch          | <Branch-Name oder "-">                           |
| Version Ziel    | <vX.Y.Z des Ziel-Branches>                       |
| Version (diese) | <vX.Y.Z bzw. "-" wenn noch offen>                |
| Status          | Klären | Implementieren | Testen | Rebasen |
|                 | Mergen | Fertig                                  |
----------------------------------------------------------------------
Zusammenfassung: ...
Offene Fragen: ... (oder "keine")
----------------------------------------------------------------------
```

Grund: Der Ziel-Branch kann sich durch parallele Sessions zwischen zwei Antworten bewegen; die feste
Struktur hält fest, auf welchem Stand eine Antwort aufbaut.

## Vorgeschichte

- 09.08.2026: Ein im Worktree fertiger Fix (v0.7.6) wurde nie zurückgeführt; `main` sprang direkt
  zu einem anderen, parallelen Fix. Seitdem verpflichtender Rebase-/Merge-Ablauf.
- 14.08.2026: GitHub-Remote `origin`; Merges wurden bis 20.08.2026 automatisch gepusht.
- 15.08.2026: Branch-Pflicht für alle Sessions, auch interaktive.
- 20.08.2026: automatische Integration bei grünen Tests ausgesetzt, Merge nur nach Freigabe.
