# Status

Stand: 2026-11-27

## Aktueller Stand

- Release-Kandidat **0.48.0** (Owner-Entscheidung 32 in #204: zuerst 0.48.0, nach Handtest und
  Freigabe des Repo-Owners Umstellung auf 1.0.0). `CHANGELOG.md` Abschnitt „[0.48.0] –
  Release-Kandidat“, Versionshinweise `docs/release-notes/0.48.0.md`, Handtest
  `docs/handbuch/handtest-0.48.0.md`, Version 0.48.0 in `Directory.Build.props`.
- Nachweise (#204), alle auf `develop` v0.47.40 (`13acd97`):
  - DoD 1 (#136 weg, je 100 Zyklen, zwei Runden): T10
    [45908226232](https://github.com/it-scholle/HaelpMi/actions/runs/45908226232),
    [45925667421](https://github.com/it-scholle/HaelpMi/actions/runs/45925667421); T18
    [45915113014](https://github.com/it-scholle/HaelpMi/actions/runs/45915113014),
    [45932485136](https://github.com/it-scholle/HaelpMi/actions/runs/45932485136); T19
    [45920303984](https://github.com/it-scholle/HaelpMi/actions/runs/45920303984),
    [45937587078](https://github.com/it-scholle/HaelpMi/actions/runs/45937587078).
  - DoD 3 (volle E2E-Suite a,b,c zweimal grün):
    [45893361138](https://github.com/it-scholle/HaelpMi/actions/runs/45893361138),
    [45901072357](https://github.com/it-scholle/HaelpMi/actions/runs/45901072357).
  - DoD 4: CI (Build, Unit-Tests, gitleaks) grün.
- Entwicklung läuft auf `develop` (GitHub-Default) mit PATCH-Version und Tag pro Merge, Stand
  v0.48.0 (Release-Kandidat). Milestone `v1.0` (geplant 08.01.2027). Alarmweg einschließlich
  Schließpfad (#136, #36), Empfangsbestätigung (#37, #182), Wiederholungstakt (#177), Erreichbarkeit
  beim Start (#73), Firewall (#150, #222), Erstinstallation (#229), Lizenz- und Sperrhinweise (#133,
  #179, #180, #181, #183), Betriebs-Log (#227, #214), „Kein Empfänger eingestellt!“ (#232) und
  sofort bedienbares Alarmfenster (#238) sind auf `develop` gemergt.
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
  Versionshinweisen 0.48.0 als bekannte Einschränkung („Startprobleme sind nicht sichtbar“, #226).
- #232 (PR #233): Absender „Arbeitsplatz-Dienst“ und neutrales Symbol der Windows-Benachrichtigung
  „Wartung erforderlich“ (eigenes Hilfsprogramm `HaelpMi.Notify.exe`): Die Dateibeschreibung prüft
  E2E (T1), die Anzeige von Absender und Symbol unter Windows ist nicht von Hand geprüft. Handtest
  0.48.0 Abschnitt 3.
- #224: Ursache vermutlich mit #238 beseitigt, nie reproduziert; `status:review`, Abschluss nach
  weiteren grünen T20/T26-Läufen bzw. dem Handtest 0.48.0 Abschnitt 2 („Erster Alarm nach dem
  Hochfahren“).
- #244 (Barrierefreiheit): Alarmfenster erst 2,5–6 s nach dem Öffnen per UI-Automation
  (Screenreader) erreichbar; Meldung „Kein Empfänger eingestellt!“ ohne Ansage. In den
  Versionshinweisen 0.48.0 als bekannte Einschränkung geführt.
- #180 (Owner-Entscheidung 22d): Altbestände mit `ForceEnabled` werden nicht migriert; Hinweis in
  den Versionshinweisen 0.48.0 („Update von einer Vorversion“).

## Bekannte Abweichungen von `AGENTS.md` Teil A

Nicht als Ausnahme freigegeben, Umsetzung steht noch aus:

- Audit-Log ohne Hash-Kette, Aufbewahrung und Schutz (`src/HaelpMi.Core/Storage/AuditLog.cs`).
- Betriebs-Log (`BetriebsLog.cs`, #227) bisher nur mit Start, Ende, Startfehlern, unbehandelten
  Ausnahmen, Alarmempfang und Vordergrund (#238) sowie verworfenen Nachrichten fremder
  Kundengruppen (#214); die übrigen Module schreiben noch nicht hinein.
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

- Handtest des Repo-Owners nach `docs/handbuch/handtest-0.48.0.md`.
- Danach Release-PR `develop` → `main` (normaler Merge oder Fast-Forward, kein Squash) und Tag
  `v0.48.0` durch den Repo-Owner; Kundeninstaller baut der Repo-Owner.
- Nach Freigabe: Umstellung auf 1.0.0 (eigener kleiner PR: Version, CHANGELOG, Versionshinweise,
  Datum), Tag `v1.0.0` durch den Repo-Owner.
- Rulesets an das neue Modell anpassen (#160): Fast-Forward `develop` → `release-X.Y` kann ein
  GitHub-PR nicht; vor dem ersten Release klären.
- Tickets im Milestone `v1.0`, danach `Regelentwicklung`; zurückgestellte Themen in `release-2.0-…` bzw. `Backlog`.
