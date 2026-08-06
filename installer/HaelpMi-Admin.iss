; HälpMi installer (Inno Setup) - Admin-Installer (FR-35): schreibt Role=Admin in
; deployment.json (HaelpMiCommon.iss.inc/GetRoleValue) UND legt zusätzlich zwei
; eingehende Firewall-Regeln an (UDP {#DiscoveryUdpPort} für die Geräte-Erkennung, TCP
; {#AlarmTcpPort} für den Alarmkanal, siehe Pflichtenheft 6./8.) - der Admin-Rechner dient
; laut FR-35 zugleich als Testgerät, braucht die Regeln also eher als ein reines
; User-Gerät. Für ein echtes Verwaltungsnetzwerk ist GPO-Rollout weiterhin der eigentlich
; vorgesehene Weg für die Regeln auf User-Geräten - diese Variante ist der praktische
; Ersatz dafür auf einzelnen Testrechnern ohne GPO-Infrastruktur.
;
; Bewusst eine ZWEITE, separate Installer-Datei statt eines Assistenten-Schrittes im
; User-Installer: das Elevation-/Rollen-Bedürfnis ist damit schon am Dateinamen sichtbar,
; nicht erst mitten im Setup-Assistenten.
;
; Beide Varianten teilen AppId, Installationswurzel ({autopf}) und Registry-Hive (HKLM,
; siehe HaelpMiCommon.iss.inc) - den jeweils anderen Installer auf demselben Gerät später
; auszuführen wird von Inno als Update/Reparatur desselben Produkts erkannt (FR-28) und
; schreibt dabei deployment.json (nicht aber die bereits vorhandene settings.json) neu -
; das ist der unterstützte Weg, die Rolle eines bereits installierten Geräts nachträglich
; zu wechseln, kein Fehlerfall.
;
; Build/Payload identisch zu HaelpMi.iss - siehe dortige Kommentare bzw.
; BUILD-UND-INSTALLATION.md im Projekt-Root.

#define HaelpMiAdminInstall
#include "HaelpMiCommon.iss.inc"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName} (Admin-Installation)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\HaelpMi
DefaultGroupName=HälpMi
DisableProgramGroupPage=yes
OutputBaseFilename=HaelpMi-Setup-Admin-{#MyAppVersion}
OutputDir=Output
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
; Braucht Adminrechte/UAC-Prompt - ausschließlich für die Firewall-Regeln (siehe [Code]
; unten). Der Agent selbst läuft weiterhin ganz normal ohne Admin/SYSTEM (5.3/5.7).
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAgentExeName}
SetupIconFile=haelpmi_icon.ico
; Siehe HaelpMi.iss für die ausführliche Begründung - identisches Vorgehen hier.
CloseApplications=no
#if InstallerPassword != ""
Password={#InstallerPassword}
#endif
