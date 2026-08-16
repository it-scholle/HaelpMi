using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace HaelpMi.UpdateSigner;

/// <summary>
/// Reine Ed25519-Erzeugungs-/Signierlogik, herausgelöst aus <c>Program.cs</c> (Nutzerwunsch:
/// HaelpMi.InstallCreator soll Schlüssel erzeugen und Pakete signieren können, ohne die
/// CLI-Kommandozeile über einen Kindprozess aufzurufen - siehe ProjectReference-Kommentar in
/// HaelpMi.InstallCreator.csproj). Der CLI-Einstiegspunkt hier ruft nur noch diese Methoden auf,
/// unverändertes Verhalten/Format gegenüber dem bisherigen Stand.
/// </summary>
public static class UpdateSigningOperations
{
    public readonly record struct KeyPair(byte[] PrivateKey, byte[] PublicKey);

    public static KeyPair GenerateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();

        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;

        return new KeyPair(privateKey.GetEncoded(), publicKey.GetEncoded());
    }

    /// <summary>
    /// Leitet den öffentlichen Schlüssel aus einem vorhandenen privaten Ed25519-Schlüssel ab
    /// (deterministische Eigenschaft von Ed25519 - der öffentliche Teil muss nie separat
    /// aufbewahrt werden). Nutzerwunsch 16.08.2026 ("separater Test-Key für Test-Installer"):
    /// Install-Creator hält in Vaultwarden nur die privaten Schlüssel - beim Bauen eines
    /// Installers wird hierüber der zum jeweils gewählten (Test- oder Produktiv-)Schlüssel
    /// passende öffentliche Schlüssel neu berechnet und in deployment.json eingebettet, statt
    /// ihn zusätzlich irgendwo vorzuhalten.
    /// </summary>
    public static byte[] DerivePublicKey(byte[] privateKeyBytes)
    {
        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
        return privateKey.GeneratePublicKey().GetEncoded();
    }

    /// <summary>
    /// Signiert <paramref name="payload"/> (SHA-256 + Ed25519) und liefert das fertige
    /// Manifest-Objekt zum Serialisieren - Format identisch zu dem, was
    /// <see cref="UpdatePackageVerifier"/> (HaelpMi.Core) und <c>UpdateSeedImporter</c> erwarten.
    /// </summary>
    public static UpdateManifestData Sign(byte[] payload, byte[] privateKeyBytes, string version)
    {
        var hash = SHA256.HashData(payload);

        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        signer.BlockUpdate(hash, 0, hash.Length);
        var signature = signer.GenerateSignature();

        return new UpdateManifestData(
            Version: version,
            Sha256Hex: Convert.ToHexString(hash).ToLowerInvariant(),
            SignatureBase64: Convert.ToBase64String(signature),
            BuiltAtUtc: DateTimeOffset.UtcNow);
    }

    public static string ToManifestJson(UpdateManifestData manifest) =>
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
}

/// <summary>Gleiche Feldnamen wie das bisherige anonyme Objekt in Program.cs - JSON-Format bleibt unverändert.</summary>
public sealed record UpdateManifestData(string Version, string Sha256Hex, string SignatureBase64, DateTimeOffset BuiltAtUtc);
