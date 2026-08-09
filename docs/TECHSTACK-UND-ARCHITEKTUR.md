# HälpMi — Tech-Stack & Funktionsweise

Stand: 09.08.2026, Version 0.7.6 (Alpha). Dieses Dokument ist eine **Momentaufnahme zum
Nachschlagen** ("was läuft hier eigentlich, falls jemand fragt") — keine dauerhaft gepflegte
Architekturvorgabe. Die verbindlichen Regeln stehen weiterhin in `CLAUDE.md`, die
Versions-/Test-Gates in `TEST-STRATEGY.md`. Bei Widersprüchen zwischen diesem Dokument und dem
Code gilt der Code; bei Widersprüchen zu Architekturregeln gilt `CLAUDE.md`.

---

## Teil 1 — Tech-Stack-Referenz

### Plattform

| Baustein | Wahl |
|---|---|
| Runtime | .NET 8, `net8.0-windows` |
| Sprache | C# |
| UI-Framework | WPF (+ punktuell `System.Windows.Forms` für Tray-Icon/Clipboard/Hotkey-Fenster) |
| Zielarchitektur | aktuell x64; ARM64 geplant (siehe Notiz unten) |
| Build | `dotnet publish` self-contained, single-file, `-r win-x64` (kein separates .NET-Runtime-Setup beim Kunden nötig) |

### Prozesse / Projekte (aus `HaelpMi.sln`)

| Projekt | Typ | Zweck |
|---|---|---|
| `HaelpMi.Core` | Class Library | Modelle, JSON-Storage, Netzwerkprotokolle (UDP/TCP), IPC, Win32-Interop, Audio, Update-Krypto, Lizenz-Platzhalter, Autostart — keine UI |
| `HaelpMi.UI` | WPF-Library | Gemeinsame Fenster/ViewModels (Popup, Dashboard, Config); kennt Netzwerk/IPC nicht direkt, bekommt es per Context-Objekt injiziert |
| `HaelpMi.Agent` | WinExe (WPF + WinForms-Tray) | Rechtelos laufender Hintergrundprozess: hält alle Sockets/Listener offen, sendet Alarme, verwaltet Hotkeys, Tray-Icon. Kein eigenes Fenster |
| `HaelpMi.Config` | WinExe (WPF) | Sichtbares Konfigurationsfenster bzw. (bei Admin-Rolle) Admin-Dashboard; separater Prozess, spricht per Named Pipe mit dem Agent |
| `HaelpMi.InstallCreator` | WinExe (WPF, Entwickler-Tool) | Baut pro Kunde den Admin-Installer (ruft Inno Setups `ISCC.exe` auf), erzeugt Kunden-/Gruppen-ID + optionales Installer-Passwort. Bewusst ohne Referenz auf Core/UI |
| `HaelpMi.UpdateService` | Windows-Dienst (`Worker`, LocalSystem) | Einzige privilegierte Komponente: Install/Test/Swap/Deinstall bei Auto-Updates. Kommuniziert nur per lokaler Named Pipe, nie TCP übers Netz |
| `HaelpMi.UpdateSigner` | CLI-Tool | Erzeugt das Ed25519-Update-Schlüsselpaar, signiert Update-Pakete. Läuft nie beim Kunden, nur beim Release-Bauen |
| `HaelpMi.Core.Tests` | xUnit | Systemunverändernde Tests, laufen bei jedem `dotnet test` |
| `HaelpMi.Installer.Tests` | xUnit | End-to-End gegen echte Installer, verändert das System real — nur manuell/auf Wegwerf-VM |

### NuGet-Pakete

| Paket | Version | Wo | Wofür |
|---|---|---|---|
| `NAudio` | 2.2.1 (MIT) | Core | Alarmton gleichzeitig über **alle** aktiven Audio-Ausgabegeräte (WASAPI), hebt Stummschaltung temporär auf |
| `BouncyCastle.Cryptography` | 2.4.0 | Core, UpdateSigner | Ed25519-Signaturen (.NET 8 hat kein natives Ed25519); Core nur Verify-Seite, UpdateSigner zusätzlich Keygen/Sign |
| `Microsoft.Extensions.Hosting.WindowsServices` | 8.0.1 | UpdateService | Offizielle SCM-Integration (Start/Stop, Ereignisprotokoll) statt Eigenbau |
| `Microsoft.NET.Test.Sdk` | 17.11.1 | beide Testprojekte | Test-Runner-Infrastruktur |
| `xunit` | 2.9.2 | beide Testprojekte | Test-Framework |
| `xunit.runner.visualstudio` | 2.8.2 | beide Testprojekte | VS/CLI-Testadapter |

Kein JSON-Paket (natives `System.Text.Json`), kein Logging-Framework (eigene `CrashLogger`/
`AuditLog`-Klassen), kein DI-Container außer im Worker-Service.

