# Architektur

Beschreibt den aktuellen Stand des Codes. Bei Widerspruch zwischen diesem Dokument und dem Code
gilt der Code; verbindliche Regeln stehen in `AGENTS.md`, Begründungen in `docs/entscheidungen/`.

## Überblick

HälpMi ist eine LAN-Alarmierung ohne zentralen Server: Jedes Gerät im selben Netz spricht direkt
mit jedem anderen Gerät (P2P). Es gibt zwei App-interne Rollen, **Admin** und **User**, die nichts
mit Windows-Rechten zu tun haben; ein Windows-Standardnutzer kann App-Admin sein.

### Prozesse auf einem Gerät

1. **`HaelpMi.Agent`**: startet bei Anmeldung (Task Scheduler, kein Dienst, weil ein
   SYSTEM-Dienst keine UI auf dem interaktiven Desktop zeigen kann). Läuft rechtelos im
   User-Kontext, hält alle Sockets offen, reagiert auf Hotkeys, zeigt bei Alarm das erzwungene
   Popup, sonst nur Tray-Icon.
2. **`HaelpMi.Config`**: startet nur bei Bedarf (Tray-Klick), zeigt Konfiguration bzw. bei
   Admin-Rolle das Admin-Dashboard. Spricht nie selbst ins Netz, nur per Named Pipe mit dem Agent.
3. **`HaelpMi.UpdateService`** (Windows-Dienst, LocalSystem): auf `release-1.0-MVP` **inaktiv**,
   wird weder installiert noch gestartet (siehe "Programm-Update" unten). Updates laufen in dieser
   Linie ausschließlich manuell über einen neuen Installer.

### Kreis vs. Gruppe

- **Kreis**: netzwerktechnische Isolation über die `CustomerGroupId`, die der Install-Creator
  beim Bauen fest einbrennt. Jedes Paket trägt sie, jeder Empfänger verwirft fremde IDs sofort.
  Kein Dashboard-Objekt, keine Verwaltung.
- **Gruppe**: die einzige organisatorische Einheit im Admin-Dashboard (z. B. eine Etage), ein
  Zusammenschluss von Geräten/Räumen.

## Module und Schnittstellen

| Projekt | Typ | Aufgabe |
|---|---|---|
| `HaelpMi.Core` | Class Library | Modelle, JSON-Storage, Netzwerkprotokolle (UDP/TCP), IPC, Win32-Interop, Audio, Update-Krypto, Autostart; keine UI |
| `HaelpMi.UI` | WPF-Library | Gemeinsame Fenster/ViewModels (Popup, Dashboard, Config); bekommt Netzwerk/IPC per Context-Objekt |
| `HaelpMi.Agent` | WinExe (WPF + WinForms-Tray) | Hintergrundprozess: Listener, Sender, Hotkeys, Tray |
| `HaelpMi.Config` | WinExe (WPF) | Konfigurationsfenster bzw. Admin-Dashboard, separater Prozess |
| `HaelpMi.InstallCreator` | WinExe (WPF, Entwickler-Tool) | Baut pro Kunde den Admin-Installer (ruft `ISCC.exe`), erzeugt Kunden-/Gruppen-ID, Installer-Passwort, Lizenzdateien. Bewusst ohne Referenz auf Core/UI |
| `HaelpMi.UpdateService` | Windows-Dienst (Worker, LocalSystem) | Install/Test/Swap/Deinstall bei Auto-Updates, nur per lokaler Named Pipe |
| `HaelpMi.UpdateSigner` | CLI-Tool | Erzeugt das Update-Schlüsselpaar, signiert Update-Pakete; läuft nie beim Kunden |

Gemeinsame Oberflächen-Bausteine (Icons, Farben) liegen im Design-System-Verzeichnis `design/`
(siehe `design/KATALOG.md`).

### Netzwerk (Konstanten in `src/HaelpMi.Core/Models/AppConstants.cs`)

Fest, nicht konfigurierbar: eine Änderung erfordert ein Lockstep-Update aller Geräte.

