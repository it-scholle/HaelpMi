using System.Security.Cryptography;
using HaelpMi.Core.Models;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace HaelpMi.Core.Security;

/// <summary>
/// Erzeugt und benutzt das vierte kryptografische Schlüsselpaar (Ed25519,
/// Admin-Rollen-Signatur, CLAUDE.md "Lizenz &amp; Secrets" Punkt 4) - anders als die
/// Kunden-Lizenzsignatur/Update-Signatur wird dieses Paar **pro Kunden-Gruppe** erzeugt
/// (Install-Creator, bei jedem Admin-Installer-Build neu), nicht einmalig global.
///
/// Bewusst ein eigener, von <c>HaelpMi.UpdateSigner.UpdateSigningOperations</c> getrennter
/// Ed25519-Keygen-Aufruf, obwohl die Kryptoprimitive identisch ist - CLAUDE.md
/// "niemals verwechseln oder zusammenlegen" gilt für die Codepfade, nicht nur die
/// Schlüssel selbst. <c>HaelpMi.InstallCreator</c> dupliziert diesen Aufruf zusätzlich
/// noch einmal lokal (siehe dortige Datei), weil dieses Tool bewusst keine
/// ProjectReference auf HaelpMi.Core hält (siehe HaelpMi.InstallCreator.csproj-Kommentar) -
/// ein winziger, rein mechanischer Ed25519-Keygen-Aufruf ohne Domänenlogik, keine echte
/// Duplikation von Verhalten.
/// </summary>
public static class AdminRoleSigner
{
    public readonly record struct KeyPair(string PublicKeyBase64, string PrivateKeyBase64);

    public static KeyPair GenerateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();

        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;

        return new KeyPair(Convert.ToBase64String(publicKey.GetEncoded()), Convert.ToBase64String(privateKey.GetEncoded()));
    }

    /// <summary>
    /// Signiert den Boot-Call-Anspruch dieses Geräts, falls es überhaupt ein Admin-Gerät
    /// mit eingebettetem privaten Schlüssel ist - sonst <c>null</c> (kein Fehler, nur
    /// "kann nicht signieren", z. B. jedes User-Gerät). Hash-dann-signieren, gleiches
    /// Muster wie <c>UpdatePackageVerifier</c>/<c>UpdateSigningOperations</c>. Nimmt
    /// bewusst einzelne Werte statt eines ganzen <see cref="DeploymentInfo"/> entgegen -
    /// der einzige Aufrufer (DiscoveryService) hat Rolle/CustomerGroupId ohnehin schon
    /// über <see cref="LiveIdentity"/>, nur den privaten Schlüssel nicht.
    /// </summary>
    public static string? TrySign(Role role, Guid customerGroupId, string? adminRolePrivateKeyBase64, Guid deviceId, DateTimeOffset sentAtUtc)
    {
        if (role != Role.Admin || string.IsNullOrEmpty(adminRolePrivateKeyBase64))
        {
            return null;
        }

        try
        {
            var privateKeyBytes = Convert.FromBase64String(adminRolePrivateKeyBase64);
            var hash = SHA256.HashData(AdminRoleClaim.BuildPayload(customerGroupId, deviceId, sentAtUtc));

            var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);
            var signer = new Ed25519Signer();
            signer.Init(true, privateKey);
            signer.BlockUpdate(hash, 0, hash.Length);
            return Convert.ToBase64String(signer.GenerateSignature());
        }
        catch (Exception)
        {
            // Ein kaputt eingetragener/zu kurzer Schlüssel darf das Signieren nie zum
            // Absturz bringen (z. B. beschädigte deployment.json) - dann eben unsigniert
            // ausliefern, der Empfänger behandelt das wie einen unsignierten Peer.
            return null;
        }
    }
}
