<#
.SYNOPSIS
    Erzeugt für einen E2E-Lauf eine eigene Test-Kundengruppe mit Lizenz-Schlüsselpaar und
    signierter Lizenz, wie der Install-Creator für einen Test-Installer
    (LicenseFileSigner.GenerateKeyPair/CreateSigned, Issue #56: ein Schlüsselpaar pro Kundengruppe).

.DESCRIPTION
    Ohne gültige Lizenz gilt für User-Geräte Kontingent 0 (LicenseLimitGuard), ein manuell per
    ISCC gebauter Installer hat aber nur den Nullschlüssel und kann nie eine Lizenz prüfen.
    Der private Schlüssel existiert nur im Speicher dieses Laufs und wird nirgends gespeichert.

    Ausgabe: lizenz.json unter -Ausgabe; customer_group_id und license_public_key_hex nach
    $env:GITHUB_OUTPUT für ISCC /DCustomerGroupId=... /DLicensePublicKeyHex=...

.PARAMETER Payload
    Ordner mit HaelpMi.Core.dll und BouncyCastle.Cryptography.dll (installer\payload nach dotnet publish).

.PARAMETER UserLimit
    Kontingent der Testlizenz; der Admin zählt mit (LicenseLimitGuard).
#>
param(
    [Parameter(Mandatory)] [string] $Payload,
    [Parameter(Mandatory)] [string] $Ausgabe,
    [int] $UserLimit = 5,
    [int] $GueltigTage = 30
)
$ErrorActionPreference = 'Stop'

# Signaturbasis aus HaelpMi.Core selbst (License.GetSigningPayload), damit das Format nicht doppelt gepflegt wird.
Add-Type -Path (Join-Path $Payload 'BouncyCastle.Cryptography.dll')
Add-Type -Path (Join-Path $Payload 'HaelpMi.Core.dll')

$generator = [Org.BouncyCastle.Crypto.Generators.Ed25519KeyPairGenerator]::new()
$generator.Init([Org.BouncyCastle.Crypto.Parameters.Ed25519KeyGenerationParameters]::new([Org.BouncyCastle.Security.SecureRandom]::new()))
$paar = $generator.GenerateKeyPair()
$oeffentlich = $paar.Public.GetEncoded()

$gruppe = [guid]::NewGuid()
$t = [datetime]::UtcNow
$ausgestellt = [datetime]::new($t.Year, $t.Month, $t.Day, $t.Hour, $t.Minute, $t.Second, [DateTimeKind]::Utc)
$ablauf = $ausgestellt.AddDays($GueltigTage)
$stufe = [HaelpMi.Core.Licensing.LicenseTier]::Custom

$daten = [HaelpMi.Core.Licensing.License]::new($gruppe, $stufe, $UserLimit, $ausgestellt, $ablauf, '').GetSigningPayload()
$signierer = [Org.BouncyCastle.Crypto.Signers.Ed25519Signer]::new()
$signierer.Init($true, $paar.Private)
$signierer.BlockUpdate($daten, 0, $daten.Length)
$signatur = [Convert]::ToBase64String($signierer.GenerateSignature())
$lizenz = [HaelpMi.Core.Licensing.License]::new($gruppe, $stufe, $UserLimit, $ausgestellt, $ablauf, $signatur)

$optionen = [System.Text.Json.JsonSerializerOptions]::new()
$json = [System.Text.Json.JsonSerializer]::Serialize($lizenz, [HaelpMi.Core.Licensing.License], $optionen)

# Gegenprobe wie LicenseReader: Datei zurücklesen und Signatur mit dem öffentlichen Schlüssel prüfen.
$zurueck = [System.Text.Json.JsonSerializer]::Deserialize($json, [HaelpMi.Core.Licensing.License], $optionen)
$pruefdaten = $zurueck.GetSigningPayload()
$pruefer = [Org.BouncyCastle.Crypto.Signers.Ed25519Signer]::new()
$pruefer.Init($false, [Org.BouncyCastle.Crypto.Parameters.Ed25519PublicKeyParameters]::new($oeffentlich, 0))
$pruefer.BlockUpdate($pruefdaten, 0, $pruefdaten.Length)
if (-not $pruefer.VerifySignature([Convert]::FromBase64String($zurueck.SignatureBase64))) {
    throw 'Gegenprobe fehlgeschlagen: zurückgelesene Testlizenz lässt sich nicht verifizieren.'
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Ausgabe) | Out-Null
[IO.File]::WriteAllText($Ausgabe, $json, [Text.UTF8Encoding]::new($false))

$hex = [Convert]::ToHexString($oeffentlich)
"Test-Kundengruppe $gruppe, Kontingent $UserLimit, gültig bis $($ablauf.ToString('yyyy-MM-dd'))"
if ($env:GITHUB_OUTPUT) {
    "customer_group_id=$gruppe" | Out-File -Append -Encoding utf8 $env:GITHUB_OUTPUT
    "license_public_key_hex=$hex" | Out-File -Append -Encoding utf8 $env:GITHUB_OUTPUT
}
