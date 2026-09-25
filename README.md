# HälpMi

LAN-Alarmierungssystem für ein Windows-Verwaltungsnetzwerk. Netzwerkkommunikation bleibt
P2P zwischen Geräten, ohne zentralen Server-Prozess/Broker. Seit Version 2 gibt es zusätzlich
eine app-interne Admin/User-Rollenaufteilung (unabhängig von Windows-Rechten), Config-Sync
per Hot-Reload, eine signierte Auto-Update-Pipeline und eine eigene Lizenzverwaltung.

Vollständige Anforderungen: [`Anweisungen/pflichtenheft-HälpMi.md`](Anweisungen/pflichtenheft-HälpMi.md)
(ursprüngliche FR-/NFR-Nummern), [`Anweisungen/claude-code-prompt-teil2-admin-update.md`](Anweisungen/claude-code-prompt-teil2-admin-update.md)
(Umsetzungs-Prompt für Admin-Rollen/Config-Sync/Auto-Update).
Verbindliche Entwicklungsregeln für diese Codebase: [`CLAUDE.md`](CLAUDE.md) — bei
Widersprüchen zu den obigen Dokumenten gewinnt `CLAUDE.md`.

## Status

Aktiver Entwicklungsstand: `v0.45.1` auf Branch `release-1.0-MVP` (aktuell GitHub-Default-Branch
für den laufenden Release-Zyklus), letzter getesteter/freigegebener Stand auf `main`: `v0.44.2`.
Zustand **„stable+buggy"**, kein Anspruch auf Vollständigkeit oder Produktionsreife. Grundfunktionen
laufen weitestgehend: Alarmversand/-empfang inkl. Feedback-Relay ("bin unterwegs"), Selbsttest,
Config-Sync/Hot-Reload, Discovery/Boot-Call/Gossip, Admin-Rollen inkl. Exklusiv-Edit-Lock,
Lizenzverwaltung (Import, Geräte-Limits, Ablaufwarnungen), revisionssicheres Audit-Log, signierte
P2P-Auto-Updates mit automatisch anwachsendem Wellen-Rollout, Install-Creator/Installer.

Milestone `release-1.0-MVP` auf GitHub: 44 von 49 Issues erledigt (Stand 22.09.2026).

Der bisherige `main`-Stand vor dem Reset am 20.08.2026 (bis `v0.39.5`, u. a. mit
LAN-Verschlüsselung, kryptografisch verifizierten Admin-Rollen, echter Ed25519-Lizenzprüfung,
Fast User Switching) bleibt eingefroren unter dem Branch `legacy/main-0.39.x` erhalten, wird
nicht weiterentwickelt und stattdessen einzeln über eigene GitHub-Issues neu aufgebaut. Grund,
Versionslinien-Status, Branch-/Release-Workflow und das Issue-getriebene Vorgehen (inkl.
Status-Labels für parallele Sessions): [`CLAUDE.md`](CLAUDE.md).

## Architektur auf einen Blick

- **P2P, kein zentraler Server**: Geräte-Abgleich über persistente Geräte-GUID, nie über
  IP-Adresse. Kein Heartbeat/Polling — Netzverkehr nur bei Boot, Alarm, Config-Änderung,
  Update-Verteilung.
- **Kreis vs. Gruppe**: "Kreis" ist die kundennummernbasierte Netz-Isolation
  (`CustomerGroupId`, beim Bauen des Installers fest eingebrannt) — kein Dashboard-Objekt,
  automatisch eindeutig. "Gruppe" ist die einzige organisatorische Einheit im Admin-Dashboard
  (z. B. eine Etage).
- **App-interne Rollen** Admin/User, im Lizenz-/Konfigfile verankert, unabhängig von
  Windows-Adminrechten.
- **Config-Sync ist Hot-Reload**: kein Save-Button, kein Prozess-Neustart. **Programm-Updates**
  laufen dagegen über eine getrennte, signierte Swap-Pipeline (parallele Testinstanz, Peer-
  Bestätigung, Port-Übernahme, Deinstallation der Altversion).
- Vollständige Ports-/Datenhaltungs-/Ablaufbeschreibung (Boot, Alarm, Config-Sync, Edit-Lock,
  Update-Rollout, Audit-Log): [`docs/TECHSTACK-UND-ARCHITEKTUR.md`](docs/TECHSTACK-UND-ARCHITEKTUR.md)
  — Momentaufnahme zum Nachschlagen, bei Widerspruch gilt der Code bzw. `CLAUDE.md`.

