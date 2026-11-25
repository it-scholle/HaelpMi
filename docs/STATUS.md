# Status

Stand: 2026-11-25

## Aktueller Stand

- `release-1.0-MVP` ist mit v0.47.11 abgeschlossen, ein Release gibt es daraus nicht. Entwicklung
  läuft auf `develop` (GitHub-Default) mit PATCH-Version und Tag pro Merge. Ziel ist Milestone
  `v1.0` (geplant 08.01.2027); vorher kommt der Hotfix v0.46.2-rc.1 (#210). Stabilisierungs-Branch
  `release-X.Y` nach #194 und ADR 0010. Status „stable+buggy“: Alarmversand/-empfang, Selbsttest, Config-Sync, Discovery/Boot-Call und
  Install-Creator/Installer funktionieren weitgehend, ohne Anspruch auf Produktionsreife.
- `main` nimmt nur noch Releases auf und steht noch auf v0.16.5 (1.0 wurde bisher nicht nach
  `main` übernommen); `legacy/main-0.39.x` ist der alte,
  nicht mehr weiterentwickelte Stand (siehe `docs/entscheidungen/0001-neustart-auf-v0-16.md`).
- Update-Pipeline (Update-Dienst, P2P-Verteilung) ist in dieser Linie inaktiv; Updates erfolgen
  manuell per Installer.
- CI (Windows-Build + Tests), gitleaks und Dependabot: aktiv seit v0.47.1 (#145).
- Ziel-Framework .NET 10 (`net10.0-windows`, LTS bis 14.11.2028) statt .NET 8 (Support seit
  10.11.2026 beendet), #154.
- Struktur an die MasterAgents-Vorlage angeglichen (v0.47.4, #151); die Ausnahmen von Teil A
  sind vom Repo-Owner am 2026-11-18 entschieden (`AGENTS.md` Teil B).

## Offene Annahmen
<!-- Jede mit Fundstelle (Datei oder Ticket). Sonst „keine“. -->

- #73 (PR #220): Ursache des Original-Vorfalls (v0.34.1) nicht per Log belegt, Logs gibt es nicht
  mehr. Der Fix behebt die im Code belegten Wege (veraltete Adresse in der laufenden Session,
  Gossip überschreibt direkt gemeldete Adresse, Boot-Call vor Alarm-Port). Andere Ursachen aus dem
  Ticket (Mutex, Abbruch von `StartBackgroundServices`) sind damit nicht ausgeschlossen.

## Bekannte Abweichungen von `AGENTS.md` Teil A

Nicht als Ausnahme freigegeben, Umsetzung steht noch aus:

- Audit-Log ohne Hash-Kette, Aufbewahrung und Schutz (`src/HaelpMi.Core/Storage/AuditLog.cs`).
- Betriebs-Log (`BetriebsLog.cs`, #227) bisher nur mit Start, Ende, Startfehlern und unbehandelten
  Ausnahmen; die übrigen Module schreiben noch nicht hinein (erstes Modul: #214).
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
- Repo-Ruleset „Block-all“ (alle Branches außer `develop`): GitHub meldet beim Push auf
  Ticket-Branches „Changes must be made through a pull request“ und beim Löschen „Cannot delete
  this branch“, nimmt beides per Owner-Bypass aber an. Signaturpflicht nur noch auf `main`
  (Ruleset „Signaturen main“), `develop` verlangt PR und grüne Pflicht-Checks ohne Approval
  (Ruleset „develop: Checks statt Approval“); Entscheidungen 11, 19 und 20 in #204. (#160)
- `.ico` ohne 20/24/40/64-px-Stufen (siehe `design/KATALOG.md`). (#161)
- Linter/Formatierer (`.editorconfig` + `dotnet format`) einrichten: eigenes Ticket, weil im
  Bestandscode viele Funde zu erwarten sind. (#162)

## Nächste Schritte

- Version 1.0 nach `main` übernehmen und taggen (Freigabe Repo-Owner offen).
- Rulesets an das neue Modell anpassen (#160): „Block-all“ verlangt lineare Historie auf allen
  Branches außer `develop`, das Ruleset für `develop` ebenfalls. Teil A und `release-X.Y` brauchen
  aber Merge-Commits (`release-X.Y` bzw. `main` → `develop`, `release-X.Y` → `main`) und einen
  Fast-Forward `develop` → `release-X.Y`, den ein GitHub-PR nicht kann. Vor dem ersten Release klären.
- Tickets im Milestone `v1.0`, danach `Regelentwicklung`; zurückgestellte Themen in `release-2.0-…` bzw. `Backlog`.