| Port / Pipe | Protokoll | Zweck |
|---|---|---|
| 51500 | UDP-Broadcast | Boot-Call: Geräte-Erkennung inkl. Gossip (Antwort trägt bekannte Geräteliste) |
| 51501 | TCP | Alarm senden + Ack |
| 51502 | UDP-Broadcast | Config-Sync-Announce (neue Config-Versionsnummer) |
| 51503 | TCP | Vollständige Config abholen |
| 51504 | TCP | Exklusiv-Edit-Lock ("will editieren") |
| 51505 | TCP | Alarm-Feedback ("bin unterwegs", Status-Relay) |
| 51506 | TCP | Signiertes Update-Paket P2P abholen (inaktiv auf `release-1.0-MVP`) |
| Pipe `HaelpMi.Agent.Ipc` (+ Sitzungs-ID) | lokal | Config/Dashboard ↔ Agent |
| Pipe `HaelpMi.Agent.AlarmRelay`, `HaelpMi.Agent.AlarmFeedbackRelay` | lokal, sitzungsübergreifend | Weiterleitung zwischen Agent-Instanzen mehrerer Windows-Sitzungen |
| Pipe `HaelpMi.UpdateService.Ipc` | lokal | Agent ↔ Update-Dienst |

Kein Multicast, nur `255.255.255.255`-Broadcast (bleibt im Subnetz/VLAN). Jedes Paket wird vor
der Verarbeitung auf die `CustomerGroupId` geprüft. Kein Heartbeat, kein Polling.

### Windows-APIs

| Mechanismus | Wofür |
|---|---|
| `RegisterHotKey`/`UnregisterHotKey` | ein globaler Hotkey pro Alarm-Profil |
| `GetSystemMetrics(SM_REMOTESESSION)` | RDP-Erkennung, reine Live-Abfrage, nie langfristig geloggt |
| `SetThreadExecutionState` + `SendInput` | Bildschirmschoner während der Alarmanzeige unterdrücken |
| `schtasks.exe` | Autostart (`/SC ONLOGON /RL LIMITED`) |
| Dienst `HaelpMiUpdateService` | per `sc.exe create` im Installer registriert |
| `netsh advfirewall` | nur Admin-Installer: Firewall-Freigaben |

## Datenflüsse

### Gerät bootet
Der Agent sendet einen UDP-Broadcast (51500) mit seiner `CustomerGroupId`. Geräte im selben Kreis
antworten und hängen ihre bekannte Geräteliste an (Gossip). Ergebnis: `devices.json`.

### Alarm
Hotkey oder UI → Agent öffnet TCP-Verbindungen (51501) zu den Empfängern der Gruppe → Empfänger
bestätigt, zeigt das erzwungene Popup, unterdrückt den Bildschirmschoner, spielt den Ton auf allen
Audiogeräten auch bei Stummschaltung. Antworten laufen über den Feedback-Kanal (51505) zurück und
werden im Status-Banner gesammelt.

### Config-Änderung
Kein Save-Button: bei Blur eines gültig ausgefüllten Pflichtfelds wird gespeichert und die neue
`ConfigVersion` per UDP (51502) angekündigt. Geräte mit älterem Stand holen die Config per TCP
(51503) und laden sie im laufenden Prozess neu (Hot-Reload). Jede Änderung landet in der
Änderungshistorie mit Undo.

### Zwei Admins gleichzeitig
Beim Öffnen einer Gruppe/eines Alarm-Profils sendet das Dashboard "will editieren" (51504) an alle
erreichbaren Admin-Geräte im Kreis. Granularität pro Datensatz. Bei Millisekunden-Gleichstand
lehnen sich beide ab und versuchen es nach zufälligem Backoff (200–800 ms) erneut. Auto-Freigabe
nach 10 Minuten ohne Aktivität.

### Programm-Update
**Auf `release-1.0-MVP` inaktiv** (seit v0.45.1): Update-Dienst, Update-Signer und die
zugehörigen Core-Klassen sind als `DEPRECATED für release-1.0-MVP` markiert und werden weder
installiert noch gestartet; der Quellcode bleibt für die Reaktivierung in einem späteren Release
erhalten. Updates erfolgen manuell per Installer. Vorgesehene Pipeline:

Getrennt vom Config-Hot-Reload, über die Swap-Pipeline (`UpdateOrchestrator`,
`UpdateServiceWorker`): neuere Peer-Version erkannt → Admin-Freigabe für genau diese Version →
Wellen-Gate (`IsMyTurn`) → Lockout-Prüfung (Kill-Switch nach 3 Fehlschlägen in Folge, 24 h) →
Jitter → signiertes Paket per P2P (51506) → Update-Dienst installiert auf separatem Testport und
testet → erst nach lokalem Erfolg **und** mindestens einer Peer-Bestätigung Swap, Altversion
nach `_previous`, danach entfernt. Nur Ed25519-signierte Pakete werden angenommen.

