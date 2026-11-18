#Requires -Version 7.0
#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Richtet die Windows-Vorlage der E2E-Test-VMs ein und entschlackt sie sanft.

.DESCRIPTION
    Idempotent: jeder Schritt prüft zuerst, ob er schon erfüllt ist. Mit -Pruefen wird
    nichts geändert, nur Abweichungen gemeldet (Dry-Run und Monatspflege).

    Benutzerbezogene Einstellungen landen im Default-Profil (gilt für jedes neu angelegte
    Profil, also auch für den Testnutzer bei seiner ersten Anmeldung) und, falls schon
    vorhanden, direkt im Profil des Testnutzers.

    Bewusst nicht angefasst: Windows Audio, Aufgabenplanung, Firewall-Dienst, Windows
    Installer, Defender (nur eine Ausnahme), UAC (bleibt an), RDP.

.PARAMETER Pruefen
    Nur prüfen, nichts ändern. Exit-Code 1 bei Abweichungen.

.PARAMETER TestNutzer
    Lokales Konto, unter dem Autologon und Runner laufen.

.PARAMETER SshSchluessel
    Öffentliche SSH-Schlüssel (je eine Zeile "ssh-ed25519 AAAA… kommentar"), die in
    administrators_authorized_keys stehen müssen. Vorhandene Einträge bleiben erhalten.

.PARAMETER SshErlaubteAdressen
    IPv4-Adressen, von denen eingehendes SSH erlaubt ist. Ohne Angabe bleibt die
    Firewall-Regel unverändert - erst setzen, wenn alle Gegenstellen feststehen, sonst
    sperrt man sich aus.

.EXAMPLE
    pwsh -File vorlage-einrichten.ps1 -Pruefen
#>
[CmdletBinding()]
param(
    [switch]$Pruefen,
    [string]$TestNutzer = 'hmtest',
    [string[]]$SshSchluessel = @(),
    [string[]]$SshErlaubteAdressen = @()
)

$ErrorActionPreference = 'Stop'
# Über SSH gibt die Konsole sonst in der OEM-Codepage aus, Umlaute kämen verstümmelt an.
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$schritte = [System.Collections.Generic.List[object]]::new()

# Ist: liefert $true, wenn erfüllt. Setze: $null = nur von Hand möglich, dann steht im Hinweis wie.
function Schritt([string]$Name, [scriptblock]$Ist, [scriptblock]$Setze, [string]$Hinweis = '') {
    $schritte.Add([pscustomobject]@{ Name = $Name; Ist = $Ist; Setze = $Setze; Hinweis = $Hinweis })
}

# Closures per GetNewClosure sehen keine Skript-Funktionen, nur Cmdlets und eingefangene Variablen.
function Reg([string]$Pfad, [string]$Name, $Wert, [string]$Typ = 'DWord') {
    $ist = {
        $v = Get-ItemProperty -Path $Pfad -Name $Name -ErrorAction SilentlyContinue
        $null -ne $v -and "$($v.$Name)" -eq "$Wert"
    }.GetNewClosure()
    $setze = {
        if (-not (Test-Path $Pfad)) { New-Item -Path $Pfad -Force | Out-Null }
        New-ItemProperty -Path $Pfad -Name $Name -Value $Wert -PropertyType $Typ -Force | Out-Null
    }.GetNewClosure()
    Schritt "$($Pfad -replace '^Registry::HKEY_USERS\\', 'HKU:\') : $Name = $Wert" $ist $setze
}

function RegEntfernt([string]$Pfad, [string]$Name) {
    $ist = { $null -eq (Get-ItemProperty -Path $Pfad -Name $Name -ErrorAction SilentlyContinue) }.GetNewClosure()
    $setze = { Remove-ItemProperty -Path $Pfad -Name $Name }.GetNewClosure()
    Schritt "$($Pfad -replace '^Registry::HKEY_USERS\\', 'HKU:\') : $Name entfernt" $ist $setze
}

# --- Benutzer-Hives einhängen -------------------------------------------------------------

