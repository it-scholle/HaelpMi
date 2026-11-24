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

## E2E-Umgebung

Testfälle und Auslöser: `docs/wissen/test-strategie.md`, Abschnitt H. Die Umgebung besteht aus
dem Orchestrator-LXC (Self-Hosted-Runner `pve-orchestrator`) auf PVE 2 und den Test-VMs 9101 (A)
und 9102 (B) mit je einem eigenen Runner (`haelpmi-e2e`, `vm-a`/`vm-b`).

Orchestrator einrichten:

- LXC (Debian 12) mit `features: nesting=1` anlegen, sonst dauert der SSH-Login rund 25 s.
- Debian 12 bringt kein curl mit: vorher `apt-get install -y curl ca-certificates`.
- `ci/e2e/orchestrator-einrichten.sh` als root ausführen (idempotent; `--pruefen` prüft nur,
  `--api-test` testet das PVE-Token). Es installiert den Runner, kopiert `pve-lib.sh`,
  `pve-reset.sh`, `pve-stop.sh` und `wachhalten.sh` ins CI-Verzeichnis und legt den
  Montags-Timer an.
- Konfiguration unter `/etc/haelpmi-ci` (Werte nie ins Repo):

| Datei | Schlüssel |
|---|---|
| `pve.conf` | `PVE_API`, `PVE_NODE` |
| `pve-token` | `PVEAPIToken=<id>=<wert>` (Token nur mit VM.Audit, VM.PowerMgmt, VM.Snapshot.Rollback) |
| `e2e.conf` | `VM_<vmid>_IP`, `SSH_USER`, optional `WACHHALTEN_VMIDS` |
| `mqtt.conf` | `MQTT_HOST`, `MQTT_PORT`, `MQTT_USER`, `MQTT_PASS` (optional) |

Skripte unter `ci/e2e/` (alle mit `--trocken`):

| Skript | Zweck |
|---|---|
| `pve-reset.sh <vmid>...` | Rollback auf `clean-base`, Start, warten bis die Runner online sind (max. 10 min) |
| `pve-stop.sh <vmid>...` | Herunterfahren (ACPI, notfalls harter Stopp); im Workflow mit `if: always()` |
| `wachhalten.sh` | Montags 02:30 per systemd-Timer: VMs starten, Runner online abwarten, wieder stoppen |
| `rendezvous-relay.sh` | Spiegelt während eines Laufs die Markendateien zwischen A und B |

Laufzeit (Lauf 2, alles grün): gesamt rund 21 min, davon T1 rund 1 min je VM. Der Engpass ist
der Download der Artefakte auf die VMs (rund 130 KB/s); er macht den Großteil der rund 17 min
der Test-Jobs aus.

14-Tage-Regel: GitHub löscht Runner-Registrierungen nach 14 Tagen ohne Verbindung. Der
Montags-Timer startet die VMs deshalb einmal pro Woche. Meldet `pve-reset.sh` „Runner neu
registrieren“, muss die Registrierung erneuert werden.

Bei Fehlern:

- Logs im Workflow-Lauf; die Artefakte `e2e-a`/`e2e-b` enthalten trx, Screenshots, Diagnose
  (`netzwerk.txt`, JSON-Dateien) und die `*.log` aus `%ProgramData%\HaelpMi`.
- Bleibt eine VM nach einem abgebrochenen Lauf an: `pve-stop.sh <vmid>` auf dem Orchestrator
  oder die VM in PVE herunterfahren.

Pflege der Vorlage: Die Test-VMs sind Klone einer Vorlage (eingerichtet mit
`ci/e2e/vorlage-einrichten.ps1`). Eine neue Vorlage heißt: alle Klone neu anlegen, Runner neu
registrieren und den Snapshot `clean-base` neu setzen.

Die Vorlage braucht ein .NET-10-SDK x64 (bringt die Windows Desktop Runtime 10 mit), weil die
Test-Jobs `dotnet test` mit der `net10.0-windows`-Test-DLL auf der VM ausführen. Das Skript
`vorlage-einrichten.ps1` installiert es nicht; von Hand als Admin:
`winget install --id Microsoft.DotNet.SDK.10 --exact --silent --accept-package-agreements --accept-source-agreements`.
Prüfen: `dotnet --list-runtimes` zeigt `Microsoft.WindowsDesktop.App 10.0.x`.
