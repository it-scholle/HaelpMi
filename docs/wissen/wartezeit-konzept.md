# HälpMi Wartezeiten-Konzept

Systematisches Gegenstück zu den bisherigen Einzel-Fixes an Wartezeiten (Flaw 12/13
Signierschlüssel-Ladezeit, Flaw 15 Alarm-Latenz, Flaw 16 Start-Latenz). Dieses Dokument
ist reine Analyse/Konzept — **keine der hier vorgeschlagenen Maßnahmen ist in dieser Aufgabe
umgesetzt**. Umsetzung folgt separat, nach Abschluss der aktuell laufenden kritischen Fixes
17/18. Die Design-Richtlinie am Ende gilt aber ab sofort für neue Entwicklung.

## Datenbasis / Methodik

Drei unterschiedlich belastbare Quellen, im Inventar unten pro Zeile gekennzeichnet:

- **Gemessen (real, aus dem Code ablesbar)**: harte Konstanten aus `AppConstants.cs`
  (Timeouts, Jitter-Bereiche, Intervalle) — das sind keine Schätzungen, sondern das
  Programm hält sich exakt daran.
- **Instrumentiert, aber noch kein Log ausgewertet**: `StartupTimingLog`
  (`src/HaelpMi.Core/Diagnostics/StartupTimingLog.cs`) markiert bereits reale Meilensteine
  im Agent-Start (siehe Liste unten) — die Reihenfolge/Abhängigkeit der Schritte ist damit
  gesichert, absolute Millisekundenwerte aus einem echten Lauf lagen für dieses Dokument
  aber nicht vor (kein `startup-timing.log` im Repo, da laufzeitgeneriert und pro Definition
  nicht eingecheckt).
- **Noch nicht instrumentiert**: `TestLogger` (Flaw 19, v0.38.0) liefert das *Framework* für
  ein strukturiertes JSONL-Aktionsprotokoll inkl. `CorrelationId` — die eigentliche
  Instrumentierung der Kommunikationsschicht (Netzwerkempfang bis Popup/Sound, also genau
  der Flaw-15-Pfad) ist laut Commit-Beschreibung explizit als **Flaw 20 noch offen**. Für
  den Empfangspfad gibt es deshalb aktuell keine belastbaren Zeitstempel — nur eine
  Ingenieurs-Schätzung, unten als solche markiert.

**Konsequenz für die Priorisierung unten:** Sobald Flaw 20 die Kommunikationsschicht
instrumentiert, sollte dieses Dokument mit den dann echten Werten aktualisiert werden, bevor
die Umsetzungsreihenfolge verbindlich fixiert wird. Die Reihenfolge hier basiert auf
Code-Fakten (welche Schritte sind synchron verkettet, welche Konstanten gelten bereits) plus
begründeter Schätzung für die noch unvermessenen Netzwerk-/Prozessstart-Anteile.

## 1. Vollständiges Wartezeiten-Inventar

Kategorien: **a** = zwingend synchron vor Nutzung, **b** = unnötig/vermeidbar, **c** = nötig,
aber nicht vor erster Interaktion nötig (Hintergrund-Kandidat).

