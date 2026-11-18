# Flaw 1 — Diagnose: verzögerter Alarm-Selbsttest nach Admin-Installation

Fehlerbericht: Alarm-Selbsttest ist direkt nach einer Admin-Installation ca. 1 Minute verzögert
(Meldung/Popup/Sound), nach einem Neustart sofort korrekt. Ursprüngliche Vermutung (Commit
f64b4b5, v0.35.2): Windows-Defender-Reputationscheck auf drei frisch von `[Files]` kopierten,
nie zuvor ausgeführten Binaries. Diese Vermutung war zum Zeitpunkt von f64b4b5 nicht verifiziert.

**Status nach dieser Untersuchung: eine konkrete Teil-Hypothese widerlegt, Gesamt-Root-Cause
weiterhin nicht abschließend bestätigt.**

## Datengrundlage

Reale Logs aus `StartupTimingLog` (`Z:\HaelpMi-Logs\<Rechnername>\startup-timing.log`), bereits
vorhanden von einem Mehrgeräte-Alarm/P2P-Testlauf am 18.08.2026 auf drei frisch installierten
Admin-Testmaschinen (SYSTEMTECHNIK, PERSONALAMT, AMTSLEITUNG). Kein neuer Installationslauf in
dieser Sitzung nötig gewesen — die Daten lagen bereits vor und wurden ausgewertet statt zu raten.

**Einschränkungen dieser Datengrundlage:**
- Die Daten stammen von vor v0.39.0 (TestLogger/Flaw 20) — keine Korrelierbarkeit mit der
  Sendeseite (Config.exe-Dashboard) verfügbar, nur die Empfangsseite (Agent.exe via
  StartupTimingLog).
- Der Testlauf war kein isoliertes "sofort nach Install auf Selbsttest klicken"-Experiment,
  sondern ein breiterer Mehrgeräte-Test — die hier ausgewerteten Selbsttest-Momente sind Indiz,
  kein gezielt reproduzierter Beweis für exakt das im Fehlerbericht beschriebene Szenario.
- In dieser Sitzung war keine neue elevierte Admin-Installation möglich: der Background-Job-
  Prozess hat kein UAC-Token (Administrators-Gruppe steht auf "deny only"), ein Admin-Installer
  braucht aber Elevation für Dienst/Task-Scheduler/Firewall — identischer Blocker wie bereits in
  f64b4b5 dokumentiert.

## Befund 1: doppelter "OnStartup entered"-Eintrag ist kein Bug

Jede der drei Maschinen zeigt zwei "OnStartup entered"-Einträge im Abstand von 0,6–0,8 s direkt
nach der Installation, beide mit `+0ms` Stopwatch-Offset — das bedeutet zwei separate Prozesse,
nicht ein doppelt geloggter Aufruf (die Stopwatch ist ein Static-Feld, pro Prozess neu). Erklärt
durch `App.xaml.cs` (`OnStartup`, Zeile ~106–158): der Installer ruft `Agent.exe` zuerst elevated
mit `--register-autostart` auf (kurzlebiger Helper, beendet sich sofort nach dem
Task-Scheduler-Eintrag), danach den echten, de-elevated Produktivprozess. Beide durchlaufen
denselben unbedingten `StartupTimingLog.Mark(..., "OnStartup entered")`-Aufruf ganz am Anfang,
bevor sich ihre Pfade trennen. Erwartetes, bereits mit dem 11.08.2026-Fix dokumentiertes
Verhalten — keine neue Erkenntnis, aber wichtig, um die Logs nicht misszuverstehen.

## Befund 2: Agent.exe selbst ist konsistent in 1–2 s startbereit

Auf allen drei Maschinen liegt die Zeit von "OnStartup entered" (Produktivinstanz, jeweils der
zweite der beiden Einträge) bis "TryBecomePrimaryOrSatellite() done" (= Selbsttest-Empfang wäre
ab hier IPC-seitig möglich) durchgehend unter 2 Sekunden bei den drei Erstinstallations-Blöcken:

