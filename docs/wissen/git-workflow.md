# Git-Ablauf in HälpMi

Anleitung zur Mechanik. Die gültigen Regeln stehen in `AGENTS.md` (Teil A „Branches, Commits,
Merge“ und „Release und Hotfix“, Teil B „Ausnahmen von Teil A“), die Begründung in
`docs/entscheidungen/0010-vorschlag-branch-modell-develop-main.md`.

## Branches

| Branch        | Zweck                                                                 |
|---------------|-----------------------------------------------------------------------|
| `develop`     | Integration, GitHub-Default. Ticket-PRs zielen hierhin.               |
| `release-X.Y` | Stabilisierung vor dem Release (aktuell `release-2.0`). Ruht, bis der Milestone-Umfang in `develop` erledigt ist. |
| `main`        | Nur Releases, mit Tag. Kein direkter Push (Hook `pre-push`).          |
| `hotfix/…`    | Schwere Fehler im Release, von `main`.                                |

Der Name des Release-Zyklus steht im Milestone (z. B. `release-2.0-…`), nicht im Ziel-Branch der
Ticket-PRs.

## Ablauf pro Ticket

1. Issue prüfen (kein `status:in-progress`/`status:review`), `bash scripts/claim-issue.sh <nr>`.
2. Branch aus dem Issue: `gh issue develop <nr> --base develop --name <präfix>/<nr>-<kurz>`,
   dann eigener Worktree (`EnterWorktree` oder `git worktree add`).
3. Arbeiten und committen nach Conventional Commits: `typ: Beschreibung (#N)`
   (`feat`, `fix`, `docs`, `chore`, `refactor`, `test`). Die Version setzt erst der Merge
   (Schritt 7). Signierte Commits sind nur auf `develop` und `main` Pflicht; den Squash-Merge
   signiert GitHub. Auf Ticket-Branches meldet GitHub beim Push „Commits must have verified
   signatures“, nimmt den Push aber an, bis das Ruleset angepasst ist.
   Berührt ein Commit `src/HaelpMi.InstallCreator/`, `src/HaelpMi.UpdateSigner/` oder
   `Directory.Build.props`, baut der Hook den Install-Creator neu
   (`docs/wissen/installcreator-rebuild-hook.md`).
4. Branch regelmäßig pushen: `git push -u origin <branch>`.
5. Vor dem PR den aktuellen Stand holen: `git fetch && git merge origin/develop`. Kein Rebase,
   kein Force-Push.
6. Tests ausführen (`AGENTS.md`, „Tests“), PR nach `develop` mit `Closes #N` öffnen,
   `bash scripts/finish-issue.sh <nr>`; auf OK des Auftraggebers warten.
7. Nach dem OK (`AGENTS.md`, „Merge durch den Agent“): `develop` erneut einmergen, nächste
   freie PATCH-Version auf `develop` in `Directory.Build.props` und
   `installer/HaelpMiCommon.iss.inc` setzen (bis #168), PR-Titel `vX.Y.Z: Beschreibung (#N)`,
   CI grün, Squash-Merge mit diesem Titel (`gh pr merge <pr> --squash --subject "<Titel>"`),
   danach `git tag vX.Y.Z <merge-commit> && git push origin vX.Y.Z` und den Branch lokal und
   remote löschen. Der Merge schließt das Issue.
8. Aufräumen: `git worktree remove <pfad>`, `git branch -D <branch>`.

## Release

1. Milestone-Umfang in `develop` erledigt: `develop` per Fast-Forward nach `release-X.Y`
   übernehmen (PR `develop` → `release-X.Y`).
2. Auf `release-X.Y` nur noch Fehlerbehebungen: Ticket-Branch von `release-X.Y`, PR dorthin.
3. Repo-Owner testet `release-X.Y` und gibt frei.
4. Release-Version aus den Commits seit dem letzten Release ableiten, in `Directory.Build.props`
   und `installer/HaelpMiCommon.iss.inc` setzen (bis #168), `CHANGELOG.md` ergänzen.
5. PR `release-X.Y` → `main`, Merge oder Fast-Forward (nie Squash). Den Release-Tag und das
   GitHub-Release mit den CHANGELOG-Notizen setzt der Repo-Owner.
6. `release-X.Y` in `develop` mergen. Nächster Zyklus: neuer Milestone und Branch
   `release-X.Y+1` vom aktuellen `develop`.

## Fehlschläge

- Tests rot: bis zu 3 Korrekturversuche, danach nachfragen statt raten.
- Push abgelehnt (Auth, nicht fast-forward): nie force-pushen, Remote-Stand einmergen oder
  nachfragen.

## Status-Tabelle im Chat

Bei aktiver Branch-, Versions- oder Git-Arbeit (nicht bei reinen Rückfragen), ohne Fettdruck:

```
----------------------------------------------------------------------
| Feld            | Wert                                             |
----------------------------------------------------------------------
| Bereich         | <Feature/Bereich>                                |
| Branch          | <Branch-Name oder "-">                           |
| Ziel            | <develop | release-X.Y | main>                    |
| Status          | Klären | Implementieren | Testen | PR | Fertig  |
----------------------------------------------------------------------
Zusammenfassung: ...
Offene Fragen: ... (oder "keine")
----------------------------------------------------------------------
```

## Vorgeschichte

- 09.08.2026: Ein im Worktree fertiger Fix (v0.7.6) wurde nie zurückgeführt. Seitdem
  verpflichtender Merge-Ablauf.
- 15.08.2026: Branch-Pflicht für alle Sessions, auch interaktive.
- 20.08.2026: automatische Integration bei grünen Tests ausgesetzt, Merge nur nach Freigabe.
- 24.08.2026 – 19.11.2026: rotierende Release-Branches mit Versionsnummer pro Ticket-Commit
  (ADR 0004), letzter Zyklus `release-1.0-MVP` bis v0.47.11.
- 19.11.2026: Umstellung auf `develop`/`main` mit Stabilisierungs-Branch `release-X.Y` (ADR 0010, #194).
- 24.11.2026: PATCH-Version und Tag pro Merge nach `develop` bleiben; Signaturpflicht nur auf
  `develop`/`main` (Repo-Owner, #204 Punkte 11 und 12).
