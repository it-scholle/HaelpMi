<#
.SYNOPSIS
    Erzeugt für einen E2E-Lauf eine eigene Test-Kundengruppe mit Lizenz-Schlüsselpaar und
    signierten Lizenzen, wie der Install-Creator für einen Test-Installer
    (LicenseFileSigner.GenerateKeyPair/CreateSigned, Issue #56: ein Schlüsselpaar pro Kundengruppe).

.DESCRIPTION
    Ohne gültige Lizenz gilt für User-Geräte Kontingent 0 (LicenseLimitGuard), ein manuell per
    ISCC gebauter Installer hat aber nur den Nullschlüssel und kann nie eine Lizenz prüfen.
    Der private Schlüssel existiert nur im Speicher dieses Laufs und wird nirgends gespeichert.

    Ausgabe: lizenz.json unter -Ausgabe, daneben für T9 mit demselben Schlüssel
    lizenz-bald.json (läuft in 10 Tagen ab) und lizenz-abgelaufen.json (vor 60 Tagen ausgestellt,
    seit einem Tag abgelaufen); customer_group_id und license_public_key_hex nach
    $env:GITHUB_OUTPUT für ISCC /DCustomerGroupId=... /DLicensePublicKeyHex=...

.PARAMETER Payload
    Ordner mit HaelpMi.Core.dll und BouncyCastle.Cryptography.dll (installer\payload nach dotnet publish).

.PARAMETER UserLimit
    Kontingent der Testlizenzen; der Admin zählt mit (LicenseLimitGuard).

.PARAMETER GueltigTage
    Laufzeit von lizenz.json. Muss über LicenseWarningEvaluator.WarningWindowDays (30) liegen,
    sonst gilt schon die gültige Lizenz als "läuft bald ab" und der Agent zeigt einen Toast.
#>
param(
    [Parameter(Mandatory)] [string] $Payload,
    [Parameter(Mandatory)] [string] $Ausgabe,
    [int] $UserLimit = 5,
    [int] $GueltigTage = 365
)
$ErrorActionPreference = 'Stop'

# Add-Type lädt HaelpMi.Core.dll in die Laufzeit von PowerShell selbst; eine ältere .NET-Version als die
# des Produkts kann die Assembly nicht laden.
if ([Environment]::Version.Major -lt 10) {
    throw "PowerShell $($PSVersionTable.PSVersion) läuft auf .NET $([Environment]::Version), HaelpMi.Core braucht .NET 10. PowerShell 7.6 oder neuer verwenden."
}

# Signaturbasis aus HaelpMi.Core selbst (License.GetSigningPayload), damit das Format nicht doppelt gepflegt wird.
Add-Type -Path (Join-Path $Payload 'BouncyCastle.Cryptography.dll')
Add-Type -Path (Join-Path $Payload 'HaelpMi.Core.dll')

$generator = [Org.BouncyCastle.Crypto.Generators.Ed25519KeyPairGenerator]::new()
$generator.Init([Org.BouncyCastle.Crypto.Parameters.Ed25519KeyGenerationParameters]::new([Org.BouncyCastle.Security.SecureRandom]::new()))
$paar = $generator.GenerateKeyPair()
$oeffentlich = $paar.Public.GetEncoded()

$gruppe = [guid]::NewGuid()
$t = [datetime]::UtcNow
$jetzt = [datetime]::new($t.Year, $t.Month, $t.Day, $t.Hour, $t.Minute, $t.Second, [DateTimeKind]::Utc)
$stufe = [HaelpMi.Core.Licensing.LicenseTier]::Custom
$optionen = [System.Text.Json.JsonSerializerOptions]::new()
$ordner = Split-Path -Parent $Ausgabe
New-Item -ItemType Directory -Force -Path $ordner | Out-Null

function Schreibe-Lizenz([string] $Ziel, [datetime] $Ausgestellt, [datetime] $Ablauf) {
    $daten = [HaelpMi.Core.Licensing.License]::new($gruppe, $stufe, $UserLimit, $Ausgestellt, $Ablauf, '').GetSigningPayload()
    $signierer = [Org.BouncyCastle.Crypto.Signers.Ed25519Signer]::new()
    $signierer.Init($true, $paar.Private)
    $signierer.BlockUpdate($daten, 0, $daten.Length)
    $signatur = [Convert]::ToBase64String($signierer.GenerateSignature())
    $lizenz = [HaelpMi.Core.Licensing.License]::new($gruppe, $stufe, $UserLimit, $Ausgestellt, $Ablauf, $signatur)
    $json = [System.Text.Json.JsonSerializer]::Serialize($lizenz, [HaelpMi.Core.Licensing.License], $optionen)

    # Gegenprobe wie LicenseReader: Datei zurücklesen und Signatur mit dem öffentlichen Schlüssel prüfen.
    $zurueck = [System.Text.Json.JsonSerializer]::Deserialize($json, [HaelpMi.Core.Licensing.License], $optionen)
    $pruefdaten = $zurueck.GetSigningPayload()
    $pruefer = [Org.BouncyCastle.Crypto.Signers.Ed25519Signer]::new()
    $pruefer.Init($false, [Org.BouncyCastle.Crypto.Parameters.Ed25519PublicKeyParameters]::new($oeffentlich, 0))
    $pruefer.BlockUpdate($pruefdaten, 0, $pruefdaten.Length)
    if (-not $pruefer.VerifySignature([Convert]::FromBase64String($zurueck.SignatureBase64))) {
        throw "Gegenprobe fehlgeschlagen: zurückgelesene Testlizenz $Ziel lässt sich nicht verifizieren."
    }

    [IO.File]::WriteAllText($Ziel, $json, [Text.UTF8Encoding]::new($false))
    "$(Split-Path -Leaf $Ziel): ausgestellt $($Ausgestellt.ToString('yyyy-MM-dd')), gültig bis $($Ablauf.ToString('yyyy-MM-dd'))"
}

# lizenz-bald.json mit derselben Ausstellungszeit wie lizenz.json: TryAdoptFromPeer übernimmt nur echt neuere
# Lizenzen, so tauschen die Geräte zwischen den T9-Stufen keine Lizenz untereinander aus.
Schreibe-Lizenz $Ausgabe $jetzt $jetzt.AddDays($GueltigTage)
Schreibe-Lizenz (Join-Path $ordner 'lizenz-bald.json') $jetzt $jetzt.AddDays(10)
Schreibe-Lizenz (Join-Path $ordner 'lizenz-abgelaufen.json') $jetzt.AddDays(-60) $jetzt.AddDays(-1)

$hex = [Convert]::ToHexString($oeffentlich)
"Test-Kundengruppe $gruppe, Kontingent $UserLimit"
if ($env:GITHUB_OUTPUT) {
    "customer_group_id=$gruppe" | Out-File -Append -Encoding utf8 $env:GITHUB_OUTPUT
    "license_public_key_hex=$hex" | Out-File -Append -Encoding utf8 $env:GITHUB_OUTPUT
}