### Netzwerkprotokolle & Ports

Alle Konstanten zentral in `src/HaelpMi.Core/Models/AppConstants.cs`. **Fix, nicht
konfigurierbar** — eine Änderung würde ein Lockstep-Update aller Geräte erfordern.

| Port | Protokoll | Zweck |
|---|---|---|
| 51500 | UDP, Broadcast | Boot-Call: Geräte-Erkennung inkl. "Gossip" (Reply trägt bekannte Geräteliste mit) |
| 51501 | TCP | Alarm senden + Ack empfangen |
| 51502 | UDP, Broadcast | Config-Sync-Announce (neue Config-Versionsnummer) |
| 51503 | TCP | Vollständige Config abholen, nachdem ein neuerer Announce gesehen wurde |
| 51504 | TCP | Exklusiv-Edit-Lock ("will editieren") |
| 51505 | TCP | Alarm-Feedback ("bin unterwegs", Status-Relay) |
| 51506 | TCP | Signiertes Update-Paket P2P abholen |
| Named Pipe `HaelpMi.Agent.Ipc` | lokal | Config/Dashboard ↔ Agent |
| Named Pipe `HaelpMi.UpdateService.Ipc` | lokal, ACL für "Authenticated Users" | Agent ↔ privilegierter Update-Dienst |

Kein Multicast — nur `255.255.255.255`-Broadcast (bleibt auf dasselbe Subnetz/VLAN begrenzt).
Jedes Paket trägt eine `CustomerGroupId`; sie wird in jedem Empfangspfad geprüft, bevor
irgendetwas weiterverarbeitet wird (Kreis-Isolation, siehe Teil 2). Kein Heartbeat/Polling —
Netzverkehr nur bei Boot, Alarm, Config-Änderung, Update-Verteilung.

### Windows-APIs & Windows-Dienste

| Mechanismus | Wofür |
|---|---|
| `RegisterHotKey`/`UnregisterHotKey` (user32.dll) | Ein globaler Hotkey pro Alarm-Profil |
| `GetSystemMetrics(SM_REMOTESESSION)` | RDP-Erkennung, reine Live-Abfrage, nie langfristig geloggt |
| `SetThreadExecutionState` + `SendInput` | Bildschirmschoner während Alarm-Anzeige unterdrücken (rührt Sperrbildschirm nicht an) |
| `schtasks.exe` (Task Scheduler, **kein** COM) | Autostart, per-User-Task `/SC ONLOGON /RL LIMITED` — bewusst kein SYSTEM-Dienst wegen Session-0-Isolation |
| Windows-Dienst `HaelpMiUpdateService` | Per `sc.exe create` im Installer registriert, läuft als LocalSystem, einzige privilegierte Komponente |
| `netsh advfirewall firewall add rule` | Nur im Admin-Installer: Firewall-Freigabe für Discovery/Alarm-Port |

### Kryptografie — drei getrennte Mechanismen

| # | Zweck | Stand |
|---|---|---|
| 1 | Kunden-Lizenzsignatur (Ed25519) | nur Platzhalter (`LicensePublicKey.cs`), Mechanismus noch nicht implementiert |
| 2 | Update-Signatur (Ed25519, separater Schlüssel) | implementiert (SHA-256-Hash + Ed25519-Signaturprüfung), **eingebetteter Public Key ist noch ein Wegwerf-Dev-Key** — vor jedem echten Release ersetzen |
| 3 | Installer-Passwort pro Kunde | kein Schlüsselpaar, optionales Inno-Setup-Passwort, 20-stellig, verwechslungsarmes Alphabet |

### Datenhaltung

Alles **JSON via `System.Text.Json`**, atomar geschrieben (Temp-Datei + `File.Replace` mit
Retry). Root: **`%ProgramData%\HaelpMi`** (Maschinen-Scope, nicht `%AppData%` — Geräte-ID/Raum
hängt am physischen Gerät, nicht am Windows-Konto). Keine SQLite/XML.

| Datei | Inhalt |
|---|---|
| `settings.json` | lokale Geräte-Identität (DeviceId, Raum, Rolle-Cache) |
| `devices.json` | entdeckte Peer-Geräte (Boot-Call-Cache) |
| `shared-config.json` | synchronisierte Konfiguration (Gruppen, Alarm-Profile, Update-Rollout) |
| `config-history.json` | Änderungshistorie inkl. Undo-Snapshots |
| `audit.log` | Klartext-Ereignislog (Timestamp, Ereignis), nie Alarmtext |
| `{app}\deployment.json` | Installer-fixe Werte (CustomerGroupId, Role, IsTestInstaller) |
| `crash.log` | unbehandelte Ausnahmen, keine Alarmdaten |