$geladeneHives = [System.Collections.Generic.List[string]]::new()
$nutzerRoots = [System.Collections.Generic.List[string]]::new()

function Haenge-Hive([string]$Kurzname, [string]$Datei) {
    & reg.exe load "HKU\$Kurzname" $Datei | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Hive $Datei ließ sich nicht laden" }
    $geladeneHives.Add($Kurzname)
    $nutzerRoots.Add("Registry::HKEY_USERS\$Kurzname")
}

# --- Ablauf -------------------------------------------------------------------------------

try {
    Haenge-Hive 'HM_Default' 'C:\Users\Default\NTUSER.DAT'

    $nutzer = Get-LocalUser -Name $TestNutzer -ErrorAction SilentlyContinue
    if ($nutzer) {
        $sid = $nutzer.SID.Value
        if (Test-Path "Registry::HKEY_USERS\$sid") {
            $nutzerRoots.Add("Registry::HKEY_USERS\$sid")
        } else {
            $profil = Get-CimInstance Win32_UserProfile -Filter "SID='$sid'"
            if ($profil) { Haenge-Hive 'HM_Testnutzer' (Join-Path $profil.LocalPath 'NTUSER.DAT') }
        }
    }

    # Appx läuft unter PowerShell 7 nur über die Windows-PowerShell-Kompatibilität zuverlässig.
    Import-Module Appx -UseWindowsPowerShell -Global -WarningAction SilentlyContinue

    $hklmPol = 'HKLM:\SOFTWARE\Policies\Microsoft'
    $sysPol = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'

    # Energie
    $hoechstleistung = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
    Schritt 'Energieplan Höchstleistung aktiv' `
        { (powercfg /getactivescheme) -match $hoechstleistung }.GetNewClosure() `
        { powercfg /duplicatescheme $hoechstleistung $hoechstleistung 2>$null | Out-Null
          powercfg /setactive $hoechstleistung }.GetNewClosure()

    foreach ($e in @(
        @{ N = 'Bildschirm aus'; Sub = 'SUB_VIDEO'; Set = 'VIDEOIDLE' }
        @{ N = 'Energiesparen';  Sub = 'SUB_SLEEP'; Set = 'STANDBYIDLE' }
        @{ N = 'Ruhezustand';    Sub = 'SUB_SLEEP'; Set = 'HIBERNATEIDLE' }
        @{ N = 'Festplatte aus'; Sub = 'SUB_DISK';  Set = 'DISKIDLE' })) {
        $sub = $e.Sub; $set = $e.Set
        # Die letzten beiden 0x-Zeilen von powercfg /query sind Netz- und Akkuwert (sprachunabhängig).
        Schritt "$($e.N): nie" `
            { $z = @(powercfg /query SCHEME_CURRENT $sub $set | Select-String ':\s*0x[0-9a-fA-F]{8}\s*$')
              $z.Count -ge 2 -and
              @($z[-2..-1] | Where-Object { [Convert]::ToInt32(($_.Line -split '0x')[-1].Trim(), 16) -ne 0 }).Count -eq 0 }.GetNewClosure() `
            { powercfg /setacvalueindex SCHEME_CURRENT $sub $set 0
              powercfg /setdcvalueindex SCHEME_CURRENT $sub $set 0
              powercfg /setactive SCHEME_CURRENT }.GetNewClosure()
    }

    Schritt 'Ruhezustand-Datei aus' `
        { (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power').HibernateEnabled -eq 0 } `
        { powercfg /hibernate off }

    # Sperrbildschirm, UAC, Erstanmeldung
    Reg "$hklmPol\Windows\Personalization" 'NoLockScreen' 1
    Reg $sysPol 'EnableLUA' 1
    Reg $sysPol 'ConsentPromptBehaviorAdmin' 0
    Reg $sysPol 'EnableFirstLogonAnimation' 0

    # SmartScreen
    Reg "$hklmPol\Windows\System" 'EnableSmartScreen' 0
    Reg 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' 'SmartScreenEnabled' 'Off' 'String'

    # Windows Update nur von Hand, keine automatischen Neustarts
    Reg "$hklmPol\Windows\WindowsUpdate\AU" 'NoAutoUpdate' 1
    Reg "$hklmPol\Windows\WindowsUpdate\AU" 'NoAutoRebootWithLoggedOnUsers' 1

    # Edge, OneDrive, Tipps, Widgets, Copilot, Hintergrund-Apps
    Reg "$hklmPol\Edge" 'HideFirstRunExperience' 1
    Reg "$hklmPol\Windows\OneDrive" 'DisableFileSyncNGSC' 1
    Reg "$hklmPol\Windows\CloudContent" 'DisableWindowsConsumerFeatures' 1
    Reg "$hklmPol\Windows\CloudContent" 'DisableSoftLanding' 1
    Reg "$hklmPol\Windows\CloudContent" 'DisableCloudOptimizedContent' 1
    Reg "$hklmPol\Windows\WindowsCopilot" 'TurnOffWindowsCopilot' 1
    Reg "$hklmPol\Windows\AppPrivacy" 'LetAppsRunInBackground' 2

    foreach ($root in $nutzerRoots) {
        Reg "$root\Control Panel\Desktop" 'ScreenSaveActive' '0' 'String'
        Reg "$root\Control Panel\Desktop" 'ScreenSaverIsSecure' '0' 'String'
        # 96 dpi = 100 % Skalierung
        Reg "$root\Control Panel\Desktop" 'LogPixels' 96
        Reg "$root\Control Panel\Desktop" 'Win8DpiScaling' 1
        Reg "$root\Software\Microsoft\Windows\CurrentVersion\PushNotifications" 'ToastEnabled' 0
        Reg "$root\Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications" 'NoToastApplicationNotification' 1
        Reg "$root\Software\Policies\Microsoft\Windows\WindowsCopilot" 'TurnOffWindowsCopilot' 1
        $cdm = "$root\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
        foreach ($n in 'SubscribedContent-338389Enabled', 'SubscribedContent-310093Enabled',
                       'SystemPaneSuggestionsEnabled', 'SilentInstalledAppsEnabled', 'SoftLandingEnabled') {
            Reg $cdm $n 0
        }
        Reg "$root\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" 'Start_IrisRecommendations' 0
        Reg "$root\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement" 'ScoobeSystemSettingEnabled' 0
        RegEntfernt "$root\Software\Microsoft\Windows\CurrentVersion\Run" 'OneDriveSetup'
    }

    # Suchindizierung
    Schritt 'Suchindizierung (WSearch) deaktiviert' `
        { (Get-Service WSearch).StartType -eq 'Disabled' } `
        { Stop-Service WSearch -Force; Set-Service WSearch -StartupType Disabled }

    # Vorinstallierte Apps ohne Bezug zu HälpMi. Store, App Installer (winget), Terminal,
    # Editor, Rechner, Paint und Windows-Sicherheit bleiben.
    # Widgets über das Paket statt per Richtlinie: den Dsh-Schlüssel sperrt der UCPD-Treiber für Skripte.
    foreach ($app in 'MicrosoftWindows.Client.WebExperience', 'Microsoft.Copilot', 'Microsoft.BingNews', 'Microsoft.BingWeather', 'Microsoft.BingSearch',
                     'Microsoft.GamingApp', 'Microsoft.XboxGamingOverlay', 'Microsoft.Xbox.TCUI',
                     'Microsoft.XboxSpeechToTextOverlay', 'Microsoft.MicrosoftSolitaireCollection',
                     'Microsoft.Todos', 'Microsoft.PowerAutomateDesktop', 'Clipchamp.Clipchamp',
                     'Microsoft.YourPhone', 'Microsoft.GetHelp', 'Microsoft.WindowsFeedbackHub',
                     'Microsoft.OutlookForWindows', 'MSTeams', 'Microsoft.Windows.DevHome') {
        Schritt "App entfernt: $app" `
            { -not (Get-AppxPackage -AllUsers -Name $app) -and
              -not (Get-AppxProvisionedPackage -Online | Where-Object DisplayName -EQ $app) }.GetNewClosure() `
            { Get-AppxProvisionedPackage -Online | Where-Object DisplayName -EQ $app |
                  Remove-AppxProvisionedPackage -Online -AllUsers | Out-Null
              Get-AppxPackage -AllUsers -Name $app | ForEach-Object {
                  Remove-AppxPackage -Package $_.PackageFullName -AllUsers } }.GetNewClosure()
    }

    # Defender bleibt an, nur das Runner-Arbeitsverzeichnis ist ausgenommen.
    $runnerWork = 'C:\actions-runner\_work'
    Schritt "Defender-Ausnahme $runnerWork" `
        { (Get-MpPreference).ExclusionPath -contains $runnerWork }.GetNewClosure() `
        { Add-MpPreference -ExclusionPath $runnerWork }.GetNewClosure()

    # Zeit und Sprache
    Schritt 'Zeitzone W. Europe Standard Time' `
        { (Get-TimeZone).Id -eq 'W. Europe Standard Time' } `
        { Set-TimeZone -Id 'W. Europe Standard Time' }
    Schritt 'Systemsprache de-DE' `
        { (Get-WinSystemLocale).Name -eq 'de-DE' -and (Get-Culture).Name -eq 'de-DE' } `
        $null 'Windows mit deutscher ISO neu installieren oder Sprachpaket de-DE einrichten.'

    # Bildschirm: der Standard-VGA-Adapter übernimmt die Auflösung aus der UEFI-Firmware.
    Schritt 'Auflösung 1920x1080' `
        { $v = Get-CimInstance Win32_VideoController | Select-Object -First 1
          $v.CurrentHorizontalResolution -eq 1920 -and $v.CurrentVerticalResolution -eq 1080 } `
        $null 'PVE-Konsole: VM neu starten, beim Logo ESC → Device Manager → OVMF Platform Configuration → 1920x1080 → Commit → Reset.'

    # Autologon braucht das Kennwort, deshalb nur prüfen.
    Schritt "Autologon für $TestNutzer" `
        { $w = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
          $w.AutoAdminLogon -eq '1' -and $w.DefaultUserName -eq $TestNutzer }.GetNewClosure() `
        $null "Sysinternals Autologon (autologon64.exe) als Administrator starten, $TestNutzer + Kennwort aus Vaultwarden eintragen."

    # Netzwerk und SSH
    Schritt 'Netzwerkprofil Privat' `
        { @(Get-NetConnectionProfile | Where-Object NetworkCategory -NE 'Private').Count -eq 0 } `
        { Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private }
    Schritt 'Ping (ICMPv4) erlaubt' `
        { (Get-NetFirewallRule -Name 'FPS-ICMP4-ERQ-In').Enabled -eq 'True' } `
        { Enable-NetFirewallRule -Name 'FPS-ICMP4-ERQ-In' }
    Schritt 'SSH-Dienst automatisch und gestartet' `
        { $s = Get-Service sshd; $s.StartType -eq 'Automatic' -and $s.Status -eq 'Running' } `
        { Set-Service sshd -StartupType Automatic; Start-Service sshd }
    Reg 'HKLM:\SOFTWARE\OpenSSH' 'DefaultShell' 'C:\Program Files\PowerShell\7\pwsh.exe' 'String'

    $schluesselDatei = 'C:\ProgramData\ssh\administrators_authorized_keys'
    if ($SshSchluessel.Count -gt 0) {
        Schritt 'SSH-Schlüssel eingetragen, Rechte nur Administratoren und SYSTEM' `
            { if (-not (Test-Path $schluesselDatei)) { return $false }
              $zeilen = @(Get-Content $schluesselDatei)
              $acl = Get-Acl $schluesselDatei
              $sids = @($acl.Access | ForEach-Object {
                  $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value })
              @($SshSchluessel | Where-Object { $zeilen -notcontains $_ }).Count -eq 0 -and
              $acl.AreAccessRulesProtected -and
              @($sids | Where-Object { $_ -notin 'S-1-5-32-544', 'S-1-5-18' }).Count -eq 0 }.GetNewClosure() `
            { $zeilen = if (Test-Path $schluesselDatei) { @(Get-Content $schluesselDatei) } else { @() }
              $neu = @($zeilen) + @($SshSchluessel | Where-Object { $zeilen -notcontains $_ })
              Set-Content $schluesselDatei ($neu | Where-Object { $_ }) -Encoding ascii
              icacls $schluesselDatei /inheritance:r /grant '*S-1-5-32-544:F' /grant '*S-1-5-18:F' | Out-Null }.GetNewClosure()
    }
    if ($SshErlaubteAdressen.Count -gt 0) {
        $soll = @($SshErlaubteAdressen | Sort-Object)
        Schritt "SSH nur von $($soll -join ', ')" `
            { $ist = @((Get-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' | Get-NetFirewallAddressFilter).RemoteAddress | Sort-Object)
              ($ist -join ',') -eq ($soll -join ',') }.GetNewClosure() `
            { Set-NetFirewallRule -Name 'OpenSSH-Server-In-TCP' -RemoteAddress $soll }.GetNewClosure()
    }

    # Darf HälpMi nicht fehlen - nur prüfen, das Skript schaltet hier nie etwas ab.
    foreach ($dienst in 'Audiosrv', 'AudioEndpointBuilder', 'Schedule', 'mpssvc', 'WinDefend') {
        Schritt "Dienst $dienst läuft" `
            { (Get-Service $dienst).Status -eq 'Running' }.GetNewClosure() `
            { Set-Service $dienst -StartupType Automatic; Start-Service $dienst }.GetNewClosure()
    }

    # --- Ausführen ----------------------------------------------------------------------

    # Die Closures laufen im globalen Gültigkeitsbereich und sähen sonst nur dessen
    # ErrorActionPreference; Fehler würden nicht abgefangen, sondern erst am Ende ausgegeben.
    $globalAlt = $global:ErrorActionPreference
    $global:ErrorActionPreference = 'Stop'
    $ergebnis = foreach ($s in $schritte) {
        $status = try {
            if (& $s.Ist) { 'ok' }
            elseif ($Pruefen) { 'ABWEICHUNG' }
            elseif ($null -eq $s.Setze) { 'VON HAND' }
            else {
                & $s.Setze
                if (& $s.Ist) { 'gesetzt' } else { 'FEHLER' }
            }
        } catch { "FEHLER: $($_.Exception.Message)" }

        $farbe = switch -Wildcard ($status) {
            'ok' { 'DarkGray' } 'gesetzt' { 'Green' } 'FEHLER*' { 'Red' } default { 'Yellow' }
        }
        $text = if ($status -eq 'ok') { 'war schon so' } else { $status }
        Write-Host ("{0,-14} {1}" -f $text, $s.Name) -ForegroundColor $farbe
        if ($status -in 'VON HAND', 'ABWEICHUNG' -and $s.Hinweis) { Write-Host "               → $($s.Hinweis)" -ForegroundColor Yellow }
        [pscustomobject]@{ Name = $s.Name; Status = $status }
    }
    $global:ErrorActionPreference = $globalAlt

    $offen = @($ergebnis | Where-Object Status -NotIn 'ok', 'gesetzt')
    Write-Host ''
    Write-Host ("{0} Schritte: {1} schon erfüllt, {2} gesetzt, {3} offen" -f $ergebnis.Count,
        @($ergebnis | Where-Object Status -EQ 'ok').Count,
        @($ergebnis | Where-Object Status -EQ 'gesetzt').Count, $offen.Count)
    if (-not $Pruefen -and @($ergebnis | Where-Object Status -EQ 'gesetzt').Count -gt 0) {
        Write-Host 'Einige Einstellungen wirken erst nach einem Neustart.' -ForegroundColor Yellow
    }
    if ($offen.Count -gt 0) { exit 1 }
}
finally {
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
    foreach ($h in $geladeneHives) { & reg.exe unload "HKU\$h" | Out-Null }
}
