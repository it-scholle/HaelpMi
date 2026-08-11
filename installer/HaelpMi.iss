; HälpMi installer (Inno Setup) - User-Installer (FR-35): schreibt Role=User in
; deployment.json (siehe HaelpMiCommon.iss.inc/GetRoleValue), bringt keine Firewall-Regeln
; mit (dafür HaelpMi-Admin.iss - der Admin-Rechner dient laut FR-35 zugleich als Testgerät,
; braucht die Regeln also eher). Beide Varianten teilen sich dieselbe App-Payload/Binary;
; einzeln aktivierbare/deaktivierbare Funktionen aus FR-35 ("jede Funktion einzeln
; aktivierbar") sind NICHT umgesetzt - dafür fehlt im C#-Code noch ein Feature-Flag-
; Mechanismus, das ist eine eigene Design-Entscheidung, keine reine Installer-Frage.
;
; Kunden-/Gruppen-ID (FR-49) kommt per ISCC-Kommandozeile vom (noch nicht existierenden)
; Install-Creator: siehe #define CustomerGroupId/IsTestInstaller in HaelpMiCommon.iss.inc.
; Ohne diese Angabe kompiliert das Skript trotzdem (Nullen-ID, IsTestInstaller=true) - für
; lokales Testen ausreichend, für eine echte Kunden-Auslieferung nicht.
;
; Machine-Scope (FR-34): braucht Adminrechte, weil lokaler Zustand seit Teil 2 unter dem
; maschinenweiten %ProgramData%\HaelpMi liegt (siehe [Dirs]/Permissions unten im .inc).
;
; Build the payload first (from the repo root - siehe BUILD-UND-INSTALLATION.md für die
; verbindliche, zuletzt geprüfte Fassung dieser Befehle):
;   dotnet publish src\HaelpMi.Agent\HaelpMi.Agent.csproj  -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o installer\payload
;   dotnet publish src\HaelpMi.Config\HaelpMi.Config.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o installer\payload
;   dotnet publish src\HaelpMi.UpdateService\HaelpMi.UpdateService.csproj -c Release -r win-x64 -p:Platform=x64 --self-contained true -p:PublishReadyToRun=true -o installer\payload
; then compile this script with ISCC.exe (Inno Setup 6 or 7).
;
; NICHT mehr -p:PublishSingleFile=true (06.08.2026 verworfen): ein normaler mehrdateiiger
; self-contained-Ordner mit PublishReadyToRun startet nachweislich spürbar schneller (eigene
; App-DLLs vorkompiliert statt kalt gejittet, kein Bundle-Extraktions-Overhead) - siehe
; BUILD-UND-INSTALLATION.md für die Messung/Begründung.
;
; Self-contained (bundles the .NET 8 runtime) so office workstations need no separate
; runtime install/internet access (Pflichtenheft 2.: no internet dependency). Built for
; win-x64 - the vast majority of real-world office PCs; a network with ARM64 Windows
; devices would need a second payload/installer built with -r win-arm64.

#include "HaelpMiCommon.iss.inc"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\HaelpMi
DefaultGroupName=HälpMi
DisableProgramGroupPage=yes
OutputBaseFilename=HaelpMi-Setup-{#MyAppVersion}
OutputDir=Output
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
; Machine-Scope (FR-34): braucht Adminrechte/UAC-Prompt für die maschinenweite Installation
; unter Program Files + die ProgramData-Rechtevergabe (siehe HaelpMiCommon.iss.inc). Die
; Autostart-Registrierung bleibt trotzdem ein per-User-Task-Scheduler-Eintrag, den der
; Agent selbst zur Laufzeit anlegt (5.3/5.7) - nur die Installation selbst ist elevated,
; der laufende Agent bleibt weiterhin ohne Admin/SYSTEM-Rechte.
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAgentExeName}
SetupIconFile=haelpmi_icon.ico
; Nutzerwunsch 06.08.2026: die "Schließe die Anwendungen automatisch"-Rückfrage von Inno's
; eingebautem Restart Manager spart sich der Nutzer. Erster Versuch war CloseApplications=
; force - das unterdrückt die Rückfrage NICHT: Inno's Restart-Manager-Erkennung (und die
; Seite, die daraus die Rückfrage baut) läuft auf der "Preparing to Install"-Seite, VOR
; ssInstall - unsere eigene CurStepChanged(ssInstall)-Prozedur mit ihrem taskkill kommt also
; erst danach dran, zu spät, um die Erkennung/Seite zu verhindern (per Fehlerbericht
; bestätigt: Rückfrage kam trotz "force" wieder). CloseApplicationsFilter= (leer) sollte laut
; Doku die Erkennung selbst komplett abschalten, ISCC lehnt eine leere Angabe aber als
; ungültigen Wert ab (per Testkompilierung entdeckt) - CloseApplications=no allein reicht
; ohnehin: es schaltet dieselbe Erkennung/Seite ab, ganz ohne die Filter-Zeile. Unnötig,
; weil CurStepChanged(ssInstall) Agent/Config/UpdateService bereits bedingungslos per
; taskkill/sc.exe beendet, bevor [Files] etwas kopiert. RestartApplications ist damit
; ebenfalls gegenstandslos (nichts wird von Inno's Restart Manager mehr verwaltet) - unser
; eigener [Run]-Abschnitt startet Agent/Config bereits gezielt neu.
CloseApplications=no
#if InstallerPassword != ""
Password={#InstallerPassword}
#endif