### Installer & Build

- **Inno Setup** (nicht WiX): `HaelpMi.iss` (User-Installer), `HaelpMi-Admin.iss`
  (zusätzlich Firewall-Regeln), `HaelpMiCommon.iss.inc` (gemeinsame Logik).
- **Install-Creator** — eigenes Entwickler-WPF-Tool, ruft `ISCC.exe` auf, baut den
  Admin-Installer (der wiederum den kompletten Bausatz für spätere User-Installer-Exporte
  aus dem Dashboard heraus enthält).
- **`Directory.Build.props`** — zentrale Versionsnummer für alle Projekte, muss von Hand
  synchron zu `MyAppVersion` in `HaelpMiCommon.iss.inc` gehalten werden.

### Tests

xUnit durchgehend. `Core.Tests` (~1500 Zeilen, 8 Dateien: Audio, JsonFileStore,
MessageValidation, Models, Networking, Sending, Storage, Updates) läuft bei jedem
`dotnet test`. `Installer.Tests` (9 sequenzielle Schritte, steuert Inno-Setup-Wizard per
`SendKeys`) verändert das System real — nur auf Wegwerf-VM. Versions-Gates siehe
`TEST-STRATEGY.md` (Patch → Smoke-Set, Minor → volle coded Suite, Major → zusätzlich
FlaUI/Audio-Loopback, beide noch nicht gebaut).

---

## Teil 2 — Wie alles zusammenhängt

### Grundidee

HälpMi ist eine LAN-Alarmierung ohne zentralen Server: Jedes Gerät im selben Netz redet direkt
mit jedem anderen Gerät (P2P). Es gibt zwei App-interne Rollen — **Admin** und **User** — die
nichts mit Windows-Rechten zu tun haben; ein Windows-Standardnutzer kann App-Admin sein.

### Prozesslandschaft auf einem Gerät

Auf jedem Rechner laufen (nach Installation) bis zu drei eigene Prozesse:

1. **`HaelpMi.Agent`** — startet automatisch bei Anmeldung (Task-Scheduler, nicht als Dienst,
   weil ein SYSTEM-Dienst keine UI im Interactive-Desktop zeigen könnte). Läuft rechtelos im
   User-Kontext, hält alle Netzwerk-Sockets offen, reagiert auf Hotkeys, zeigt bei Alarm das
   erzwungene Popup, sitzt sonst nur als Tray-Icon im Hintergrund.
2. **`HaelpMi.Config`** — startet nur bei Bedarf (Tray-Klick), zeigt das Konfigurationsfenster
   bzw. bei Admin-Rolle das Admin-Dashboard. Redet nie selbst über das Netzwerk, sondern nur
   per lokaler Named Pipe mit dem Agent, der stellvertretend sendet/empfängt.
3. **`HaelpMi.UpdateService`** (Windows-Dienst, LocalSystem) — läuft dauerhaft im Hintergrund,
   aber komplett passiv, bis der Agent ihn per Named Pipe zu einem Update-Swap anweist. Das ist
   die einzige Komponente mit echten Windows-Rechten im ganzen System.

### Kreis vs. Gruppe — der wichtigste Begriffsunterschied

- **Kreis** = rein netzwerktechnische Isolation über die `CustomerGroupId`, die beim Bauen des
  Installers (Install-Creator) fest eingebrannt wird. Jedes Netzwerkpaket trägt diese ID, jeder
  Empfänger verwirft Pakete mit fremder ID sofort. Zwei unabhängige HälpMi-Installationen im
  selben physischen Netz (z. B. zwei Kunden im selben Gebäude) beeinflussen sich dadurch nie.
  Der Kreis ist **kein Objekt im Dashboard** — er ist automatisch eindeutig pro Installation und
  braucht keine Verwaltung.
- **Gruppe** = die einzige organisatorische Einheit, die ein Admin im Dashboard tatsächlich
  anlegt und pflegt — ein Zusammenschluss von Geräten/Räumen, z. B. eine Etage. Frühere Entwürfe
  hatten "Kreis" fälschlich als eigene Zwischenebene zwischen Gerät und Gruppe geführt; das
  wurde am 04.08.2026 korrigiert.

### Ablauf: Ein Gerät bootet

Der Agent startet, sendet einen UDP-Broadcast-Announce (Port 51500) mit seiner
`CustomerGroupId`. Andere Geräte im selben Kreis antworten und hängen ihre eigene bekannte
Geräteliste an ("Gossip") — so verbreitet sich die Geräteliste auch über Geräte, die gerade
nicht gleichzeitig online sind, ohne dass irgendein Gerät eine Autorität dafür wäre. Ergebnis
landet in `devices.json`.

### Ablauf: Ein Alarm wird ausgelöst

