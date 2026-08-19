; Shared between HaelpMi.iss (User-Installer, FR-35) and HaelpMi-Admin.iss (Admin-
; Installer - zusätzlich Firewall-Regeln, da der Admin-Rechner laut FR-35 zugleich als
; Testgerät dient). #include this near the top of each - ISPP just pastes it in place, so
; the #define values below are usable in that script's own [Setup] section.
;
; Same AppId in both variants on purpose: if someone installs one variant and later runs
; the other on the same machine, this is what lets the version-check below recognize it
; as the same product (FR-28) and offer a repair/update instead of a silent parallel
; install. Since Teil 2/FR-34 both variants now run PrivilegesRequired=admin (see
; HaelpMi.iss), so they also agree on install root ({autopf}) and registry hive (HKLM) -
; running the other variant afterwards is treated as an update of the same product.

#define MyAppName "HälpMi"
; Alpha: 0.x until release, per city/product decision - Version 1.0.0 is reserved for
; the actual release. Bump the third number for ordinary fixes, second for a feature.
#define MyAppVersion "0.16.3"
#define MyAppPublisher "HälpMi contributors"
#define MyAgentExeName "HaelpMi.Agent.exe"
#define MyConfigExeName "HaelpMi.Config.exe"
#define MyAppId "{{4B6C0A6E-6D9C-4A2E-8C7E-2E7B7E1A5F3D}"

; Must match HaelpMi.Core/Models/AppConstants.cs - there is no automatic sharing between
; the C# and Pascal Script toolchains, so these two constants are the single place in the
; installer scripts that needs updating if the app's ports ever change.
#define DiscoveryUdpPort 51500
#define AlarmTcpPort 51501

; FR-35/FR-49: die gemeinsame Kunden-/Gruppen-ID, die Admin- und User-Installer eines
; Kunden verknüpft. Wird vom (noch nicht existierenden) Install-Creator pro Kunde per
; ISCC-Kommandozeile gesetzt: "ISCC /DCustomerGroupId={guid} /DIsTestInstaller=false ...".
; Format wie ein .NET Guid ("D"-Format, 36 Zeichen, OHNE geschweifte Klammern) - genau wie
; bei GenerateDeviceId unten, da System.Text.Json keine andere Schreibweise akzeptiert.
; Ohne diese Angabe (z. B. beim manuellen Kompilieren zu Entwicklungszwecken) fällt das
; auf eine Nullen-ID + "Test" zurück - ein damit gebauter Installer taugt nur zum lokalen
; Testen, niemals für eine echte Kunden-Auslieferung (siehe deployment.json unten).
#ifndef CustomerGroupId
  #define CustomerGroupId "00000000-0000-0000-0000-000000000000"
#endif
#ifndef IsTestInstaller
  #define IsTestInstaller "true"
#endif

; Optionales Installer-Passwort (kein Schlüsselpaar - siehe CLAUDE.md Lizenz & Secrets,
; Punkt 3): schützt nur den Installer selbst vor versehentlichem/unbefugtem Ausführen,
; keine Kryptografie. Leer = kein Passwortschutz (siehe Password=-Zeile im jeweiligen
; [Setup]-Abschnitt); der Install-Creator setzt es per /DInstallerPassword=... nie zentral
; gespeichert, nur transient für diesen ISCC-Aufruf.
#ifndef InstallerPassword
  #define InstallerPassword ""
#endif

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Dirs]
; Teil 2, FR-34: maschinenweiter Speicherort statt %AppData% (siehe AppPaths.cs) - eine
; Geräte-ID/ein Raum gehört zum physischen Gerät, nicht zum jeweils angemeldeten Windows-
; Konto. "users-modify" vergibt Schreibrechte an die normale Users-Gruppe, weil die
; eigentliche App weiterhin rechtelos im User-Kontext läuft (CLAUDE.md) und dorthin
; schreiben können muss (Geräteliste, Config-Cache, Änderungshistorie, ...).
Name: "{commonappdata}\HaelpMi"; Permissions: users-modify

