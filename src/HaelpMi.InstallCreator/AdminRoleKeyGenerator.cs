using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Erzeugt das vierte kryptografische Schlüsselpaar (Ed25519, Admin-Rollen-Signatur,
/// CLAUDE.md "Lizenz &amp; Secrets" Punkt 4, Nutzerwunsch 15.08.2026) - **pro Kunden-
/// Gruppe**, bei jedem Admin-Installer-Build neu, direkt neben der CustomerGroupId (siehe
/// MainWindow.xaml.cs BuildAdminButton_Click). Anders als der Update-Schlüssel lebt dieser
/// nie in Vaultwarden: er wandert direkt in genau diesen einen Build und muss dev-seitig
/// nie wiedergefunden werden.
///
/// Bewusst eine eigene, kleine Kopie des Ed25519-Keygen-Aufrufs (identisch zu
/// HaelpMi.Core.Security.AdminRoleSigner.GenerateKeyPair, das die Laufzeit-Sign/Verify-
/// Logik hält) statt einer ProjectReference auf HaelpMi.Core - dieses Tool hält bewusst
/// keine Referenz auf HaelpMi.Core/HaelpMi.UI (siehe HaelpMi.InstallCreator.csproj-
/// Kommentar: "teilt keine Laufzeit-Modelle mit der eigentlichen App"). Ein reiner,
/// domänenfreier Ed25519-Keygen-Aufruf duplizieren ist dafür kein echter Bruch dieses
/// Prinzips - BouncyCastle ist bereits transitiv verfügbar (ProjectReference auf
/// HaelpMi.UpdateSigner für den Update-Schlüssel).
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