Nutzer drückt Hotkey (oder löst über UI aus) → Agent öffnet TCP-Verbindungen (Port 51501) zu
den Empfängergeräten der konfigurierten Gruppe → Empfänger bestätigt (Ack), zeigt sein
erzwungenes Popup, unterdrückt währenddessen den Bildschirmschoner, spielt den Ton auf allen
Audiogeräten auch bei Stummschaltung. Antworten wie "bin unterwegs" laufen über einen separaten
Feedback-Kanal (Port 51505) zurück zum Sender, der sie im Status-Banner sammelt ("x von y
geantwortet"). Alles landet fürs spätere Nachschlagen im Alarm-Log — das einzige Protokoll, das
bewusst Klarnamen enthalten darf (mit dem Auftraggeber bezüglich Personalrat abzustimmen).

### Ablauf: Eine Config-Änderung

Kein Save-Button — sobald ein Pflichtfeld im Dashboard den Fokus verliert und gültig ausgefüllt
ist, wird sofort lokal gespeichert und die neue `ConfigVersion` per UDP-Broadcast (Port 51502)
angekündigt. Andere Geräte, die einen höheren Versionsstand sehen, holen sich die komplette
Config aktiv per TCP (Port 51503) ab und laden sie im laufenden Prozess neu (Hot-Reload, kein
Prozess-Neustart wie beim Programm-Update). Jede Änderung landet zusätzlich in der
Änderungshistorie mit Undo.

### Ablauf: Zwei Admins bearbeiten gleichzeitig

Beim Öffnen einer Gruppe oder eines Alarm-Profils zur Bearbeitung sendet das Dashboard einen
TCP-Call "will editieren" (Port 51504) an alle erreichbaren Admin-fähigen Geräte im selben
Kreis. Granularität ist pro Datensatz — zwei Admins dürfen parallel an unterschiedlichen
Gruppen/Profilen arbeiten. Kollidieren zwei Anfragen auf Millisekundenebene, lehnen sich beide
gegenseitig ab und probieren nach zufälligem Backoff (200–800 ms) erneut. Ohne Aktivität wird
ein Lock nach 10 Minuten automatisch wieder freigegeben.

### Ablauf: Ein Programm-Update wird verteilt

Läuft komplett getrennt vom Config-Hot-Reload, über die "Swap-Pipeline"
(`UpdateOrchestrator.cs` + `UpdateServiceWorker.cs`): Ein Gerät erkennt eine neuere Peer-Version
→ prüft Admin-Freigabe/Rollout-Quote → prüft, ob es wegen zu vieler Fehlschläge gerade in einem
24-Stunden-Lockout steckt (Kill-Switch nach 3 Fehlschlägen in Folge) → wartet zufälligen Jitter
ab → zieht sich das signierte Paket per P2P (Port 51506) → lässt den privilegierten
`UpdateService` das Paket auf einem separaten Testport installieren und selbst testen → erst
nach lokalem Erfolg **und** mindestens einer Peer-Bestätigung wird die neue Version scharf
geschaltet (alte Version nach `_previous`, neue Version übernimmt) und die Altversion entfernt.
Nur signierte Pakete (Ed25519, separater Update-Schlüssel) werden überhaupt angenommen.

### Datenschutz in der Praxis

UI zeigt immer Raum/Raumnummer groß, Benutzername klein. Die RDP-Erkennung
(`SM_REMOTESESSION`) ist eine reine Momentaufnahme für die Alarmanzeige, wird nirgends dauerhaft
protokolliert — außer im ohnehin vorgesehenen Alarm-Log, das erfasst, wer wie auf welchen Alarm
reagiert hat. Crash-Log und Audit-Log enthalten keine Alarmtexte, nur Ereignis-Metadaten.

### Bekannte offene Punkte (Stand dieses Dokuments)

- Lizenzsignatur (Mechanismus 1 der Kryptografie) ist nur ein Platzhalter, noch nicht
  implementiert.
- Der eingebettete Update-Signatur-Public-Key ist noch ein Wegwerf-Dev-Key und muss vor dem
  ersten echten Release ersetzt werden.
- ARM64-Build ist Ziel, aber noch nicht Teil der aktuellen Build-Pipeline (aktuell nur x64).
- FlaUI-UI-Suite und Audio-Loopback-Suite (für Major-Releases vorgesehen) sind noch nicht
  gebaut.
- `Installer.Tests` sind laut Projektstand noch nie live end-to-end gegen echte Installer-
  Dateien gelaufen, nur strukturell verifiziert.
- Änderungshistorie/Antwort-Log enthalten Klarnamen — das ist mit dem Auftraggeber bezüglich
  Personalrat noch abzustimmen, nicht eigenmächtig zu erweitern.