[Files]
; Payload is produced by the dotnet publish commands documented in BUILD-UND-INSTALLATION.md (project root).
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifdef HaelpMiAdminInstall
; Nutzerwunsch 04.08.2026: "eine Datei an den Sysadmin, jeder daraus exportierte User-
; Installer bekommt automatisch dieselbe Kunden-Gruppen-ID" - aber der Kundenrechner soll
; dafür nicht selbst mit ISCC neu kompilieren müssen. Bis Version 0.4.2 bettete der Admin-
; Installer dafür einen kompletten Bausatz ein (Nutzlast + Skripte + ~57 MB Inno-Compiler)
; und "User-Installer exportieren" rief ISCC live auf dem Kundenrechner auf - Quelle
; mehrerer Bugs (stdout/stderr, fehlende Icon-Datei im Bausatz, verwaiste ISCC-Prozesse)
; und unnötig groß. Der Install-Creator (MainWindow.xaml.cs) kompiliert den User-Installer
; jetzt VORAB, mit dem exakt gleichen CustomerGroupId/IsTestInstaller/Passwort wie diesen
; Admin-Installer, nach installer\UserInstallerPayload\ - hier wird nur noch die fertige,
; bereits vollständig gebaute Datei eingebettet. "User-Installer exportieren" im Admin-
; Dashboard ist damit eine reine Kopieroperation ohne ISCC-Aufruf auf dem Kundenrechner.
Source: "UserInstallerPayload\HaelpMi-User-Setup.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
; FR-4: Start Menu shortcut for the Konfigurationsprogramm - the Agent has no shortcut
; and no window of its own; it is reached only via its own autostart self-registration.
; User-Installer: reines Einstellungsfenster (eigener Raum/Hotkeys/Ton), kein Dashboard -
; Admin-Installer: derselbe Prozess, aber mit --open-dashboard direkt ins Dashboard, daher
; auch anders benannt, damit der Unterschied schon am Startmenü-Eintrag sichtbar ist.
#ifdef HaelpMiAdminInstall
Name: "{group}\HälpMi Dashboard"; Filename: "{app}\{#MyConfigExeName}"; Parameters: "--open-dashboard"
#else
Name: "{group}\HälpMi Konfiguration"; Filename: "{app}\{#MyConfigExeName}"
#endif
Name: "{group}\{cm:UninstallProgram,HälpMi}"; Filename: "{uninstallexe}"

[Run]
; Abschnitt 11: der Update-Dienst läuft als Windows-Dienst (LocalSystem statt nur
; LocalService, weil er Dateien unter {app}/Program Files verschieben und andere Prozesse
; beenden können muss - siehe HaelpMi.UpdateService/UpdateServiceWorker.cs). Stop+Delete
; vor Create, damit ein erneuter Installer-Lauf (Update/Reparatur) idempotent bleibt,
; statt an einem bereits existierenden Dienstnamen zu scheitern - gleiches Prinzip wie
; EnsureFirewallRules unten (löschen ohne Fehlerprüfung, dann neu anlegen).
; RunOnceId ist ausschließlich für [UninstallRun] gültig (ISCC lehnt es hier ab) - in
; [Run] führt Setup jeden Eintrag ohnehin genau einmal pro Installationslauf aus.
Filename: "sc.exe"; Parameters: "stop HaelpMiUpdateService"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete HaelpMiUpdateService"; Flags: runhidden
Filename: "sc.exe"; Parameters: "create HaelpMiUpdateService binPath= ""{app}\HaelpMi.UpdateService.exe"" start= auto obj= LocalSystem DisplayName= ""HälpMi Update-Dienst"""; Flags: runhidden
Filename: "sc.exe"; Parameters: "description HaelpMiUpdateService ""Installiert/testet/uebernimmt signierte HaelpMi-Programm-Updates (Abschnitt 11). Laeuft ausschliesslich dafuer mit erhoehten Rechten - die eigentliche App bleibt rechtelos."""; Flags: runhidden
Filename: "sc.exe"; Parameters: "start HaelpMiUpdateService"; Flags: runhidden