| Maschine       | Erststart-Block | Dauer OnStartup → startbereit |
|----------------|------------------|--------------------------------|
| SYSTEMTECHNIK  | 13:02:30         | ~1,3 s                         |
| PERSONALAMT    | 22:43:28         | ~1,9 s                         |
| AMTSLEITUNG    | 22:44:57         | ~1,1 s                         |

Zwei spätere Blöcke (PERSONALAMT 23:29:19: ~7,4 s; AMTSLEITUNG 23:21:06: ~5,1 s) liegen höher,
sind aber erkennbar spätere Neustarts (keine begleitende zweite "--register-autostart"-Zeile
davor, also keine Erstinstallation) — plausibler regulärer System-Jitter, kein neuer Beleg für
eine ~60s-Verzögerung. Der Größenordnungsunterschied zur berichteten ~1 Minute bleibt in jedem
Fall deutlich.

**Das widerspricht der Defender-Hypothese in ihrer bisherigen Form**, soweit sie sich auf
`Agent.exe` selbst bezieht: würde ein Reputationscheck die Erstausführung von `Agent.exe` um
~60 s verzögern, müsste sich das als Lücke *innerhalb* dieser Kette zeigen (z. B. zwischen
"OnStartup entered" und "IpcServer.Start() done", wo der Prozess durch einen synchronen Scan
blockiert wäre). Das tritt in keinem der sechs beobachteten Startvorgänge auf.

## Befund 3: beobachtete SelfTest-Request → AlarmReceived-Läufe sind schnell

Alle in den Logs sichtbaren Paare "IPC SelfTest-Request empfangen" → nachfolgendes
"AlarmReceived" liegen unter 500 ms auseinander (PERSONALAMT, 22:46:00 und 22:46:13 Uhr). In den
vorliegenden Daten konnte die berichtete ~1-Minuten-Verzögerung also nicht reproduziert werden —
entweder ist sie hier nicht aufgetreten, oder keiner dieser Selbsttests war der tatsächliche
"erste Klick direkt nach frischer Installation" aus dem Fehlerbericht (siehe Einschränkungen
oben).

## Fazit

- Die Defender-auf-`Agent.exe`-Hypothese gilt für den Agent-eigenen Startpfad als **widerlegt**:
  die gemessene Zeit bis zur vollen Betriebsbereitschaft ist über alle sechs beobachteten
  Startvorgänge konsistent kurz (1–2 s im Regelfall, kein Fall über 8 s).
- Die eigentliche Root Cause für die im Fehlerbericht beschriebene ~1-Minuten-Verzögerung ist
  damit weiterhin **nicht bestätigt**. Naheliegendste verbleibende Kandidaten, beide außerhalb
  des bisher instrumentierten Agent-Startpfads:
  1. Startzeit von `Config.exe` (Dashboard) selbst nach frischer Installation — potenziell
     derselbe Defender-Reputationscheck-Mechanismus, nur auf einer anderen, ebenfalls frisch
     kopierten Binary statt auf `Agent.exe`. Dafür existiert bisher keine Instrumentierung.
  2. Reine Bedienzeit des Testers zwischen Installationsende und Klick auf "Selbsttest" — in
     diesem Fall kein Bug, sondern eine Fehlinterpretation der beobachteten Wartezeit.
- **Kein Fix in dieser Sitzung umgesetzt**, da CLAUDE.md-Prinzip ("Anforderungen/Ursache klären,
  dann durcharbeiten") einen Fix ohne bestätigte Root Cause ausschließt und nur eine
  Teil-Hypothese widerlegt, nicht die eigentliche Ursache bestätigt werden konnte.

## Empfohlener nächster Schritt (in dieser Sitzung nicht durchführbar)

Eine interaktive, elevierte Sitzung wiederholt das Experiment mit dem aktuellen Stand
(inklusive v0.39.0 `TestLogger`, das jetzt auch die Sendeseite in `Config.exe` zeitstempeln
kann) und klickt unmittelbar nach Abschluss einer wirklich frischen Admin-Installation auf
Selbsttest, um `Config.exe`-Startzeit und SelfTest-Sendezeitpunkt direkt zu erfassen und damit
zwischen den zwei verbleibenden Kandidaten oben zu unterscheiden.