## Projektstruktur

```
src/
  HaelpMi.Core/            Modelle, JSON-Speicherung, Netzwerkprotokolle (UDP/TCP), IPC,
                            Win32-Interop, Audio, Lizenzierung, Update-Krypto
  HaelpMi.UI/               Gemeinsame WPF-Fenster/ViewModels (Popup, Admin-Dashboard, Konfiguration)
  HaelpMi.Agent/            Rechteloser Hintergrundprozess: Sockets/Listener, Sender/Empfänger,
                            Hotkeys, Tray-Icon, kein eigenes Fenster
  HaelpMi.Config/           Sichtbares Konfigurationsfenster bzw. bei Admin-Rolle das
                            Admin-Dashboard; separater Prozess, spricht per Named Pipe mit dem Agent
  HaelpMi.InstallCreator/   Entwickler-Tool: baut Kunden-Installer, erzeugt Kunden-/Gruppen-ID,
                            baut den Update-Bootstrapper
  HaelpMi.UpdateService/    Windows-Dienst (LocalSystem) - einzige privilegierte Komponente:
                            Install/Test/Swap/Deinstall bei Auto-Updates, nur per Named Pipe
  HaelpMi.UpdateSigner/     CLI-Tool zum Erzeugen/Signieren des Update-Schlüsselpaars, läuft
                            nie beim Kunden
tests/
  HaelpMi.Core.Tests/           xUnit, läuft bei jedem `dotnet test`, keine Systemänderung
  HaelpMi.Installer.Tests/      xUnit, verändert das ausführende System real - nur manuell/Wegwerf-VM
  HaelpMi.InstallCreator.Tests/ xUnit
installer/
  HaelpMi.iss               Installer, User-Variante (kein Admin nötig)
  HaelpMi-Admin.iss         Installer, Admin-Variante (setzt zusätzlich Firewall-Regeln)
  HaelpMiCommon.iss.inc     von beiden geteilte Konstanten/Dateien/Update-Erkennung
tools/
  InstallCreator/           gitignorete, per Git-Hook automatisch aktuell gehaltene Kopie des
                            Install-Creator-Tools - `Start.cmd`/`Start.ps1` starten, nicht die rohe .exe
docs/
  TECHSTACK-UND-ARCHITEKTUR.md   Tech-Stack- und Architektur-Nachschlagewerk
  WORKFLOW.md                     Git-/Branch-/Versionierungs-Mechanik im Detail
  WARTEZEIT-KONZEPT.md            Wartezeiten-Inventar/Design-Richtlinie
scripts/
  claim-issue.sh / finish-issue.sh / release-issue.sh   Issue-Status-Label-Helfer für
                            parallele Sessions (siehe `CLAUDE.md`, Abschnitt Issue-Workflow)
```

## Bauen

```
dotnet build HaelpMi.sln
dotnet test tests\HaelpMi.Core.Tests\HaelpMi.Core.Tests.csproj
```

## Installer bauen, installieren, testen

Ausführliche Schritt-für-Schritt-Anleitung: [`BUILD-UND-INSTALLATION.md`](BUILD-UND-INSTALLATION.md).
Test-Strategie und Versions-Gates: [`TEST-STRATEGY.md`](TEST-STRATEGY.md). Checkliste für das
(noch) nicht automatisierte Alpha-Testing: [`ALPHA-TESTPLAN.md`](ALPHA-TESTPLAN.md).

Kurzfassung: `tools\InstallCreator\Start.cmd` (baut das Tool bei Bedarf automatisch aus dem
aktuellen Quellstand neu, statt die potenziell veraltete rohe `.exe` direkt zu starten) baut
den kompletten Payload automatisch mit und lässt daraus den **Admin-Installer** entstehen -
die einzige Datei, die an einen Sysadmin geht. Der **User-Installer** für die übrigen
Arbeitsplätze wird davon anschließend selbst aus dem laufenden Admin-Dashboard heraus
exportiert.

## Fehlerdiagnose

