// HaelpMi.LicenseSigner - Ausstellungs-/Release-Werkzeug, niemals an Kunden ausliefern
// (CLAUDE.md "Lizenz & Secrets": Kunden-Lizenzsignatur ist ein SEPARATER Ed25519-Schlüssel
// vom Update-Signaturschlüssel; nur der öffentliche Teil landet in HaelpMi.Core, der
// private Schlüssel gehört nie ins Repo, nie ins Log, nie in eine Fehlermeldung).
//
// Verwendung:
//   HaelpMi.LicenseSigner genkey <privateKeyOut.txt> <publicKeyOut.txt>
//   HaelpMi.LicenseSigner sign <customerName> <seatCount> <expiresOn:yyyy-MM-dd> <privateKey.txt> <licenseOut.json>
//
// Die eigentliche Ed25519-Logik liegt in LicenseSigningOperations.cs.

using HaelpMi.LicenseSigner;

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

    var keyPair = LicenseSigningOperations.GenerateKeyPair();

    File.WriteAllText(args[1], Convert.ToBase64String(keyPair.PrivateKey));
    File.WriteAllText(args[2], Convert.ToBase64String(keyPair.PublicKey));

    Console.WriteLine("Schlüsselpaar erzeugt.");
    Console.WriteLine($"PRIVAT (niemals ins Repo, niemals loggen): {Path.GetFullPath(args[1])}");
    Console.WriteLine($"OEFFENTLICH (in HaelpMi.Core/Licensing/LicensePublicKey.cs einbetten): {Path.GetFullPath(args[2])}");
    return 0;
}

static int Sign(string[] args)
{
    if (args.Length != 6)
    {
        Console.Error.WriteLine("Verwendung: sign <customerName> <seatCount> <expiresOn:yyyy-MM-dd> <privateKey.txt> <licenseOut.json>");
        return 1;
    }

    var customerName = args[1];
    if (!int.TryParse(args[2], out var seatCount))
    {
        Console.Error.WriteLine($"Ungültige Sitzanzahl: {args[2]}");
        return 1;
    }

    if (!DateOnly.TryParse(args[3], out var expiresOnUtc))
    {
        Console.Error.WriteLine($"Ungültiges Ablaufdatum (erwartet yyyy-MM-dd): {args[3]}");
        return 1;
    }

    var privateKeyPath = args[4];
    var licenseOutPath = args[5];

    var privateKeyBytes = Convert.FromBase64String(File.ReadAllText(privateKeyPath).Trim());

    var license = LicenseSigningOperations.Sign(customerName, seatCount, expiresOnUtc, privateKeyBytes);

    File.WriteAllText(licenseOutPath, LicenseSigningOperations.ToLicenseJson(license));
    Console.WriteLine($"Lizenzdatei geschrieben: {Path.GetFullPath(licenseOutPath)}");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("HaelpMi.LicenseSigner - Ausstellungs-/Release-Werkzeug, niemals an Kunden ausliefern.");
    Console.WriteLine("  genkey <privateKeyOut.txt> <publicKeyOut.txt>");
    Console.WriteLine("  sign <customerName> <seatCount> <expiresOn:yyyy-MM-dd> <privateKey.txt> <licenseOut.json>");
}
