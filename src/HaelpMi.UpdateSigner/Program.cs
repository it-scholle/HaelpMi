// HaelpMi.UpdateSigner - Release-Werkzeug, niemals an Kunden ausliefern (CLAUDE.md
// "Lizenz & Secrets": Update-Signatur ist ein SEPARATER Ed25519-Schlüssel von der
// Lizenzsignatur; nur der öffentliche Teil landet in HaelpMi.Core, der private Schlüssel
// gehört nie ins Repo, nie ins Log, nie in eine Fehlermeldung).
//
// Verwendung:
//   HaelpMi.UpdateSigner genkey <privateKeyOut.txt> <publicKeyOut.txt>
//   HaelpMi.UpdateSigner sign <payload.zip> <privateKey.txt> <version> <manifestOut.json>
//
// Die eigentliche Ed25519-Logik liegt in UpdateSigningOperations.cs - HaelpMi.InstallCreator
// ruft dieselben Methoden direkt in-process auf (ProjectReference), statt diese CLI als
// Kindprozess zu starten, damit der private Schlüssel dort nie über eine Kommandozeile oder
// eine temporäre Datei laufen muss.

using HaelpMi.UpdateSigner;

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

    var keyPair = UpdateSigningOperations.GenerateKeyPair();

    File.WriteAllText(args[1], Convert.ToBase64String(keyPair.PrivateKey));
    File.WriteAllText(args[2], Convert.ToBase64String(keyPair.PublicKey));

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
    var privateKeyBytes = Convert.FromBase64String(File.ReadAllText(privateKeyPath).Trim());

    var manifest = UpdateSigningOperations.Sign(payload, privateKeyBytes, version);

    File.WriteAllText(manifestOutPath, UpdateSigningOperations.ToManifestJson(manifest));
    Console.WriteLine($"Manifest geschrieben: {Path.GetFullPath(manifestOutPath)}");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("HaelpMi.UpdateSigner - Release-Werkzeug, niemals an Kunden ausliefern.");
    Console.WriteLine("  genkey <privateKeyOut.txt> <publicKeyOut.txt>");
    Console.WriteLine("  sign <payload.zip> <privateKey.txt> <version> <manifestOut.json>");
}