Agent und Konfigurationsprogramm loggen unbehandelte Ausnahmen (Typ, Meldung, Stacktrace -
nie Alarmtext oder Gerätedaten, NFR-5) nach `%ProgramData%\HaelpMi\crash.log`. Falls sich
ein Fenster unerwartet verhält oder schließt: diese Datei zuerst prüfen. Ein UI-seitiger
Fehler (`DispatcherUnhandledException`) wird geloggt, aber abgefangen - die App läuft
weiter, statt kommentarlos zu beenden.

Der Install-Creator (läuft nie beim Kunden, siehe `src/HaelpMi.InstallCreator`) loggt nach
demselben Muster, aber in eine eigene Datei ohne Abhängigkeit auf `%ProgramData%\HaelpMi`:
`%LocalAppData%\HaelpMi-InstallCreator\crash.log`.

## Manuell zu testende Punkte (nicht automatisierbar)

Diese Punkte hängen von echter Windows-Umgebung/Hardware/Gruppenrichtlinien ab und sind
bewusst *nicht* Teil der automatisierten Tests. Vollständige, laufend gepflegte Liste inkl.
Status: [`TEST-STRATEGY.md`](TEST-STRATEGY.md) (Abschnitte E–G) und
[`ALPHA-TESTPLAN.md`](ALPHA-TESTPLAN.md). Auszug:

- **Forced-Foreground gegen echte Vollbildanwendungen** (Spiele, Kiosk-Software,
  Remote-Desktop-Vollbild) auf allen im Netzwerk vorhandenen Windows-Versionen
- **Audio-Mute-Override auf echten Mehrgeräte-Setups** (mehrere gleichzeitig aktive
  Ausgabegeräte, Bluetooth-Kopfhörer, USB-Headsets) inkl. echter Hörbarkeitsprüfung
  (Loopback-Aufnahme, noch nicht automatisiert)
- **Autostart-Selbstregistrierung unter realen Gruppenrichtlinien** - kann durch GPO
  blockiert sein
- **Windows-Firewall-Freigabe** für Discovery-/Alarm-/Config-Sync-/Edit-Lock-/Update-Ports -
  muss ggf. per GPO ausgerollt werden
- **Voller Swap-Update-Durchlauf** (Testinstanz, Peer-Bestätigung, Port-Übernahme,
  Deinstallation der Altversion) gegen eine echte Vorversion - bewusst nur auf Wegwerf-VM,
  nicht automatisiert
- **Echte WPF-UI-Automatisierung** (FlaUI) - noch nicht aufgesetzt, geplant erst wenn ein
  UI-Bereich strukturell stabil ist

## Lizenz

Source-available, keine Open-Source-Lizenz im Sinne der OSI. Maßgeblich ist allein die Datei [`LICENSE`](LICENSE) (HälpMi-Lizenz, Fassung 1.0).

Kurzfassung: Der Quellcode ist öffentlich einsehbar und prüfbar. Privatpersonen dürfen HälpMi für private Zwecke kostenfrei nutzen. Jede Nutzung durch Organisationen (Unternehmen, Behörden, Vereine usw.) sowie jede Nutzung oder Weiterentwicklung zu geschäftlichen Zwecken bedarf einer gesonderten Vereinbarung. Die Verwendung für Konkurrenzprodukte ist ohne Freigabe untersagt.

Eine anwaltlich geprüfte Fassung der Lizenz ist in Vorbereitung und gilt ab ihrer Veröffentlichung für künftige Versionen.

Lizenzanfragen: info@it-scholle.de
Komponenten Dritter: siehe [`THIRD-PARTY-NOTICES`](THIRD-PARTY-NOTICES)

Die separate Kunden-Lizenzverwaltung (Ed25519-signierte Lizenzdateien je Kundengruppe,
Geräte-Limits, Tarifstufen, Ablaufwarnungen, Import über das Admin-Dashboard, siehe
`src/HaelpMi.Core/Licensing`) ist implementiert. Getrennt davon, aber noch **nicht**
umgesetzt: eine kryptografisch verifizierte Admin-Rolle (`Role.Admin` ist im aktuellen
Stand weiterhin eine unauthentifizierte Selbstauskunft des Peers) - siehe „Bekannte offene
Punkte" in [`docs/TECHSTACK-UND-ARCHITEKTUR.md`](docs/TECHSTACK-UND-ARCHITEKTUR.md).


