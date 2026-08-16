using System.Text;

namespace HaelpMi.UpdateBootstrapper;

/// <summary>
/// Liest das Update-Paket, das Install-Creator ("Update erstellen") an das Ende dieser
/// bereits fertig veröffentlichten .exe angehängt hat - siehe
/// <c>HaelpMi.InstallCreator.UpdatePackageBuilder.AppendUpdatePackage</c> für die
/// Schreib-Seite und das dort dokumentierte, identische Footer-Format.
///
/// Bewusst simples Byte-Anhängen an eine bereits fertig veröffentlichte Single-File-Exe
/// statt eines &lt;EmbeddedResource&gt;, das zur Kompilierzeit fest im Assembly stünde und
/// echte Paket-Binärdaten (Zip mit Agent/Config/UpdateService) im Projektordner/Git
/// vorhalten müsste, nur damit HaelpMi.UpdateBootstrapper für sich allein baubar bleibt
/// (dasselbe Problem, das <c>installer/payload/</c> per .gitignore umgeht - hier geht das
/// nicht, weil ein &lt;EmbeddedResource&gt; zur Kompilierzeit existieren MUSS). Install-Creator
/// veröffentlicht die generische Bootstrapper-.exe bei jedem "Update erstellen" trotzdem
/// frisch (kein Cache/keine Wiederverwendung über Builds hinweg) - gleiches Prinzip wie
/// RefreshPayloadAsync, siehe dortiger Bugfix-Kommentar zu stillschweigend veraltetem
/// Payload.
///
/// Funktioniert zuverlässig, weil sowohl der Windows-PE-Loader als auch der .NET-Single-
/// File-AppHost die Position ihrer eigenen Nutzdaten über feste Offsets im PE-Header
/// referenzieren, nicht durch eine Suche vom Dateiende her - überzähliger Anhang danach
/// ("Overlay-Daten") wird von beiden schlicht ignoriert. Dieselbe Technik wie bei jedem
/// klassischen selbstentpackenden Archiv/Installer-Stub.
/// </summary>
internal static class EmbeddedPackage
{
    // Reihenfolge/Länge MUSS exakt zu UpdatePackageBuilder.AppendUpdatePackage
    // (HaelpMi.InstallCreator) passen - siehe dortiger Kommentar für die vollständige
    // Format-Doku: [package.zip][manifest.json][8 Byte LE Länge Paket][8 Byte LE Länge
    // Manifest][8 ASCII-Byte Magic].
    private const string MagicFooter = "HMUBEGG1";

    public static (string ManifestJson, byte[] PackageZip)? TryExtract()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is null || !File.Exists(exePath))
        {
            return null;
        }

        using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var magicBytes = Encoding.ASCII.GetBytes(MagicFooter);
        const int lengthsSize = 16; // zwei Int64 (Paketlänge, Manifestlänge)
        if (stream.Length < magicBytes.Length + lengthsSize)
        {
            return null; // zu kurz, um überhaupt einen Footer enthalten zu können
        }

        var tail = new byte[magicBytes.Length];
        stream.Seek(-magicBytes.Length, SeekOrigin.End);
        stream.ReadExactly(tail);
        if (!tail.AsSpan().SequenceEqual(magicBytes))
        {
            return null; // rohe, unbestückte Bootstrapper-exe - kein Fehler, nur "nichts eingebettet"
        }

        var lengths = new byte[lengthsSize];
        stream.Seek(-(magicBytes.Length + lengthsSize), SeekOrigin.End);
        stream.ReadExactly(lengths);
        var packageLength = BitConverter.ToInt64(lengths, 0);
        var manifestLength = BitConverter.ToInt64(lengths, 8);

        var footerStart = stream.Length - magicBytes.Length - lengthsSize;
        var manifestStart = footerStart - manifestLength;
        var packageStart = manifestStart - packageLength;
        if (packageLength < 0 || manifestLength < 0 || packageStart < 0)
        {
            return null; // beschädigter/manipulierter Footer - lieber ablehnen als falsch lesen
        }

        var packageBytes = new byte[packageLength];
        stream.Seek(packageStart, SeekOrigin.Begin);
        stream.ReadExactly(packageBytes);

        var manifestBytes = new byte[manifestLength];
        stream.Seek(manifestStart, SeekOrigin.Begin);
        stream.ReadExactly(manifestBytes);

        return (Encoding.UTF8.GetString(manifestBytes), packageBytes);
    }
}
