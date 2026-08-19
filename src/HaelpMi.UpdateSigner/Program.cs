// HaelpMi.UpdateSigner - Release-Werkzeug, niemals an Kunden ausliefern (CLAUDE.md
// "Lizenz & Secrets": Update-Signatur ist ein SEPARATER Ed25519-Schlüssel von der
// Lizenzsignatur; nur der öffentliche Teil landet in HaelpMi.Core, der private Schlüssel
// gehört nie ins Repo, nie ins Log, nie in eine Fehlermeldung).
//
// Verwendung:
//   HaelpMi.UpdateSigner genkey <privateKeyOut.txt> <publicKeyOut.txt>
//   HaelpMi.UpdateSigner sign <payload.zip> <privateKey.txt> <version> <manifestOut.json>

using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

return args[0] switch
{
    "genkey" => GenKey(args),
    "sign" => Sign(args),
    _ => Unknown(),
};

static int Unknown()
{
    PrintUsage();
    return 1;
}

static int GenKey(string[] args)
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("Verwendung: genkey <privateKeyOut.txt> <publicKeyOut.txt>");
        return 1;
    }

    var generator = new Ed25519KeyPairGenerator();
    generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
    var keyPair = generator.GenerateKeyPair();

    var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
    var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;

    File.WriteAllText(args[1], Convert.ToBase64String(privateKey.GetEncoded()));
    File.WriteAllText(args[2], Convert.ToBase64String(publicKey.GetEncoded()));

    Console.WriteLine("Schlüsselpaar erzeugt.");
    Console.WriteLine($"PRIVAT (niemals ins Repo, niemals loggen): {Path.GetFullPath(args[1])}");
    Console.WriteLine($"OEFFENTLICH (in HaelpMi.Core/Updates/UpdateSignaturePublicKey.cs einbetten): {Path.GetFullPath(args[2])}");
    return 0;
}

static int Sign(string[] args)
{
    if (args.Length != 5)
    {
        Console.Error.WriteLine("Verwendung: sign <payload.zip> <privateKey.txt> <version> <manifestOut.json>");
        return 1;
    }

    var payloadPath = args[1];
    var privateKeyPath = args[2];
    var version = args[3];
    var manifestOutPath = args[4];

    var payload = File.ReadAllBytes(payloadPath);
    var hash = SHA256.HashData(payload);

    var privateKeyBytes = Convert.FromBase64String(File.ReadAllText(privateKeyPath).Trim());
    var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes, 0);

    var signer = new Ed25519Signer();
    signer.Init(true, privateKey);
    signer.BlockUpdate(hash, 0, hash.Length);
    var signature = signer.GenerateSignature();

    var manifest = new
    {
        Version = version,
        Sha256Hex = Convert.ToHexString(hash).ToLowerInvariant(),
        SignatureBase64 = Convert.ToBase64String(signature),
        BuiltAtUtc = DateTimeOffset.UtcNow,
    };

    File.WriteAllText(manifestOutPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Manifest geschrieben: {Path.GetFullPath(manifestOutPath)}");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("HaelpMi.UpdateSigner - Release-Werkzeug, niemals an Kunden ausliefern.");
    Console.WriteLine("  genkey <privateKeyOut.txt> <publicKeyOut.txt>");
    Console.WriteLine("  sign <payload.zip> <privateKey.txt> <version> <manifestOut.json>");
}
