# HälpMi

LAN-Alarmierungssystem für Windows-Verwaltungsnetzwerke: Ein Arbeitsplatz löst per Hotkey einen
Alarm aus, die zugeordneten Arbeitsplätze erhalten sofort ein erzwungenes Popup mit Ton. Die Geräte
sprechen direkt miteinander (P2P, kein zentraler Server); ein App-interner Admin pflegt Gruppen und
Alarm-Profile über ein Dashboard, Änderungen verteilen sich automatisch an alle Geräte.

Status: Version 1.0 (v0.47.11) veröffentlicht, Weiterentwicklung auf `develop`, „stable+buggy“ (siehe [`docs/STATUS.md`](docs/STATUS.md)).

## Einstieg
1. Repo klonen.
2. Hooks aktivieren: `git config core.hooksPath .githooks` (baut u. a. den Install-Creator
   automatisch neu, siehe [`docs/wissen/installcreator-rebuild-hook.md`](docs/wissen/installcreator-rebuild-hook.md)).
3. .NET 8 SDK (Windows) und Inno Setup installieren; Details in
   [`docs/handbuch/entwickler-bauen-und-installieren.md`](docs/handbuch/entwickler-bauen-und-installieren.md).
4. Bauen und schnell testen:
   ```
   dotnet build HaelpMi.sln
   dotnet test tests\HaelpMi.Core.Tests\HaelpMi.Core.Tests.csproj
   ```
5. Install-Creator starten: `tools\InstallCreator\Start.cmd` (nicht die rohe `.exe`).
6. Weitere Befehle: `AGENTS.md`, Teil B, „Befehle“.

## Projektstruktur

```
src/
  HaelpMi.Core/            Modelle, JSON-Speicherung, Netzwerk (UDP/TCP), IPC, Win32-Interop, Audio, Lizenz
  HaelpMi.UI/              Gemeinsame WPF-Fenster (Popup, Dashboard, Konfiguration)
  HaelpMi.Agent/           Hintergrundprozess (Listener, Sender, Hotkey, Tray), autostartend
  HaelpMi.Config/          Konfigurationsprogramm / Admin-Dashboard, separater Prozess
  HaelpMi.InstallCreator/  Entwickler-Tool: baut pro Kunde den Admin-Installer
  HaelpMi.UpdateService/   Update-Dienst (derzeit inaktiv)
  HaelpMi.UpdateSigner/    Update-Signatur-Tool (derzeit inaktiv)
tests/
  HaelpMi.Core.Tests/          schnell, ohne Nebenwirkungen
  HaelpMi.InstallCreator.Tests/ schnell, ohne Nebenwirkungen
  HaelpMi.Installer.Tests/     verändert das System, nur auf Wegwerf-VM
installer/                 Inno-Setup-Skripte (User- und Admin-Installer)
design/                    Design-System: Icons, Farben, Katalog
docs/                      Architektur, Betrieb, Entscheidungen, Handbuch, Wissen, Status
tools/InstallCreator/      Startskripte für den Install-Creator
```

## Fehlerdiagnose

Agent und Konfigurationsprogramm loggen unbehandelte Ausnahmen (Typ, Meldung, Stacktrace, nie
Alarmtext oder Gerätedaten) nach `crash.log`; Ablageorte stehen in
[`docs/betrieb.md`](docs/betrieb.md). Ein UI-seitiger Fehler wird geloggt und abgefangen, die App
läuft weiter.

## Manuell zu testende Punkte

Hängen von echter Windows-Umgebung, Hardware oder Gruppenrichtlinien ab und sind bewusst nicht
automatisiert:

- Forced-Foreground gegen echte Vollbildanwendungen (Spiele, Kiosk, RDP-Vollbild)
- Audio-Mute-Override auf echten Mehrgeräte-Setups (Bluetooth, USB-Headsets)
- Autostart-Selbstregistrierung unter realen Gruppenrichtlinien
- Windows-Firewall-Freigaben (Ports siehe [`docs/architektur.md`](docs/architektur.md))
- Bildschirmschoner-Verhalten auf unterschiedlichen Windows-Versionen
- Installer-Update über eine vorhandene Vorversion mit laufendem Agent

Vollständige Checkliste: [`docs/handbuch/entwickler-alpha-testplan.md`](docs/handbuch/entwickler-alpha-testplan.md).

## Doku
- Arbeitsregeln: [`AGENTS.md`](AGENTS.md)
- Aktueller Stand: [`docs/STATUS.md`](docs/STATUS.md)
- Architektur und Betrieb: [`docs/architektur.md`](docs/architektur.md), [`docs/betrieb.md`](docs/betrieb.md)
- Entscheidungen: [`docs/entscheidungen/`](docs/entscheidungen/)
- Handbuch: [`docs/handbuch/`](docs/handbuch/)
- Hintergrundwissen, Anforderungen (Pflichtenheft): [`docs/wissen/`](docs/wissen/)
- Design-System: [`design/KATALOG.md`](design/KATALOG.md)
- Änderungen: [`CHANGELOG.md`](CHANGELOG.md)

## Lizenz

Source-available, keine Open-Source-Lizenz im Sinne der OSI. Maßgeblich ist allein die Datei [`LICENSE`](LICENSE) (HälpMi-Lizenz, Fassung 1.0).

Kurzfassung: Der Quellcode ist öffentlich einsehbar und prüfbar. Privatpersonen dürfen HälpMi für private Zwecke kostenfrei nutzen. Jede Nutzung durch Organisationen (Unternehmen, Behörden, Vereine usw.) sowie jede Nutzung oder Weiterentwicklung zu geschäftlichen Zwecken bedarf einer gesonderten Vereinbarung. Die Verwendung für Konkurrenzprodukte ist ohne Freigabe untersagt.

Eine anwaltlich geprüfte Fassung der Lizenz ist in Vorbereitung und gilt ab ihrer Veröffentlichung für künftige Versionen.

Lizenzanfragen: info@it-scholle.de
Komponenten Dritter: siehe [`THIRD-PARTY-NOTICES`](THIRD-PARTY-NOTICES)
