# Status

Stand: 2026-11-26

## Aktueller Stand

- Release-Kandidat 1.0 in Vorbereitung (#204, DoD 5; #241): `CHANGELOG.md` Abschnitt
  „[1.0.0] – unveröffentlicht“, Versionshinweise `docs/release-notes/1.0.0.md`, Handtest
  `docs/handbuch/handtest-1.0.md`. Versionsvorschlag 1.0.0; `Directory.Build.props` steht noch auf
  0.47.x und wird erst im Release-PR gesetzt. Vor dem RC fehlen noch #233 (B2, „Kein Empfänger
  eingestellt!“) und #239 (#238, Quittung unabhängig von der Tonausgabe), beide im CHANGELOG als
  „folgt“ geführt, sowie zwei grüne volle E2E-Läufe (DoD 3).
- Entwicklung läuft auf `develop` (GitHub-Default) mit PATCH-Version und Tag pro Merge, Stand
  v0.47.37. Milestone `v1.0` (geplant 08.01.2027). Alarmweg einschließlich Schließpfad (#136, #36),
  Empfangsbestätigung (#37, #182), Wiederholungstakt (#177), Erreichbarkeit beim Start (#73),
  Firewall (#150, #222), Lizenz- und Sperrhinweise (#133, #179, #180, #181, #183) und Betriebs-Log
  (#227, #214) sind auf `develop` gemergt.
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
  Ticket (Mutex, Abbruch von `StartBackgroundServices`) sind damit nicht ausgeschlossen. In den
  Versionshinweisen 1.0.0 als bekannte Einschränkung („Startprobleme sind nicht sichtbar“, #226).
- #233 (noch nicht gemergt): Absender und Symbol der Windows-Benachrichtigung „Wartung
  erforderlich“ (`AssemblyTitle` „Arbeitsplatz-Dienst“) sind unter Windows nicht von Hand geprüft;
  auf Altinstallationen kann Windows weiter „HaelpMi.Agent“ zeigen. Handtest 1.0 Abschnitt 3,
  Versionshinweise „Bekannte Einschränkungen“.
- #180 (Owner-Entscheidung 22d): Altbestände mit `ForceEnabled` werden nicht migriert; Hinweis in
  den Versionshinweisen 1.0.0 („Update von einer Vorversion“).

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

- #233 und #239 nach E2E-Nachweis mergen, danach im CHANGELOG und in den Versionshinweisen die
  Markierung „(folgt)“ entfernen.
- Volle E2E-Suite `a,b,c` zweimal grün (DoD 3), CI grün.
- Release-PR: Version 1.0.0 in `Directory.Build.props`, Datum in CHANGELOG und Versionshinweisen,
  `dependency-check`. Vorher klären, ob der Weg über `release-1.0` (`AGENTS.md` Teil B, Ausnahme
  „Release-Stabilisierung“) oder direkt `develop` → `main` (#204, DoD 5) gilt.
- Handtest des Repo-Owners nach `docs/handbuch/handtest-1.0.md`, danach Merge nach `main`, Tag
  `v1.0.0` und Kundeninstallat durch den Repo-Owner.
- Rulesets an das neue Modell anpassen (#160): Fast-Forward `develop` → `release-X.Y` kann ein
  GitHub-PR nicht; vor dem ersten Release klären.
- Tickets im Milestone `v1.0`, danach `Regelentwicklung`; zurückgestellte Themen in `release-2.0-…` bzw. `Backlog`.