| # | Schritt | Ort | Dauer | Quelle | Kat. |
|---|---------|-----|-------|--------|------|
| 1 | Datei-Extraktion/Kopie (Inno Setup) | Installation | ~2–5 s | Schätzung (übliche Payload-Größe) | a |
| 2 | .NET-8-Runtime-Prüfung, ggf. Nachinstallation | Installation | 0 s (vorhanden) bis 30–60 s (Download+Install fehlt) | Schätzung | a, sofern fehlend — Download selbst wäre c-fähig (parallel zu Dateikopie) |
| 3 | `AutostartRegistrar.EnsureRegistered` elevated via `RunRegisterAutostartOnlyAndExit` (separater `schtasks.exe`-Aufruf vor dem eigentlichen `[Run]`) | Installation | ~0,3–1 s je `schtasks.exe`-Aufruf | Schätzung (Prozessstart-Overhead) | a (muss elevated vor dem de-elevated `[Run]`-Start passieren) |
| 4 | `[Run]`: `Agent.exe` + `Config.exe --open-dashboard`, beide `nowait` | Installation | parallel, kein Zusatz-Wait | Code (`installer/HaelpMiCommon.iss.inc:198,204,206`) | bereits optimal (a, aber schon parallelisiert) |
| 5 | Agent `OnStartup` entered → `IpcServer.Start()` | Agent-Start | kurz (kein I/O, reine Objektkonstruktion) | `StartupTimingLog`-Meilenstein, Wert nicht gemessen | a |
| 6 | `IpcServer.Start()` → `AutostartRegistrar.EnsureRegistered` **im normalen Agent-Boot erneut** (Prüfung/Selbstheilung, nicht nur beim Installer-Sonderpfad #3) | Agent-Start | vermuteter Hauptanteil der ~1-Minuten-Verzögerung aus dem v0.35.2-Fehlerbericht — noch nicht durch `startup-timing.log`-Auswertung bestätigt | `StartupTimingLog`-Meilenstein „AutostartRegistrar.EnsureRegistered done (misst schtasks.exe-Laufzeit)“, Kommentar im Code selbst benennt dies als Messziel | **b/c** — siehe Abschnitt 5 |
| 7 | → `_coordinator` zugewiesen (Selbsttest wäre ab hier IPC-seitig bedienbar) | Agent-Start | kurz | Meilenstein | a |
| 8 | → `AuditSyncService.StartListening()` (nur Admin-Rolle), `AlarmFeedbackChannel.Start()`, `DiscoveryService`-Konstruktion — sequenziell hintereinander | Agent-Start | je einzeln kurz (Socket-Bind), aber **sequenziell statt parallel** | Code-Lesung `App.xaml.cs:337-368` | c |
| 9 | → `TryBecomePrimaryOrSatellite()` (TCP-Bind Alarm-Port oder Satellite-Anmeldung) | Agent-Start | kurz bis mittel (Named-Pipe-Verbindungsaufbau bei Satellite) | Meilenstein | a — aber Selbsttest-Empfangsbereitschaft hängt aktuell zusätzlich am vorgelagerten Schritt #6, obwohl beide unabhängig sind |
| 10 | → `ConfigSyncService`-Konstruktion + Announce | Agent-Start | kurz | Code-Lesung, kein eigener Meilenstein | c (kann nach „App ist bedienbar“ folgen) |
| 11 | Config.exe: Mutex + `settingsStore.Load()` + `DeploymentInfoStore.Load()` | Konfigurator-Start | reiner Dateizugriff, kurz | Code-Lesung `App.xaml.cs:79-131` | a |
| 12 | Config.exe: `LicenseEvaluator.Evaluate(LicenseFileLoader.LoadAndVerify(), …)` — **lokale** Ed25519-Prüfung, **kein** Netzwerkzugriff (im gesamten `src/`-Baum kein `HttpClient`/`WebRequest` für Lizenz- oder Zertifikatsprüfung gefunden) | Konfigurator-Start | Mikrosekunden (SHA-256 + Ed25519-Verify, reine CPU-Arbeit) | Code-Lesung + gezielte Suche (kein Treffer für Online-Prüfung) | a, aber trivial billig — blockiert das Fenster dennoch unnötig (Reihenfolge, nicht Kosten, ist das Problem, siehe #13) |
| 13 | Config.exe: `await EnsureAgentIsRunningAsync()` — IPC-Ping mit 1 s Timeout, bei Fehlschlag `Agent.exe` neu starten — **läuft vor `configWindow.Show()`**, blockiert also die gesamte Fenstererstellung | Konfigurator-Start | 0 s (Agent läuft schon) bis volle 1 s (Timeout) + Prozessstartzeit, falls Agent fehlt | Code (`App.xaml.cs:178`, Timeout `TimeSpan.FromSeconds(1)` in `EnsureAgentIsRunningAsync`) | **c** — kein Grund, das Fenster deswegen zurückzuhalten |
| 14 | `ConfigWindow`/`AdminDashboardWindow`-Aufbau: `LoadDevices`, `LoadConfig`, `RecipientResolver` etc. | Konfigurator-Start | wächst mit Geräte-/Gruppenzahl | Code-Lesung | teils a (Grundgerüst), teils c (Gerätelisten aus Gossip können nachladen) |
| 15 | `AlarmSender.SendAsync`: `IPreSendConfirmation.ConfirmAsync` (Nutzer-Dialog) | Alarm senden | Nutzerabhängig | Code | a (echte Nutzerinteraktion) |
| 16 | `AlarmSender.SendAsync`: Parallelversand an alle Ziele via `Task.WhenAll` | Alarm senden | bereits parallelisiert, kein Sequenzialisierungs-Verlust | Code (`AlarmSender.cs:90`) | bereits optimal — als positives Gegenbeispiel im Inventar aufgeführt |
| 17 | `AppConstants.AlarmAckTimeout` — wie lange pro Zielgerät auf ein Ack gewartet wird, bevor es als „nicht erreicht“ zählt | Alarm senden | **5 s** (harte Konstante) | Code `AppConstants.cs:95` | a — echtes, gewolltes Warten auf langsame Ziele; UI hat bereits `IProgress<AlarmSendProgress>` für inkrementelle Anzeige (sicherstellen, dass es überall auch genutzt wird, siehe Abschnitt 3) |
| 18 | `SecureEnvelope`-Verschlüsselung (ChaCha20-Poly1305) pro Nachricht | Alarm senden/empfangen | Mikrosekunden | Code-Lesung (symmetrische Stream-Chiffre, kleine Payload) | a, vernachlässigbar |
| 19 | Netzwerkempfang eines Alarms bis sichtbares Popup/hörbarer Ton | Alarm empfangen | **unbekannt** — genau der Pfad, den `TestLogEventType.PopupShown`/`SoundPlayed` laut eigenem Klassenkommentar erst mit Flaw 20 sichtbar machen soll | Kein Messwert vorhanden, nur Code-Kommentar-Verweis (`TestLogger.cs:38-39`) | **Priorität 1 für Flaw 20** — bis dahin nur Schätzung, siehe Abschnitt 5 |
| 20 | IPC `SelfTest`-Request empfangen → beantwortet | Selbsttest | kurz laut Meilenstein-Abstand, kein absoluter Wert vorhanden | `StartupTimingLog` (`App.xaml.cs:663,672`) | a, sofern Agent voll gestartet ist — hängt aber wieder an #6/#9 |
| 21 | Config-Sync Hot-Reload: Broadcast bei Blur eines gültigen Pflichtfelds, Empfänger laden im laufenden Prozess neu | Laufzeit | kurz (UDP-Announce + gezielter TCP-Pull) | Architekturvorgabe CLAUDE.md „Config-Sync ist Hot-Reload“ | a, per Design bereits ohne Save-Button/Wartedialog |
| 22 | Edit-Lock „will editieren“-Call + `EditLockCollisionBackoff` bei Kollision | Admin-Dashboard | Call selbst kurz, Backoff **200–800 ms** bei Kollision | Code `AppConstants.cs:129` | a, selten (nur bei echter Gleichzeitigkeit) |
| 23 | Update-Pipeline: Install/Test/Swap + mind. eine Peer-Bestätigung + `UpdatePullJitter` | Update (Hintergrund) | Jitter bewusst **5–300 s** gestreut | Code `AppConstants.cs:132` | a, aber **kein UI-Wartender** — läuft ohne Nutzerinteraktion im Hintergrund, deshalb kein Kandidat für „wahrgenommene Wartezeit“ im engeren Sinn, siehe Anmerkung unten |
| 24 | InstallCreator: `VaultwardenClient` — sequenzielle `bw.exe`-Prozessstarts (`logout` → `config server` → `status` → ggf. `login` → `unlock` → `sync` → `get notes`), alle einzeln awaited | InstallCreator (Entwicklertool) | 6–7 Prozessstarts × ~0,3–1 s OS-Overhead je nach Maschine = geschätzt 2–7 s, nur beim Klick auf „Update erstellen“ ausgelöst | Code-Lesung `VaultwardenClient.cs` | **b/c** — das ist Flaw 12/13, siehe Abschnitt 5 |
| 25 | InstallCreator/Installer-Build (`dotnet publish` + `ISCC.exe`) | Entwickler-Build, kein Endnutzerpfad | mehrere Sekunden bis Minuten | außerhalb Scope | kein Endnutzer-Wartezeitproblem — aus der weiteren Priorisierung ausgeklammert |

## 2. Verbesserungsvorschläge je b/c-Zeile

- **#2 (.NET-Runtime-Nachinstallation, falls fehlend):** Download parallel zur übrigen
  Datei-Extraktion anstoßen statt sequenziell davor — reduziert nur den Fall „Runtime fehlt“,
  betrifft die meisten Alpha-/Bestandsinstallationen nicht.
- **#6 (AutostartRegistrar im normalen Agent-Boot):** Der eigentliche Autostart-Task ist laut
  Code-Kommentar in `RunRegisterAutostartOnlyAndExit` schon **vor** dem eigentlichen
  `Agent.exe`-Start durch den Installer erledigt (elevated Sonderlauf, #3). Der zweite Aufruf
  im normalen Boot (`StartBackgroundServices`, #6) ist eine Selbstheilungsprüfung für den
  Fall, dass der Task fehlt/kaputt ist — das ist im Normalfall (Task existiert bereits)
  redundante Arbeit, die trotzdem synchron vor #7–#9 steht und damit die
  Selbsttest-Bedienbarkeit verzögert. Vorschlag: Existenzprüfung des Tasks (billig, kein
  Prozessstart) synchron lassen, den eigentlichen `schtasks.exe /Create`-Aufruf nur bei
  tatsächlich fehlendem/kaputtem Task ausführen, und diesen Fall dann **nach** #7/#9
  (asynchron, fire-and-forget mit Tray-Fehlermeldung wie heute schon bei Fehlschlag)
  einordnen. Deckt sich mit dem in v0.35.2 vermuteten, aber noch nicht bestätigten Root
  Cause — vor der Umsetzung mit einem echten `startup-timing.log`-Lauf gegenprüfen.
- **#8 (AuditSync/Feedback/Discovery-Konstruktion sequenziell):** Die drei Konstruktionen
  sind laut Code voneinander unabhängig (kein Datenfluss zwischen ihnen vor #9). Kandidat
  für `Task.WhenAll`/parallele Initialisierung statt der aktuellen Reihenfolge — kleiner
  Gewinn einzeln, aber symptomatisch für das generelle Muster „sequenziell, weil so
  gewachsen, nicht weil nötig“.
- **#10 (ConfigSyncService erst nach allem anderen):** schon spät genug im Boot, dass sie
  kein UI blockiert (Agent hat kein eigenes Fenster) — niedrige Priorität, nur der
  Vollständigkeit halber als c erfasst.
- **#12+#13 (Lizenzprüfung + Agent-Ping vor `configWindow.Show()`):** Größter Hebel im
  Konfigurator/Dashboard-Pfad. Fenster **sofort** mit Skeleton-Zustand zeigen (Buttons
  vorhanden, aber „Verbindung wird geprüft…“-Zustand für alles, was von `IpcClient`/Agent
  abhängt), Lizenzprüfung und Agent-Ping parallel im Hintergrund starten, Toast/Banner
  nachreichen sobald fertig. Deckt sich mit dem vermuteten Flaw-16-Verdacht (siehe
  Abschnitt 4) — bestätigt sich hier als real vorhandene, aber leicht behebbare
  Sequenzialisierung, nicht als Online-Zertifikatsprüfung.
- **#14 (Geräte-/Gruppenlisten im Dashboard):** Grundgerüst (Tabs, Buttons, eigenes Gerät
  aus `LoadOwnDevice`) sofort rendern; Peer-Geräteliste aus Gossip/`LoadDevices` als
  „lädt…“-Zustand nachziehen — passt zum Skeleton-Muster aus Abschnitt 6.
- **#19 (Empfangspfad Netzwerk → Popup/Ton):** Kann ohne Flaw-20-Instrumentierung nicht
  seriös weiter zerlegt werden. **Empfehlung: Flaw 20 vor jeder Detail-Optimierung dieses
  Pfads umsetzen** — alles andere wäre Optimierung nach Vermutung statt nach Messung, genau
  das Muster, das dieses Dokument insgesamt vermeiden soll.
- **#24 (VaultwardenClient-Kette, Flaw 12/13):** Drei unabhängige Verbesserungen, keine
  davon senkt die Sicherheit:
  1. `logout` + `config server` + `status` sind reine Zustandsermittlung, keine
     nutzereingabeabhängige Arbeit — können **vorgezogen** werden: sobald das
     InstallCreator-Fenster öffnet (oder sobald `VaultwardenClient.TryCreate()` erfolgreich
     `bw.exe` findet), diese Kette spekulativ im Hintergrund anstoßen, statt erst beim Klick
     auf „Update erstellen“. Der Nutzer tippt/liest ohnehin einige Sekunden, bevor er den
     Button trifft — dieselbe Zeit reicht für die Zustandsermittlung.
  2. `sync` vor `get notes` ist laut Code selbst schon als best-effort/optional markiert
     (Kommentar: „falls sync fehlschlägt, versuchen wir trotzdem mit dem lokalen
     Cache-Stand“) — könnte mit kurzem Timeout statt vollem Warten laufen, da ein
     veralteter Cache-Stand im Zweifel ohnehin durch eine bewusste
     Neuerstellen-Bestätigung des Nutzers abgefangen wird.
  3. Das Master-Passwort bleibt bewusst bei „einmal pro Programmstart“ (Kategorie a,
     nicht verhandelbar aus Sicherheitssicht) — nur die *Schlüssel-Abfrage selbst* ist der
     b/c-Kandidat, nicht die Passworteingabe.

## 3. Priorisierte Umsetzungsreihenfolge

Größter wahrgenommener Zeitgewinn zuerst, unter Berücksichtigung, wie viele Nutzer den Pfad
wie oft treffen (Agent/Konfigurator = jeder Nutzer, jeden Start; InstallCreator = nur
Entwickler/Admin, selten):

1. **#12+#13 — Konfigurator/Dashboard: Fenster sofort zeigen, Lizenz+Agent-Ping
   parallelisieren und nach Anzeige nachladen.** Höchste Priorität: betrifft jeden
   Admin-Start, größter Sequenzialisierungs-Fehler mit dem geringsten Umsetzungsrisiko
   (reine Reihenfolgenänderung, keine neue Logik).
2. **Flaw 20 — Kommunikationsschicht-Instrumentierung (TestLogger).** Kein eigener
   Zeitgewinn, aber Voraussetzung dafür, dass #19 (Alarm-Empfangspfad, der aus Nutzersicht
   sicherheitskritischste Pfad überhaupt) datenbasiert statt geschätzt optimiert werden
   kann. Vor Punkt 3 einordnen, obwohl er selbst keine Latenz senkt.
3. **#19 — Alarm-Empfangspfad, sobald Flaw-20-Daten vorliegen.** Konkrete Maßnahmen erst
   nach Messung festlegen.
4. **#6 — AutostartRegistrar-Selbstheilung aus dem kritischen Pfad des Agent-Boots
   nehmen.** Direkter Kandidat für den v0.35.2-Fehlerbericht; vor Umsetzung mit einem
   echten `startup-timing.log` gegenprüfen (siehe Abschnitt 4).
5. **#8+#10 — Parallelisierung der unabhängigen Agent-Boot-Schritte.** Kleinerer,
   risikoarmer Gewinn, guter „Aufräum“-Zeitpunkt direkt nach #6, weil dieselbe Methode
   ohnehin angefasst wird.
6. **#14 — Dashboard-Gerätelisten als Skeleton + Nachladen.** Wirkung wächst mit
   Kundengruppengröße, bei kleinen Testinstallationen kaum spürbar — deshalb hinter #6/#8.
7. **#24 — InstallCreator/VaultwardenClient vorziehen.** Trifft nur Entwickler/Admins
   selten, niedrigste Priorität trotz spürbarer Einzel-Wartezeit (Flaw 12/13).
8. **#2 — .NET-Runtime-Download parallelisieren.** Seltenster Fall (fehlende Runtime),
   niedrigste Priorität.

`#16`, `#17`, `#18`, `#21`, `#22`, `#23` bleiben unverändert (bereits optimal bzw. bewusst
gewolltes Warten) — explizit **nicht** in der Umsetzungsliste, um zu zeigen, dass nicht
jede Wartezeit ein Fehler ist.

## 4. Sonderfälle im Detail: Flaw 12/13 und Flaw 16

- **Flaw 12/13 (Signierschlüssel-Ladezeit):** Kategorie **c**, nicht a. Die
  `VaultwardenClient`-Kette (#24) macht ausschließlich Zustandsermittlung + Schlüsselabruf,
  nichts davon hängt vom Zeitpunkt des Button-Klicks ab außer dem Passwort selbst. Die
  Wartezeit entsteht durch **sequenzielle Prozessstarts**, nicht durch tatsächlich
  notwendige Rechenzeit — genau das Muster „technisch schon asynchron (`async Task`), aber
  im UI-Pfad synchron erlebt, weil erst bei Bedarf statt vorab gestartet“. Maßnahme:
  Vorabruf beim Fensteröffnen (siehe Abschnitt 2, Punkt 1).
- **Flaw 16 (Start-Latenz Installer/Konfigurator/Dashboard), insbesondere der Verdacht
  einer synchronen Online-Zertifikatsprüfung:** **Verdacht widerlegt.** Eine gezielte Suche
  über den gesamten `src/`-Baum nach `HttpClient`/`WebRequest`/Online-Prüfungsmustern im
  Kontext von Lizenz oder Zertifikat ergab **keinen Treffer** — `LicenseVerifier.Verify`
  und `LicenseEvaluator.Evaluate` arbeiten rein lokal (Ed25519-Signaturprüfung über die im
  Programm eingebetteten Bytes, siehe `LicenseVerifier.cs`), passend zu CLAUDE.md
  „Kunden-Lizenzsignatur — Soft-Expiry, kein Hard-Lock“ und dem Kein-Heartbeat-Prinzip. Die
  tatsächliche Ursache der wahrgenommenen Start-Latenz ist stattdessen die **Reihenfolge**
  in `App.xaml.cs`: Lizenzprüfung (Kategorie a, aber im Mikrosekundenbereich, s. #12) und
  Agent-Erreichbarkeits-Ping (Kategorie c, s. #13) laufen beide vor `configWindow.Show()` —
  keiner von beiden ist teuer, aber beide zusammen verzögern unnötig den ersten sichtbaren
  Frame. Für den Agent-Prozess selbst (kein eigenes Fenster) ist der analoge Verdacht #6
  (AutostartRegistrar) — dort noch nicht abschließend bestätigt, da kein echter
  `startup-timing.log`-Lauf vorlag; die Codestruktur macht ihn aber zum wahrscheinlichsten
  Kandidaten.

## 5. Zielwerte

| Messpunkt | Zielwert | Harte Obergrenze |
|---|---|---|
| Konfigurator/Dashboard: Zeit bis erstes sichtbares Fenster | ≤ 150 ms nach Prozessstart | 500 ms |
| Konfigurator/Dashboard: Zeit bis UI bedienbar (Klicks wirken, ggf. noch mit „lädt…“-Platzhaltern) | ≤ 300 ms nach Prozessstart | 800 ms |
| Konfigurator/Dashboard: Zeit bis vollständig befüllt (Geräte/Gruppen/Lizenzstatus) | ≤ 2 s unter normalen LAN-Bedingungen | 5 s |
| Agent: Zeit bis IPC-Server bereit (Config.exe-Ping erfolgreich) | ≤ 200 ms nach Prozessstart | 1 s |
| Agent: Zeit bis Selbsttest-fähig (simulierter Alarm löst Popup/Ton aus), unabhängig von AutostartRegistrar | ≤ 500 ms nach Prozessstart | 2 s |
| Alarm: Senden → erste Empfänger-Reaktion sichtbar (ohne auf die vollen 5 s Ack-Fenster zu warten) | ≤ 300 ms unter normalen LAN-Bedingungen | Ack-Sammelfenster bleibt 5 s (bewusst) |
| Installation gesamt: Datei-Kopie bis Agent lauffähig + Autostart sichtbar im Tray | ≤ 20 s auf normaler Hardware | 60 s (aktuell laut v0.35.2-Verdacht im schlechten Fall erreicht — das ist die zu unterschreitende Grenze, nicht das Ziel) |
| InstallCreator: Zeit vom Klick auf „Update erstellen“ bis Signieren beginnt | ≤ 500 ms (bei vorab gestartetem Vaultwarden-Abruf) | 2 s |

## 6. Design-Richtlinie (verbindlich ab sofort)

**Fenster/Prozess zuerst, Daten danach.** Jedes neue UI-Fenster zeigt sich selbst — Layout,
Texte, Buttons, Icons — bevor irgendein Netzwerk-, IPC- oder Datei-Zugriff beginnt, der
nicht zwingend für genau diesen ersten Frame gebraucht wird. Alles, was von einem externen
Zustand abhängt (Agent erreichbar? Lizenz gültig? Wie viele Geräte online?), bekommt einen
definierten Platzhalterzustand statt eines blockierenden Awaits davor. Diese Regel gilt
unabhängig davon, ob der Ladevorgang „schnell genug fühlt sich sowieso schnell an“ ist — die
Reihenfolge ist der Fehler, nicht die einzelne Operation.

**Parallel ist der Normalfall, sequenziell die Ausnahme.** Zwei Ladevorgänge, die keine
Datenabhängigkeit zueinander haben, laufen nebeneinander (`Task.WhenAll` oder gleichwertig),
nicht hintereinander, weil der Code historisch in dieser Reihenfolge geschrieben wurde. Wo
eine echte Abhängigkeit besteht (B braucht das Ergebnis von A), wird das im Code kommentiert
— genau wie es in `App.xaml.cs` an mehreren Stellen bereits vorbildlich gemacht wird (z. B.
der Kommentar zur Event-Handler-Reihenfolge vor `StartListening()`). Prozessstart-lastige
Arbeit (externe `.exe`-Aufrufe wie `schtasks.exe`, `bw.exe`) wird so früh wie möglich
spekulativ angestoßen, statt erst beim tatsächlichen Bedarf — der Nutzer liest/tippt in der
Zwischenzeit ohnehin.

**Keine Wartezeit ohne Zielwert, kein Zielwert ohne Messung.** Neue Ladevorgänge bekommen
beim Entwurf einen der drei Werte aus Abschnitt 5 zugeordnet (oder einen neuen, begründeten,
falls keiner passt) — nicht nachträglich als Reaktion auf einen Fehlerbericht. Sobald die
Flaw-20-Instrumentierung steht, ist eine grobe Zeitmessung (`TestLogger`/`StartupTimingLog`-
Muster) an neuen kritischen Übergängen mitzuliefern statt einer Schätzung im Kommentar —
Over-Engineering-Grundsatz aus CLAUDE.md bleibt dabei unverändert: eine eigene
Instrumentierungs-Abstraktion nur, wenn ein konkreter, aktuell existierender
Messbedarf das rechtfertigt, kein generisches „Performance-Framework” auf Vorrat.
