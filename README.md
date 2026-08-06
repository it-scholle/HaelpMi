# HälpMi

LAN-Alarmierungssystem für ein Windows-Verwaltungsnetzwerk. Kein Admin, keine zentrale
Instanz - jeder Arbeitsplatz ist gleichberechtigt, Sender und Empfänger zugleich.

Vollständige Anforderungen: [`Anweisungen/pflichtenheft-lan-alarmierung.md`](Anweisungen/pflichtenheft-lan-alarmierung.md).
Verbindliche Entwicklungsregeln für diese Codebase: [`CLAUDE.md`](CLAUDE.md).

**Status: Phase 1** (Pflichtenheft Abschnitt 9). Zwei Produktentscheidungen (FR-7
Sicherheitsabfrage, FR-8 mehrere Nachrichten) sind noch offen - siehe Abschnitt 8a im
Pflichtenheft. Die dafür vorgesehenen Erweiterungspunkte sind gebaut, aber inaktiv (siehe
"Erweiterungspunkte für Phase 2" unten).

## Projektstruktur

```
src/
  HaelpMi.Core/     Modelle, JSON-Speicherung, UDP-Discovery, TCP-Alarmkanal, IPC, Win32-Interop, Audio
  HaelpMi.UI/        Gemeinsame WPF-Fenster (Popup, Empfangs-Banner, Ersteinrichtung, Konfiguration)
  HaelpMi.Agent/     Hintergrunddienst (Listener + Sender + Hotkey), autostartend, ohne eigenes Fenster
  HaelpMi.Config/    Konfigurationsprogramm (ein Fenster, FR-16), separater Prozess
tests/
  HaelpMi.Core.Tests/  xUnit, siehe NFR-9
installer/
  HaelpMi.iss              Installer, normal (kein Admin nötig)
  HaelpMi-Admin.iss        Installer, Admin-Variante (setzt zusätzlich Firewall-Regeln)
  HaelpMiCommon.iss.inc    von beiden geteilte Konstanten/Dateien/Update-Erkennung
```

## Bauen

```
dotnet build HaelpMi.sln
dotnet test tests\HaelpMi.Core.Tests\HaelpMi.Core.Tests.csproj
```

## Installer bauen, installieren, testen

Ausführliche Schritt-für-Schritt-Anleitung: [`BUILD-UND-INSTALLATION.md`](BUILD-UND-INSTALLATION.md).
Test-Checkliste für den Alpha-Test: [`ALPHA-TESTPLAN.md`](ALPHA-TESTPLAN.md).

Kurzfassung: `dotnet publish` für Agent, Config und UpdateService nach
`installer\payload`, dann `HälpMi\tools\InstallCreator\HaelpMi.InstallCreator.exe`
(fertig gebautes Werkzeug) den **Admin-Installer** bauen lassen. Das ist die einzige
Datei, die an einen Sysadmin geht - der exportiert sich den passenden User-Installer für
die übrigen Arbeitsplätze anschließend selbst aus dem laufenden Admin-Dashboard heraus.

## Fehlerdiagnose

Agent und Konfigurationsprogramm loggen unbehandelte Ausnahmen (Typ, Meldung, Stacktrace -
nie Alarmtext oder Gerätedaten, NFR-5) nach `%ProgramData%\HaelpMi\crash.log`. Falls sich
ein Fenster unerwartet verhält oder schließt: diese Datei zuerst prüfen. Ein UI-seitiger
Fehler (`DispatcherUnhandledException`) wird geloggt, aber abgefangen - die App läuft
weiter, statt kommentarlos zu beenden.

Der Install-Creator (läuft nie beim Kunden, siehe `HälpMi\src\HaelpMi.InstallCreator`)
loggt nach demselben Muster, aber in eine eigene Datei ohne Abhängigkeit auf
`%ProgramData%\HaelpMi`: `%LocalAppData%\HaelpMi-InstallCreator\crash.log`.

## Manuell zu testende Punkte (nicht automatisierbar)

Diese Punkte hängen von echter Windows-Umgebung/Hardware/Gruppenrichtlinien ab und sind
bewusst *nicht* Teil der automatisierten Tests, statt eine unechte Abdeckung vorzutäuschen:

- **Forced-Foreground gegen echte Vollbildanwendungen** (Spiele, Kiosk-Software,
  Remote-Desktop-Vollbild) auf allen im Netzwerk vorhandenen Windows-Versionen (5.3, 6.)
- **Audio-Mute-Override auf echten Mehrgeräte-Setups** (mehrere gleichzeitig aktive
  Ausgabegeräte, Bluetooth-Kopfhörer, USB-Headsets) (FR-11)
- **Autostart-Selbstregistrierung unter realen Gruppenrichtlinien** des
  Verwaltungsnetzwerks - kann durch GPO blockiert sein (5.7, 6., 8.)
- **Windows-Firewall-Freigabe** für `AppConstants.DiscoveryUdpPort` (UDP, Standard 51500)
  und `AppConstants.AlarmTcpPort` (TCP, Standard 51501) - muss ggf. per GPO ausgerollt
  werden (6.)
- **Bildschirmschoner-Verhalten** auf unterschiedlichen Windows-Versionen/-Konfigurationen
  (FR-29)
- **Installer-Update-Fluss** gegen eine tatsächlich vorhandene Vorversion, inkl. laufendem
  Agent-Prozess (FR-28, 5.8)

## Lizenz

MIT, siehe [`LICENSE`](LICENSE). Der Copyright-Halter in der `LICENSE`-Datei ist aktuell
ein Platzhalter ("HälpMi contributors") - vor Veröffentlichung durch die tatsächliche
Rechteinhaberin (die Stadt) ersetzen.

Für die separate Kunden-Lizenzverwaltung (Ed25519-signierte Lizenzdateien, siehe
Pflichtenheft 3.9/5.10) ist nur ein Platzhalter für den öffentlichen Schlüssel im Code
(`HaelpMi.Core/Licensing/LicensePublicKey.cs`) - kein privater Schlüssel, auch nicht als
Platzhalter. Dieser Mechanismus selbst ist nicht Teil dieses Pflichtenhefts.