; Bugfix 11.08.2026 (Fehlerbericht "Autostart nicht eingerichtet", auf einem normalen
; Win11-Rechner reproduziert): der Agent-Start unten läuft mit Absicht "runasoriginaluser"
; (siehe dortiger Kommentar) - AutostartRegistrar.EnsureRegistered lief deshalb noch NIE
; elevated, obwohl DIESER Installer selbst gerade mit Adminrechten läuft. Ein Task mit
; Principal-GroupId (BUILTIN\Users, für "läuft für jede Anmeldung, nicht nur den
; installierenden Nutzer") lässt sich aber nur mit Adminrechten anlegen - ohne diese Zeile
; schlägt die Registrierung auf jedem Rechner fehl, auf dem der angemeldete Nutzer kein
; lokaler Windows-Admin ist (der von CLAUDE.md ausdrücklich vorgesehene Normalfall). Läuft
; OHNE runasoriginaluser (also elevated wie der Rest des Installers), VOR dem eigentlichen
; Agent-Start, und wartet auf dessen Ende, damit der Task-Eintrag garantiert schon steht,
; bevor der echte (de-elevated) Produktivprozess gleich danach seinerseits nur noch prüft
; statt neu anzulegen. Best-effort: schlägt auch das fehl (z. B. echte GPO-Sperre), bleibt
; die bestehende Selbstheilung im Agent-Start plus Tray-Meldung an den Admin als
; Rückfallebene - deshalb kein Abbruch der Installation bei einem Fehlschlag hier.
Filename: "{app}\{#MyAgentExeName}"; Parameters: "--register-autostart"; Flags: runhidden waituntilterminated

; Startet den Agent automatisch im Hintergrund - kein Postinstall-Haken, weil der Agent
; kein eigenes Fenster hat (ein anhakbarer "jetzt starten"-Punkt ohne sichtbaren Effekt
; sah für den Nutzer wie "macht nichts" aus). "runasoriginaluser" ist hier Pflicht, nicht
; kosmetisch: ohne dieses Flag erbt der Prozess das elevated Token des (per UAC erhöhten)
; Installers und liefe beim allerersten Start fälschlich mit Adminrechten, obwohl der
; Agent laut CLAUDE.md immer rechtelos im User-Kontext laufen soll - jeder spätere Start
; über den selbstregistrierten Autostart-Task läuft ohnehin schon unelevated, das hier
; nur die (bisher inkonsistente) Erstausführung. Der Autostart-Task selbst steht dank der
; Zeile oben davor schon fest, auch wenn der hier gestartete Prozess selbst nicht elevated ist.
Filename: "{app}\{#MyAgentExeName}"; Flags: nowait runasoriginaluser

; Postinstall-Haken mit sichtbarem Effekt statt des bisherigen "jetzt starten": beim User
; die eigene Konfiguration (Hotkeys/Empfängerkreise stehen dort), beim Admin direkt das
; Dashboard. Ebenfalls runasoriginaluser - dieselbe Elevation-Falle wie beim Agent oben.
#ifdef HaelpMiAdminInstall
Filename: "{app}\{#MyConfigExeName}"; Parameters: "--open-dashboard"; Description: "Dashboard öffnen"; Flags: nowait postinstall runasoriginaluser
#else
Filename: "{app}\{#MyConfigExeName}"; Description: "HälpMi Konfiguration öffnen - hier siehst du deine Hotkeys und wen sie im Alarmfall erreichen"; Flags: nowait postinstall runasoriginaluser
#endif

[UninstallRun]
; Abschnitt 11: der Update-Dienst wird von der App selbst installiert/gestartet (siehe
; [Run] oben), muss beim Deinstallieren aber genauso wieder verschwinden wie der
; Autostart-Task des Agents (Pflichtenheft 6. - keine Leichen im System zurücklassen).
Filename: "sc.exe"; Parameters: "stop HaelpMiUpdateService"; Flags: runhidden; RunOnceId: "HaelpMiStopUpdateServiceOnUninstall"
Filename: "sc.exe"; Parameters: "delete HaelpMiUpdateService"; Flags: runhidden; RunOnceId: "HaelpMiDeleteUpdateServiceOnUninstall"

#ifdef HaelpMiAdminInstall
[UninstallRun]
; Mirror of EnsureFirewallRules below, run in reverse on uninstall - a stale allow-rule
; for a program that's no longer installed is exactly the kind of leftover Pflichtenheft
; 6. warns about avoiding.
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""HälpMi Discovery"""; Flags: runhidden; RunOnceId: "HaelpMiRemoveDiscoveryRule"
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""HälpMi Alarm"""; Flags: runhidden; RunOnceId: "HaelpMiRemoveAlarmRule"
#endif

[Code]
// Compares two dotted version strings (e.g. "1.2.0" vs "1.10.0") numerically,
// component by component - a plain string compare would wrongly rank "1.10.0" before
// "1.2.0". Returns -1/0/1 like a normal Compare function.
function CompareDottedVersion(V1, V2: String): Integer;
var
  S1, S2: String;
  P1, P2: Integer;
  N1, N2: Integer;
begin
  Result := 0;
  S1 := V1;
  S2 := V2;
  while (Result = 0) and ((S1 <> '') or (S2 <> '')) do
  begin
    P1 := Pos('.', S1);
    if P1 = 0 then P1 := Length(S1) + 1;
    P2 := Pos('.', S2);
    if P2 = 0 then P2 := Length(S2) + 1;

    N1 := StrToIntDef(Copy(S1, 1, P1 - 1), 0);
    N2 := StrToIntDef(Copy(S2, 1, P2 - 1), 0);

    if N1 < N2 then
      Result := -1
    else if N1 > N2 then
      Result := 1;

    S1 := Copy(S1, P1 + 1, Length(S1));
    S2 := Copy(S2, P2 + 1, Length(S2));
  end;
end;

// Inno auto-writes this key for every install; reading it back is how we detect "already
// installed" and which version (FR-28), without a separate custom version file/registry
// entry. HKCU is also checked for installs done by an old pre-Teil-2 PrivilegesRequired=
// lowest build of this installer (see top-of-file note) - a fresh install always uses HKLM.
function GetInstalledVersion(): String;
var
  Value: String;
  KeyPath: String;
begin
  KeyPath := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  if not RegQueryStringValue(HKLM, KeyPath, 'DisplayVersion', Value) then
    if not RegQueryStringValue(HKCU, KeyPath, 'DisplayVersion', Value) then
      Value := '';
  Result := Value;
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
  Comparison: Integer;
begin
  Result := True;
  InstalledVersion := GetInstalledVersion();

  if InstalledVersion <> '' then
  begin
    Comparison := CompareDottedVersion(InstalledVersion, '{#MyAppVersion}');
    if Comparison = 0 then
    begin
      // Same version re-run ("just execute the installer again"): treat as a repair,
      // not an error - CurStepChanged still stops the Agent first and [Files]'s
      // ignoreversion still overwrites everything, so this is a safe no-op if nothing
      // actually changed. Only ask when there's an interactive user to ask; a silent/
      // scripted re-run (WizardSilent) must never block forever waiting for a click.
      if not WizardSilent() then
      begin
        if MsgBox('HälpMi ' + InstalledVersion + ' ist bereits installiert. Erneut installieren (Reparatur)?',
                  mbConfirmation, MB_YESNO) = IDNO then
          Result := False;
      end;
    end
    else if Comparison > 0 then
    begin
      // Downgrade: always blocked, silent or not - unlike the repair case above, there is
      // no safe default to fall back to here, so silent mode fails closed instead of
      // guessing (FR-28 exists specifically to prevent this).
      if not WizardSilent() then
        MsgBox('Eine neuere Version von HälpMi (' + InstalledVersion + ') ist bereits installiert. ' +
               'Dieser Installer (Version {#MyAppVersion}) würde ein Downgrade durchführen und wird daher abgebrochen.',
               mbError, MB_OK);
      Result := False;
    end;
    // Comparison < 0: an older version is installed - this is a genuine update, proceed
    // without asking (FR-28: "bietet Update statt Neuinstallation an").
  end;

  // FR-37: Raumbezeichnung/Raumnummer sind Pflichtangaben bei der Ersteinrichtung (siehe
  // RoomPage unten) - die kann ein unbeaufsichtigter/silent-Lauf naturgemäß nicht
  // interaktiv beantworten. Bei einem Update/einer Reparatur (settings.json existiert
  // bereits) wird die Seite ohnehin übersprungen (ShouldSkipPage) und silent bleibt erlaubt.
  if Result and WizardSilent() and not FileExists(ExpandConstant('{commonappdata}\HaelpMi\settings.json')) then
  begin
    MsgBox('Diese Ersteinrichtung benötigt zwingend Raumbezeichnung und Raumnummer und kann ' +
           'daher nicht unbeaufsichtigt (silent) durchgeführt werden.', mbError, MB_OK);
    Result := False;
  end;
end;

// --- FR-3/FR-36 bis FR-39: Geräte-ID + Raum/Raumnummer bei der Ersteinrichtung erfassen ---
// Ersetzt das frühere App-eigene Ersteinrichtungsfenster (Version 1) vollständig: die
// Angaben werden ein einziges Mal hier erfasst und direkt als settings.json ins
// maschinenweite ProgramData-Verzeichnis geschrieben (siehe [Dirs] oben), sodass die App
// beim allerersten Start bereits vollständige, gültige Einstellungen vorfindet -
// SettingsStore.Load() bricht ohne diese Datei bewusst mit einer klaren Fehlermeldung ab,
// statt mit leeren/erfundenen Werten weiterzulaufen (siehe HaelpMi.Core/Storage/SettingsStore.cs).
//
// WICHTIG (Update/Reparatur): läuft dieser Installer erneut auf einem Gerät, das schon
// eine settings.json hat, wird die Raum-Seite übersprungen und nichts überschrieben - die
// Geräte-ID darf sich über ein Update hinweg nie ändern (FR-3, Pflichtenheft 6.: "Risiko
// einer versehentlichen Neu-Erzeugung der ID").
var
  RoomPage: TInputQueryWizardPage;

function CoCreateGuid(var Guid: TGUID): Integer;
  external 'CoCreateGuid@ole32.dll stdcall';
function StringFromGUID2(Guid: TGUID; Buffer: String; CchMax: Integer): Integer;
  external 'StringFromGUID2@ole32.dll stdcall';

// Generiert die dauerhafte Geräte-ID (FR-3) - .NET erwartet für Guid-Felder ausschließlich
// das reine "D"-Format (36 Zeichen, ohne geschweifte Klammern); StringFromGUID2 liefert
// das Windows-übliche Format inklusive Klammern und Null-Terminator im Puffer, beides wird
// hier entfernt.
function IsHexChar(C: Char): Boolean;
begin
  Result := ((C >= '0') and (C <= '9')) or ((C >= 'A') and (C <= 'F')) or ((C >= 'a') and (C <= 'f'));
end;

// Verifiziert das strikte "D"-Format (8-4-4-4-12 Hex-Ziffern), das System.Text.Json beim
// Einlesen von settings.json als Guid-Feld verlangt - entdeckt am 04.08.2026: CoCreateGuid/
// StringFromGUID2 werfen bei Fehlschlag keine Pascal-Exception (Rückgabewerte wurden bisher
// ignoriert), auf mindestens einem realen Testrechner landete dadurch ein technisch 36
// Zeichen langer, aber nicht-hexadezimaler Wert in settings.json - App-seitig nur als
// kryptischer JsonException-Absturz beim allerersten Start sichtbar (siehe HaelpMi.Agent/
// HaelpMi.Config App.xaml.cs, dortiger Kommentar zum selben Vorfall).
function IsValidGuidFormat(const S: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  if Length(S) <> 36 then
    Exit;
  for I := 1 to 36 do
  begin
    if (I = 9) or (I = 14) or (I = 19) or (I = 24) then
    begin
      if S[I] <> '-' then Exit;
    end
    else if not IsHexChar(S[I]) then
      Exit;
  end;
  Result := True;
end;

// Reiner Pascal-Fallback (kein externer DLL-Aufruf) - schwächer zufällig als ein echtes
// COM-GUID, aber für eine lokale Geräte-Kennung ausreichend eindeutig und immer syntaktisch
// gültig. Nur der Notanker, falls der Win32-Weg unten wiederholt fehlschlägt.
function GenerateFallbackDeviceId(): String;
var
  HexDigits: String;
  S: String;
  I: Integer;
begin
  HexDigits := '0123456789abcdef';
  S := '';
  for I := 1 to 32 do
    S := S + HexDigits[Random(16) + 1];
  Result := Copy(S, 1, 8) + '-' + Copy(S, 9, 4) + '-' + Copy(S, 13, 4) + '-' + Copy(S, 17, 4) + '-' + Copy(S, 21, 12);
end;

function GenerateDeviceId(): String;
var
  Guid: TGUID;
  Buffer: String;
  Attempt: Integer;
begin
  for Attempt := 1 to 3 do
  begin
    CoCreateGuid(Guid);
    SetLength(Buffer, 40);
    StringFromGUID2(Guid, Buffer, Length(Buffer));
    StringChangeEx(Buffer, #0, '', True);
    Buffer := Trim(Buffer);
    Buffer := Copy(Buffer, 2, Length(Buffer) - 2); // äußere { } entfernen
    if IsValidGuidFormat(Buffer) then
    begin
      Result := Buffer;
      Exit;
    end;
  end;

  // Win32-Weg dreimal in Folge ungültig - lieber ein technisch gültiges, schwächer
  // zufälliges Fallback-Format schreiben als eine kaputte settings.json, an der App und
  // Dashboard erst beim allerersten Start unsichtbar scheitern (siehe Kommentar oben).
  Result := GenerateFallbackDeviceId();
end;

// Raumbezeichnung ist bewusst ein freies Eingabefeld (siehe RoomPage.Add unten) und kann
// daher Anführungszeichen/Backslashes enthalten, die sonst das JSON-Dokument zerstören
// würden. Escaped zusätzlich Steuerzeichen (CR/LF/Tab) - unescaped im JSON-String-Literal
// sind sie laut Spec ungültig; ein per Copy-Paste eingefügter Zeilenumbruch in der
// Raumbezeichnung führte genau dazu ("Installation unvollständig", 04.08.2026 per
// Fehlerbericht entdeckt: "$.RoomName ... could not be converted to System.String").
function JsonEscape(const S: String): String;
var
  R: String;
begin
  R := S;
  StringChangeEx(R, '\', '\\', True);
  StringChangeEx(R, '"', '\"', True);
  StringChangeEx(R, #13, '\r', True);
  StringChangeEx(R, #10, '\n', True);
  StringChangeEx(R, #9, '\t', True);
  Result := R;
end;

// SaveStringToFile schreibt in der System-ANSI-Codepage - jeder Umlaut in Raumname/
// Computername (z. B. "Büro") wäre dann als Einzelbyte statt als gültiges UTF-8
// geschrieben worden, was System.Text.Json (erwartet strikt UTF-8) zum Scheitern brachte
// ("$.RoomName ... could not be converted to System.String", 04.08.2026 per Fehlerbericht
// entdeckt). Es gibt kein SaveStringToUTF8File (Singular) - nur die Plural-Variante für ein
// TArrayOfString, hier mit einer einzigen Zeile (das gesamte JSON-Dokument als ein String).
procedure SaveJsonUtf8(const FileName, Json: String);
var
  Lines: TArrayOfString;
begin
  SetArrayLength(Lines, 1);
  Lines[0] := Json;
  SaveStringsToUTF8FileWithoutBOM(FileName, Lines, False);
end;

// Models.Role: User = 0, Admin = 1 (System.Text.Json serialisiert Enums standardmäßig als
// Zahl, siehe HaelpMi.Core/Storage/JsonFileStore.cs). HaelpMiAdminInstall unterscheidet
// HaelpMi.iss (User-Installer) von HaelpMi-Admin.iss (Admin-Installer, siehe FR-35) -
// beide teilen sich dieses .inc, das #define ist bereits das, was die zwei Installer-
// Typen im Sinne von FR-33/35 tatsächlich unterscheidet.
function GetRoleValue(): String;
begin
#ifdef HaelpMiAdminInstall
  Result := '1';
#else
  Result := '0';
#endif
end;

procedure WriteSettingsJson();
var
  DeviceId, ComputerName, RoomName, RoomNumber, Json, SettingsDir: String;
begin
  DeviceId := GenerateDeviceId();
  ComputerName := ExpandConstant('{computername}'); // FR-36: automatisch aus Windows, nicht editierbar
  RoomName := RoomPage.Values[0];
  RoomNumber := RoomPage.Values[1];

  // Role/CustomerGroupId sind hier nur eine lokale Cache-Kopie (siehe OwnSettings.cs-
  // Kommentar) - verbindlich ist ausschließlich deployment.json (WriteDeploymentJson).
  Json :=
    '{' + #13#10 +
    '  "DeviceId": "' + DeviceId + '",' + #13#10 +
    '  "ComputerName": "' + JsonEscape(ComputerName) + '",' + #13#10 +
    '  "RoomName": "' + JsonEscape(RoomName) + '",' + #13#10 +
    '  "RoomNumber": "' + JsonEscape(RoomNumber) + '",' + #13#10 +
    '  "IncomingSoundId": "ton-1",' + #13#10 +
    '  "Role": ' + GetRoleValue() + ',' + #13#10 +
    '  "CustomerGroupId": "{#CustomerGroupId}",' + #13#10 +
    '  "AppliedConfigVersion": 0,' + #13#10 +
    '  "FirstRunCompleted": true' + #13#10 +
    '}';

  SettingsDir := ExpandConstant('{commonappdata}\HaelpMi');
  ForceDirectories(SettingsDir);
  SaveJsonUtf8(SettingsDir + '\settings.json', Json);
end;

// FR-35/FR-49: die verbindliche Beschreibung DIESES Installer-Builds - anders als
// settings.json (einmalig, geräteindividuell) sind diese Werte compile-time-konstant für
// jeden Installer, den der Install-Creator für einen Kunden erzeugt, und werden daher bei
// JEDEM Lauf (auch Update/Reparatur) neu geschrieben, nicht nur beim ersten Install.
// HaelpMi.Core/Storage/DeploymentInfoStore.Load() bricht ohne diese Datei bewusst ab.
procedure WriteDeploymentJson();
var
  Json: String;
begin
  Json :=
    '{' + #13#10 +
    '  "CustomerGroupId": "{#CustomerGroupId}",' + #13#10 +
    '  "Role": ' + GetRoleValue() + ',' + #13#10 +
    '  "IsTestInstaller": {#IsTestInstaller}' + #13#10 +
    '}';

  // Aktuell nur ASCII-Inhalte (GUID/Zahlen), aber aus Konsistenz mit WriteSettingsJson
  // ebenfalls UTF-8 statt System-ANSI-Codepage - siehe Kommentar dort.
  SaveJsonUtf8(ExpandConstant('{app}\deployment.json'), Json);
end;

procedure InitializeWizard();
begin
  RoomPage := CreateInputQueryPage(wpSelectDir,
    'Raum-Zuordnung', 'Diesem Gerät einen Raum zuordnen',
    'Raumbezeichnung und Raumnummer sind Pflichtangaben (FR-37) - sie werden einmalig bei ' +
    'dieser Installation erfasst. Später lässt sich die Zuordnung nur noch zentral über das ' +
    'Admin-Dashboard ändern.');
  RoomPage.Add('Raumbezeichnung:', False);
  RoomPage.Add('Raumnummer:', False);
end;

// Bei Update/Reparatur (settings.json existiert schon) nie erneut fragen - siehe Kommentar
// oben bei WriteSettingsJson.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = RoomPage.ID then
    Result := FileExists(ExpandConstant('{commonappdata}\HaelpMi\settings.json'));
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = RoomPage.ID then
  begin
    if Trim(RoomPage.Values[0]) = '' then
    begin
      MsgBox('Bitte eine Raumbezeichnung eingeben (Pflichtfeld).', mbError, MB_OK);
      Result := False;
    end
    else if Trim(RoomPage.Values[1]) = '' then
    begin
      MsgBox('Bitte eine Raumnummer eingeben (Pflichtfeld).', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

#ifdef HaelpMiAdminInstall
// Only reachable with admin rights (PrivilegesRequired=admin above) - this is the one
// piece of installer behavior that genuinely needs elevation; the app itself never does
// (Pflichtenheft: kein Admin). Delete-then-add rather than a "does it exist" check first,
// since netsh's exit code for "rule not found" isn't consistent enough across Windows
// versions to branch on reliably - this way is idempotent regardless.
procedure EnsureFirewallRules();
var
  ResultCode: Integer;
begin
  Exec('netsh.exe', 'advfirewall firewall delete rule name="HälpMi Discovery"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('netsh.exe', 'advfirewall firewall add rule name="HälpMi Discovery" dir=in action=allow protocol=UDP localport={#DiscoveryUdpPort}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec('netsh.exe', 'advfirewall firewall delete rule name="HälpMi Alarm"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('netsh.exe', 'advfirewall firewall add rule name="HälpMi Alarm" dir=in action=allow protocol=TCP localport={#AlarmTcpPort}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
#endif

// Update must stop the running Agent before overwriting its files, and the [Run]
// section above starts it again afterwards (Pflichtenheft 5.8). The device list lives
// under %ProgramData%\HaelpMi, entirely outside {app}, so nothing here can touch it -
// that is what actually keeps it intact across an update, not any special-case code.
//
// Entdeckt 04.08.2026: Agent war hier nicht das einzige, was seine eigenen Dateien
// gesperrt hält. HaelpMi.Config.exe (falls das Dashboard/Konfigurationsfenster gerade
// offen ist) und der Windows-Dienst HaelpMiUpdateService (läuft immer, start=auto)
// halten ihre jeweiligen Dateien - inklusive gemeinsam genutzter DLLs wie
// PresentationNative_cor3.dll - ebenfalls gesperrt, solange sie laufen. Ohne das hier
// zeigt [Files] beim Kopieren einen "Datei kann nicht ersetzt werden"-Dialog, den ein
// Installer nie zeigen sollte - alles, was die eigenen Dateien sperren könnte, muss VOR
// [Files] beendet sein, nicht erst im [Run]-Abschnitt danach (der Dienst-Stop dort war zu
// spät, [Files] war zu dem Zeitpunkt schon gelaufen).
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec('taskkill.exe', '/IM {#MyAgentExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/IM {#MyConfigExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'stop HaelpMiUpdateService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // sc.exe stop kehrt zurück, sobald der Stop-Befehl an den SCM übergeben ist - nicht
    // erst, wenn der Dienstprozess seine Dateien tatsächlich freigegeben hat. Gleiches
    // Muster wie in UpdateServiceWorker.ConfirmSwapAsync ("Windows gibt Datei-Handles nach
    // Prozessende nicht immer sofort frei").
    Sleep(500);
  end;

  if CurStep = ssPostInstall then
  begin
    if not FileExists(ExpandConstant('{commonappdata}\HaelpMi\settings.json')) then
      WriteSettingsJson();

    // Compile-time-konstant für diesen Installer-Build - anders als settings.json bei
    // JEDEM Lauf neu geschrieben (siehe Kommentar bei WriteDeploymentJson).
    WriteDeploymentJson();

#ifdef HaelpMiAdminInstall
    EnsureFirewallRules();
#endif
  end;
end;

// --- Uninstall: remove every trace, not just {app} ---------------------------------
// Inno only ever auto-removes {app}, the Start Menu group and the uninstall registry
// key by itself. Three things it does NOT know about because they were created by the
// *app*, not by Setup, and would otherwise be left behind forever:
//   1. the running Agent process (would block deleting its own .exe/.dll files)
//   2. the self-registered "bei Anmeldung" Task-Scheduler entry (5.7)
//   3. %ProgramData%\HaelpMi (settings/devices/logs, maschinenweit seit Teil 2/FR-34) -
//      real (device) data, so asked about explicitly rather than silently wiped, except
//      in a silent uninstall where there is no one to ask and "remove everything" is the
//      only sensible default.
var
  RemoveUserDataOnUninstall: Boolean;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if UninstallSilent() then
    RemoveUserDataOnUninstall := True
  else
    RemoveUserDataOnUninstall := (MsgBox(
      'Sollen auch die lokal gespeicherten Einstellungen und die Geräteliste entfernt werden?' + #13#10 +
      '(' + ExpandConstant('{commonappdata}') + '\HaelpMi)' + #13#10#13#10 +
      'Bei "Nein" bleiben sie erhalten - z. B. für eine spätere Neuinstallation auf diesem Rechner.',
      mbConfirmation, MB_YESNO) = IDYES);
end;

// Untersuchung 09.08.2026 (Fehlerbericht "im Hintergrund läuft nach dem Deinstallieren
// immer noch eine Alarmmeldung"): Codeanalyse fand ein passendes Risiko in
// HaelpMi.UpdateService/UpdateServiceWorker.cs - Testinstanzen des Agents aus
// StartTest() (--update-test-port, volle AlarmFlowCoordinator-Logik, gestartet unter
// {app}\versions\<Version>\) werden nur in einem In-Memory-Dictionary verfolgt und laufen
// als Waise unbegrenzt weiter, falls der Update-Dienst währenddessen neu startet (siehe
// Kommentar dort - jetzt mit eigener Aufräumroutine beim Dienststart gehärtet). Auf der
// Entwicklungsmaschine ließ sich der Vorfall nicht selbst nachstellen (HälpMi war dort
// gar nicht installiert) - dieses Log soll beim nächsten Auftreten harte Beweise statt
// erneuter Vermutung liefern, bevor der Uninstaller die Spuren beseitigt. Bewusst NICHT
// unter {commonappdata}\HaelpMi (siehe RemoveUserDataOnUninstall unten) - genau der Fall
// "Nutzer entfernt auch die lokalen Daten" darf die Diagnose nicht mit wegwerfen.
procedure LogUninstallDiagnostics(const Line: String);
begin
  SaveStringToFile(
    ExpandConstant('{commonappdata}') + '\HaelpMi-uninstall-diagnostics.log',
    GetDateTimeString('yyyy-mm-dd hh:nn:ss', #0, #0) + '  ' + Line + #13#10,
    True);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  LogPath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    LogPath := ExpandConstant('{commonappdata}') + '\HaelpMi-uninstall-diagnostics.log';
    LogUninstallDiagnostics('=== HaelpMi {#MyAppVersion} Uninstall gestartet ===');
    // Momentaufnahme VOR dem Aufräumen unten - genau das, was ein Support-Fall sonst nie
    // sieht, weil taskkill/DelTree es gleich darauf beseitigen. /V (tasklist) bzw. /FO LIST
    // (schtasks) liefern den vollen Bildpfad mit - der einzige Weg, um im Log zwischen einer
    // regulären Instanz ({app}\...) und einer herrenlosen Testinstanz ({app}\versions\...,
    // siehe Kommentar oben) zu unterscheiden.
    Exec('cmd.exe', '/C tasklist /FI "IMAGENAME eq {#MyAgentExeName}" /V >> "' + LogPath + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('cmd.exe', '/C tasklist /FI "IMAGENAME eq {#MyConfigExeName}" /V >> "' + LogPath + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('cmd.exe', '/C schtasks /Query /TN "HaelpMi Agent" /V /FO LIST >> "' + LogPath + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('cmd.exe', '/C sc query HaelpMiUpdateService >> "' + LogPath + '" 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if DirExists(ExpandConstant('{app}\versions')) then
      LogUninstallDiagnostics('WARNUNG: {app}\versions existiert noch - deutet auf ein unterbrochenes Update hin (siehe UpdateServiceWorker-Kommentar).');
    if DirExists(ExpandConstant('{app}\_previous')) then
      LogUninstallDiagnostics('WARNUNG: {app}\_previous existiert noch - deutet auf einen unterbrochenen Swap hin.');

    // Bugfix 06.08.2026 ("Uninstall entfernt Programm-Ordner nicht", Fehlerbericht): hier
    // stand bisher NUR der Agent-Kill - HaelpMi.Config.exe (Dashboard/Konfigurationsfenster,
    // z. B. offen gelassen über das Tray-Menü "HälpMi öffnen") wurde nie beendet. Solange
    // dieser Prozess läuft, hält er seine eigenen Dateien UND gemeinsam genutzte DLLs
    // (HaelpMi.Core.dll, HaelpMi.UI.dll, PresentationNative_cor3.dll, ...) gesperrt - Inno
    // überspringt gesperrte Dateien beim automatischen {app}-Aufräumen kommentarlos, wodurch
    // der ganze Ordner nicht leer und damit nicht entfernbar wird. Gleiches Muster wie
    // CurStepChanged oben bei der Installation, hier nur bisher vergessen.
    Exec('taskkill.exe', '/IM {#MyConfigExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Stop the Agent first - otherwise its own files can be locked and skipped/
    // deferred-to-reboot instead of actually being removed. taskkill /IM matcht nach reinem
    // Bilddateinamen, unabhängig vom Pfad - trifft also auch eine herrenlose Testinstanz
    // unter {app}\versions\... (siehe Log-Ausgabe oben), sofern sie überhaupt noch läuft.
    Exec('taskkill.exe', '/IM {#MyAgentExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Task name must match AutostartRegistrar.TaskName in HaelpMi.Core exactly ("HaelpMi
    // Agent", plain ASCII spelling - not "HälpMi") or this silently deletes nothing.
    Exec('schtasks.exe', '/Delete /TN "HaelpMi Agent" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Handles werden nach Prozessende nicht immer sofort freigegeben (gleiches Prinzip wie
    // CurStepChanged oben und UpdateServiceWorker.ConfirmSwapAsync) - kurz warten, bevor
    // Inno gleich im Anschluss versucht, die Dateien unter {app} zu löschen.
    Sleep(500);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    // Bugfix 06.08.2026 (Teil 2 desselben Fehlerberichts): {app} enthält nicht nur die von
    // [Files] installierten Dateien, sondern auch zur Laufzeit vom Update-Dienst selbst
    // angelegte Versions-/Swap-Ordner (siehe HaelpMi.UpdateService/UpdateServiceWorker.cs,
    // VersionDir/AppRoot = AppContext.BaseDirectory = {app}) - die kennt Inno's automatische
    // Deinstallation gar nicht, weil Setup sie nie selbst dorthin kopiert hat. Ein "leer,
    // also lösche den Ordner"-Automatismus greift dann nicht, selbst wenn alle gesperrten
    // Dateien oben schon frei sind. Deshalb hier zusätzlich ein expliziter, bedingungsloser
    // DelTree - {app} enthält ausschließlich Programmdateien, keine Nutzerdaten (die liegen
    // separat unter {commonappdata}\HaelpMi, siehe RemoveUserDataOnUninstall unten), daher
    // ohne Rückfrage sicher.
    DelTree(ExpandConstant('{app}'), True, True, True);

    if DirExists(ExpandConstant('{app}')) then
      LogUninstallDiagnostics('WARNUNG: {app} konnte nicht vollstaendig entfernt werden (vermutlich noch eine gesperrte Datei) - siehe tasklist-Ausgabe oben.');

    if RemoveUserDataOnUninstall then
      DelTree(ExpandConstant('{commonappdata}\HaelpMi'), True, True, True);

    LogUninstallDiagnostics('=== HaelpMi {#MyAppVersion} Uninstall abgeschlossen ===');
  end;
end;
