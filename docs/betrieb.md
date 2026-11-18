# Betrieb

## Installation und Start

- Bauen, signieren, Installer erzeugen: `docs/handbuch/entwickler-bauen-und-installieren.md`.
- An den Kunden geht nur der **Admin-Installer** (aus dem Install-Creator, Start über
  `tools/InstallCreator/Start.cmd`). User-Installer exportiert der Admin anschließend selbst aus
  dem Admin-Dashboard.
- Nach der Installation startet `HaelpMi.Agent` bei jeder Anmeldung über eine geplante Aufgabe
  (`schtasks`, `/SC ONLOGON /RL LIMITED`). Der Update-Dienst wird auf `release-1.0-MVP` nicht
  installiert; neue Versionen kommen per neuem Installer.
- Firewall-Freigaben setzt nur der Admin-Installer; in verwalteten Netzen ggf. per GPO ausrollen
  (Ports siehe `docs/architektur.md`).

## Konfiguration

- Gerätezustand und synchronisierte Konfiguration: `%ProgramData%\HaelpMi` (Dateien siehe
  `docs/architektur.md`, Abschnitt "Datenhaltung").
- Installer-fixe Werte (`CustomerGroupId`, Rolle, Test-Installer): `deployment.json` im
  Programmverzeichnis. Wird vom laufenden Programm nie neu geschrieben.
- Konfigurationsänderungen erfolgen im Admin-Dashboard und werden per Hot-Reload an alle Geräte
  im Kreis verteilt.

## Logs

| Log | Ablageort | Inhalt |
|---|---|---|
| `crash.log` (Agent, Config, UI) | `Z:\HaelpMi-Logs\<Rechnername>\`, sonst `%ProgramData%\HaelpMi\` | unbehandelte Ausnahmen (Typ, Meldung, Stacktrace), nie Alarmtext |
| `audit.log` | wie `crash.log` | Ereignis-Metadaten (wer hat was wann ausgelöst/empfangen), nie Alarmtext |
| `crash.log` (Install-Creator) | `%LocalAppData%\HaelpMi-InstallCreator\` | unbehandelte Ausnahmen des Entwickler-Tools |

Das Laufwerk `Z:` ist ein Zwischenschritt für die Alpha-Testphase (zentrale Ferndiagnose). Eine
Aufbewahrungsfrist, Rotation und eine Hash-Kette für das Audit-Log gibt es im aktuellen Stand nicht
(Abweichung von `AGENTS.md` Teil A, siehe `docs/STATUS.md`).

## Sicherung und Rückweg

- Programm-Updates: manuell per Installer über die bestehende Installation; die Konfiguration in
  `%ProgramData%\HaelpMi` bleibt erhalten. Rückweg: Installer der Vorversion erneut ausführen.
- Gerätezustand: `%ProgramData%\HaelpMi` sichern. Eine Neuinstallation übernimmt eine vorhandene
  Konfiguration.
- Entwickler-Rechner: `%LocalAppData%\HaelpMi-InstallCreator\` (Kunden-, Lizenzregister,
  Lizenz-Schlüsselpaare) sichern. Der private Update-Signaturschlüssel liegt in Vaultwarden.

## Bekannte Störungen

- Startverzögerung des Alarm-Selbsttests nach Admin-Installation:
  `docs/wissen/flaw-1-startup-race-diagnose.md`.
- Wartezeiten beim Laden/Starten: `docs/wissen/wartezeit-konzept.md`.
- Install-Creator zeigt alten Stand: über `tools/InstallCreator/Start.cmd` starten, nicht über die
  rohe `.exe` (siehe `docs/wissen/installcreator-rebuild-hook.md`).
