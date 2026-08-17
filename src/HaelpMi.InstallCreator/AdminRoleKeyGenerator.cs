using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Erzeugt das vierte kryptografische Schlüsselpaar (Ed25519, Admin-Rollen-Signatur,
/// Nutzerwunsch 17.08.2026, siehe CLAUDE.md "Lizenz &amp; Secrets") für einen neuen
/// Admin-Installer-Build. Bewusst ein eigener, von <c>UpdateSigningOperations</c>
/// getrennter Aufruf, obwohl die Kryptoprimitive identisch ist und über dieselbe
/// ProjectReference (HaelpMi.UpdateSigner) transitiv verfügbar wäre - CLAUDE.md
/// "niemals verwechseln oder zusammenlegen" gilt für die Codepfade, nicht nur die
/// Schlüssel selbst; Update-Signatur und Admin-Rollen-Signatur sind zwei völlig
/// unabhängige Vertrauensfragen (welches Programm-Update ist echt? vs. welches Gerät ist
/// wirklich Admin?), die sich hier zufällig dieselbe Primitive teilen. Spiegelt exakt
/// <c>HaelpMi.Core.Security.AdminRoleSigner.GenerateKeyPair()</c> - dieses Tool hält
/// bewusst keine ProjectReference auf HaelpMi.Core (siehe HaelpMi.InstallCreator.csproj-
/// Kommentar), ein winziger, rein mechanischer Keygen-Aufruf ohne Domänenlogik ist keine
/// echte Duplikation von Verhalten.
/// </summary>
internal static class AdminRoleKeyGenerator
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
}