## Datenhaltung

JSON via `System.Text.Json`, atomar geschrieben (Temp-Datei + `File.Replace` mit Retry). Root:
`%ProgramData%\HaelpMi` (Maschinen-Scope).

| Datei | Inhalt |
|---|---|
| `settings.json` | lokale Geräte-Identität (DeviceId, Raum, Rolle-Cache) |
| `devices.json`, `removed-devices.json` | entdeckte bzw. entfernte Peer-Geräte |
| `shared-config.json` | synchronisierte Konfiguration (Gruppen, Alarm-Profile, Update-Rollout) |
| `config-history.json` | Änderungshistorie inkl. Undo-Snapshots |
| `update-lockout.json` | Kill-Switch-Zustand der Update-Pipeline |
| `lizenz.json`, `lizenz-pending.json`, `license-reminder.json` | Lizenzstand des Geräts |
| `{app}\deployment.json`, `installed-by.json` | Installer-fixe Werte (CustomerGroupId, Role, IsTestInstaller) |
| `audit.log`, `crash.log` | Logs, siehe `docs/betrieb.md` |

Install-Creator (nur Entwickler-Rechner, `%LocalAppData%`): `kundenregister.json`,
`lizenzregister.json`, `lizenzschluesselpaare.json`.

## Kryptografie

Getrennte Schlüssel, nie verwechseln oder zusammenlegen (Regeln: `AGENTS.md` Teil B):

| # | Zweck | Stand |
|---|---|---|
| 1 | Kunden-Lizenzsignatur (Ed25519) | im Install-Creator erzeugt und signiert |
| 2 | Update-Signatur (Ed25519, eigener Schlüssel) | implementiert, auf `release-1.0-MVP` inaktiv; eingebetteten Public Key vor Reaktivierung prüfen |
| 3 | Installer-Passwort pro Kunde | kein Schlüsselpaar, optionales Inno-Setup-Passwort |

## Externe Abhängigkeiten

| Paket / Werkzeug | Version | Wo | Wofür |
|---|---|---|---|
| `NAudio` (MIT) | 2.2.1 | Core | Alarmton gleichzeitig über alle Ausgabegeräte (WASAPI) |
| `BouncyCastle.Cryptography` | 2.4.0 | Core, UpdateSigner, InstallCreator | Ed25519 (in .NET 8 nicht nativ) |
| `Microsoft.Extensions.Hosting.WindowsServices` | 8.0.1 | UpdateService | SCM-Integration |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | 2.9.2 / 2.8.2 / 17.11.1 | Tests | Test-Framework |
| Inno Setup (`ISCC.exe`) | – | `installer/` | Installer (nicht WiX) |

Kein Logging-Framework (eigene `CrashLogger`/`AuditLog`), kein DI-Container außer im Worker-Service.

## Nicht im aktuellen Stand (nur `legacy/main-0.39.x`)

Frühere Fassungen dieses Dokuments und der `CLAUDE.md` beschrieben Funktionen, die nur im
eingefrorenen Legacy-Stand existieren und im aktuellen Code fehlen. Sie werden bei Bedarf über
eigene Tickets neu entwickelt (siehe `docs/entscheidungen/0001-neustart-auf-v0-16.md`):

- Revisionssicheres Audit-Log (Hash-Kette `audit.jsonl`, `AuditSyncService`, Ports 51507/51508,
  `AuditIngestStore`)
- Kryptografisch verifizierte Admin-Rolle (`AdminRoleSigner`/`AdminRoleVerifier`,
  `AdminRoleTrustStore`, `AdminRoleKeySyncService`, `SecureEnvelope`)
- Self-Bootstrap-Update (`HaelpMi.UpdateBootstrapper`, `EmbeddedPackage`, Knopf "Update erstellen")
- LAN-Verschlüsselung, Fast User Switching (in der Legacy-Fassung)

## Bekannte offene Punkte

- ARM64 ist Ziel (`Platforms` enthält es), aber nicht Teil der Build-Pipeline.
- FlaUI-UI-Suite und Audio-Loopback-Suite sind nicht gebaut.
- Änderungshistorie und Antwort-Log enthalten Klarnamen; mit dem Auftraggeber bezüglich
  Personalrat abzustimmen.
- `Role.Admin` ist eine unauthentifizierte Selbstauskunft des Peers.
