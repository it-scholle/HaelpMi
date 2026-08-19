# Flaw 15 — Bekannte Geräte ~30s verzögert: Befund

Branch `fix/known-device-alarm-latency`. Ausgangshypothese laut Audit bereits widerlegt:
`AlarmSender.SendAsync` nutzt seit dem allerersten Commit (v0.7.3) `Task.WhenAll`, hat also nie
sequenziell zugestellt. Diese Aufgabe sollte reproduzieren und mit echten Timings die
tatsächliche Ursache eingrenzen, **bevor** irgendetwas am Sendecode geändert wird.

## Geprüfte Kandidaten (aus dem Auftrag)

### 1. Einzelne sehr lange TCP-Connect-Timeouts blockieren `WhenAll` — widerlegt

Empirisch getestet (nicht nur code-gelesen), zwei unabhängige Repros:

- **Roh-Socket-Test** (`TcpClient.ConnectAsync` + `CancellationTokenSource.CancelAfter(5s)`
  gegen ein TEST-NET-Blackhole `192.0.2.1` und eine unvergebene lokale Adresse
  `10.255.255.254`): beide Verbindungsversuche brechen sauber bei **5,00–5,14 s** ab
  (`OperationCanceledException`). Kein Hinweis auf das befürchtete Windows-SYN-Retry-Verhalten
  (~21 s), das die .NET-Cancellation eigentlich umgehen sollte, aber in älteren
  .NET-Versionen ein bekanntes Risiko war — auf .NET 8 bestätigt sauber.
- **Realer `AlarmSender.SendAsync`-Aufruf** mit gemischter Zielliste (4 echte, sofort
  antwortende lokale Listener + 1 "bekanntes" aber unerreichbares Ziel, simuliert eine
  veraltete DeviceStore-IP nach DHCP-Wechsel): Gesamtlaufzeit **5,12 s** für alle 5 Ziele,
  `AckedCount=4`. Die Laufzeit wächst NICHT mit der Anzahl der Ziele und bleibt exakt am
  `AppConstants.AlarmAckTimeout` (5 s) hängen — `Task.WhenAll` parallelisiert tatsächlich wie
  im Code behauptet.

Kandidat 1 ist damit sowohl auf reiner Socket-Ebene als auch im echten `AlarmSender`-Pfad
widerlegt. Ein einzelner besonders langsamer/unerreichbarer Peer verzögert die Gesamtwelle auf
maximal ~5 s, nicht ~30 s.

### 2. DNS-Verzögerung — entfällt

`AlarmSender.SendToOneAsync` parst `target.IpAddress` direkt über `IPAddress.TryParse` — es
findet nie eine DNS-Auflösung statt, Ziel ist immer schon eine IP-Adresse.

### 3. Veraltete/nicht mehr erreichbare Einträge in der bekannten-Geräte-Liste — teilweise bestätigt, aber ohne 30s-Erklärung auf dieser Ebene

`DeviceStore.Upsert` matcht ausschließlich über `DeviceId`, nie über IP (FR-22, bewusst gegen
DHCP-Duplikate) — ein Gerät, das seit einer IP-Änderung keinen neuen Boot-Call-Kontakt hatte,
bleibt mit seiner alten IP in der Liste stehen, es gibt keine Eviction/Staleness-Prüfung.
Das ist real, erklärt aber laut obigem Repro (Kandidat 1) für sich allein keine 30s-Verzögerung
einer einzelnen Sendewelle — ein solches Ziel zählt nach spätestens 5s einfach als "nicht
erreicht" (`AckedCount` entsprechend niedriger), die Welle als Ganzes ist trotzdem nicht
verzögert.

## Wo die ~30s vermutlich tatsächlich herkommen

`docs/WARTEZEIT-KONZEPT.md` (19.08.2026, vor Flaw 20 geschrieben) kommt in Zeile #16/#17 seines
Inventars unabhängig zum selben Schluss wie oben: der Sendepfad (`Task.WhenAll`,
`AlarmAckTimeout`) ist "bereits optimal". Zeile #19 markiert stattdessen den **Empfangspfad**
(Netzwerkempfang eines Alarms bis sichtbares Popup/hörbarer Ton) als **unvermessen** und
"Priorität 1 für Flaw 20" — zum Zeitpunkt dieses Dokuments blockiert, weil die
Kommunikationsschicht noch nicht instrumentiert war.

Flaw 20 (v0.39.0, `8df0591`) hat genau diese Instrumentierung seither ergänzt
(`TestLogEventType.MessageReceived`/`PopupShown`/`SoundPlayed` in `AlarmTcpListener`/
`AlarmFlowCoordinator`). Damit ist der Empfangspfad jetzt technisch messbar — aber:

**Es liegen noch keine echten Log-Daten vor.** `Z:\HaelpMi-Logs\*\test-actions-*.jsonl` (Glob
geprüft, Stand 19.08.2026) enthält keine einzige Datei — seit v0.39.0 wurde offenbar noch kein
echter Alarm auf einer Testinstallation ausgelöst, der diese Instrumentierung durchlaufen hätte.

## Warum hier gestoppt statt weiter spekuliert wird

Der Auftrag verlangt ausdrücklich "erst nach bestätigter Ursache fixen". Eine plausible nächste
Vermutung (z. B. Dispatcher-Kontention beim `Application.Current.Dispatcher.BeginInvoke` in
`AlarmFlowCoordinator.HandleIncomingAlarmRequest`, oder eine Win32-Forced-Foreground-Retry-
Schleife im Popup-Fenster) wäre an dieser Stelle reine Spekulation ohne Messwert — genau das
Muster, das `WARTEZEIT-KONZEPT.md` (Abschnitt 6, Design-Richtlinie) und dieser Auftrag
ausdrücklich vermeiden wollen. Eine isolierte Sandbox ohne zweites echtes Gerät (Popup/Sound/
Win32-Forced-Foreground lassen sich nicht sinnvoll in einem einzelnen Konsolenprozess
nachstellen) kann diesen Pfad nicht seriös weiter eingrenzen.

## Empfehlung für die Fortsetzung

1. **Kein Fix an `AlarmSender`/`Task.WhenAll`/`AlarmAckTimeout`** — durch obiges Repro
   bestätigt korrekt, eine Änderung dort wäre unbegründet und würde ein funktionierendes,
   bereits als "positives Gegenbeispiel" dokumentiertes Stück Code anfassen.
2. Einen echten Alarm gegen ein "bekanntes" Zielgerät in der vorgesehenen 4-VM-Testumgebung
   auslösen (1 Admin + 3 Client-VMs, siehe Testplan) und die resultierende
   `test-actions-*.jsonl` auf beiden Seiten auswerten: Zeitdifferenz `MessageSent`
   (Sender) → `MessageReceived` (Empfänger) grenzt den Netzwerkanteil ein,
   `MessageReceived` → `PopupShown`/`SoundPlayed` (Empfänger) den lokalen Anzeige-/
   Audio-Anteil — exakt der in `WARTEZEIT-KONZEPT.md` Abschnitt 3 Punkt 3 vorgesehene
   nächste Schritt.
3. Erst mit diesen echten Zeitstempeln lässt sich der tatsächliche ~30s-Anteil lokalisieren
   und gezielt fixen.
