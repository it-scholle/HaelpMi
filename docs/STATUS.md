# Status

Stand: 2026-11-18

## Aktueller Stand

- Aktive Linie: `release-1.0-MVP` (GitHub-Default-Branch), Version 0.47.x. Status
  „stable+buggy“: Alarmversand/-empfang, Selbsttest, Config-Sync, Discovery/Boot-Call und
  Install-Creator/Installer funktionieren weitgehend, ohne Anspruch auf Produktionsreife.
- `main` ist der eingefrorene, getestete Stable-Anker; `legacy/main-0.39.x` ist der alte,
  nicht mehr weiterentwickelte Stand (siehe `docs/entscheidungen/0001-neustart-auf-v0-16.md`).
- Update-Pipeline (Update-Dienst, P2P-Verteilung) ist in dieser Linie inaktiv; Updates erfolgen
  manuell per Installer.
- CI (Windows-Build + Tests), gitleaks und Dependabot: aktiv seit v0.47.1 (#145).
- Struktur an die MasterAgents-Vorlage angeglichen (v0.47.4, #151); die Ausnahmen von Teil A
  sind vom Repo-Owner am 2026-11-18 entschieden (`AGENTS.md` Teil B).

## Offene Annahmen
<!-- Jede mit Fundstelle (Datei oder Ticket). Sonst „keine“. -->

- keine `ANNAHME:`-Marker im Code.

## Bekannte Abweichungen von `AGENTS.md` Teil A

Nicht als Ausnahme freigegeben, Umsetzung steht noch aus:

- Audit-Log ohne Hash-Kette, Aufbewahrung und Schutz (`src/HaelpMi.Core/Storage/AuditLog.cs`).
- Betriebs-Log ohne Stufen, Tagesdateien, Rotation und Aufbewahrung
  (`src/HaelpMi.Core/Diagnostics/CrashLogger.cs`).
- Feste Werte im Code (z. B. Log-Laufwerk `Z:` in `SharedLogPaths.cs`, Ports in
  `AppConstants.cs`, dort bewusst fest).
- Kein Linter/Formatierer konfiguriert.

## Offene Funde aus #151

Bei #151 aufgefallen, nicht Teil des Tickets, jeweils als eigenes Issue angelegt:

- Barrierefreiheit: Das Admin-Dashboard unterscheidet Zustände nur über Farbe („grün =
  zugeordnet“, „grün = Mitglied“). Teil A verlangt: Information nie nur über Farbe. (#157)
- `scripts/*.sh` sind nicht ausführbar (Mode 100644); nur `bash scripts/…` funktioniert. (#158)
- `docs/wissen/test-strategie.md` nennt `tests/HaelpMi.Audio.Tests` (nur im Legacy-Stand),
  `tests/HaelpMi.InstallCreator.Tests` fehlt dort. (#159)
- Repo-Ruleset „Block-all“: GitHub meldet beim Push „Commits must have verified signatures“
  und „Changes must be made through a pull request“, nimmt den Push per Bypass aber an.
  Commits auf `release-1.0-MVP` sind unsigniert. (#160)
- `.ico` ohne 20/24/40/64-px-Stufen (siehe `design/KATALOG.md`). (#161)
- Linter/Formatierer (`.editorconfig` + `dotnet format`) einrichten: eigenes Ticket, weil im
  Bestandscode viele Funde zu erwarten sind. (#162)

## Nächste Schritte

- Nach Abschluss von `release-1.0-MVP`: Branch-Modell auf `develop`/`main` umstellen
  (ADR 0010), dazu Commit-Format und Merge-Art nach Teil A.
- Weitere Tickets im Milestone `release-1.0-MVP`.
