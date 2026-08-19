using HaelpMi.Core.Audio;
using HaelpMi.Core.Models;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Läuft bewusst gegen die echte NAudio/WASAPI-Pipeline statt gegen ein Mock - der
/// Fehlerbericht 07.08.2026 ("kein Signalton, kein Fehler, kompletter Stillstand") war
/// genau ein Fehler IN dieser Integration (NAudios SampleToWaveProvider + WasapiOut.Dispose
/// hingen sich auf einer Testumgebung unbegrenzt auf, siehe Kommentar in
/// MultiDeviceAlarmPlayer.cs) - ein reines Unit-Test-Mock hätte das nie gefangen. Braucht
/// mindestens ein aktives Wiedergabegerät (WASAPI Render/Active) auf dem Testrechner, um
/// aussagekräftig zu sein - auf CI-Maschinen ganz ohne Audiogerät ist ein leerer Durchlauf
/// (kein Play, kein Hang) ebenfalls ein gültiges, wenn auch weniger scharfes Ergebnis.
///
/// Nutzerwunsch 20.08.2026: läuft seit hier mit <c>silent: true</c> - derselbe echte
/// WASAPI-Pfad (Geräte, Mute/Lautstärke, WasapiOut, Timeout-Sicherheitsnetz) bleibt
/// vollständig unter Test, nur die abgespielte Nutzlast ist stumm. Grund: genau dieser Test
/// war es, der unregelmäßig und ungefragt hörbar auf einer Session-VM piepte (siehe
/// CLAUDE.md-Abschnitt "Tests").
/// </summary>
public class AudioTests
{
    [Fact]
    public async Task PlayOnAllActiveDevicesAsync_CompletesWithinBoundedTime_DoesNotHang()
    {
        // Regressionstest für den 07.08.2026 live gemeldeten und nachgestellten Bug: die
        // Wiedergabe blieb auf mindestens einer Testumgebung (ARM64-Windows) unbegrenzt
        // hängen (WasapiOut.Dispose() kehrte nie zurück, nachdem NAudios interne
        // Float->Byte-Konvertierung eine ArrayTypeMismatchException geworfen hatte) - kein
        // Fehler, keine Rückmeldung, einfach Stillstand. Der eigentliche Ton dauert nur
        // ca. 1-2s (siehe IncomingSoundCatalog) - 15s WaitAsync ist großzügig, aber endlich;
        // ein erneutes unbegrenztes Hängen lässt diesen Test zuverlässig mit einem klaren
        // Timeout fehlschlagen statt den gesamten Testlauf für immer zu blockieren.
        var messages = new List<string>();
        var player = new MultiDeviceAlarmPlayer(messages.Add);
        var option = IncomingSoundCatalog.Resolve(IncomingSoundCatalog.DefaultId);

        var playTask = player.PlayOnAllActiveDevicesAsync(option, silent: true);

        var exception = await Record.ExceptionAsync(() => playTask.WaitAsync(TimeSpan.FromSeconds(15)));

        Assert.Null(exception);
        // Kein Wiedergabegerät auf dem Testrechner ist ein gültiger, wenn auch schwächerer
        // Testlauf (siehe Klassenkommentar) - dann steht hier genau die eine erwartete
        // Meldung und sonst keine Fehlschlag-Meldung.
        Assert.DoesNotContain(messages, m => m.Contains("fehlgeschlagen", StringComparison.Ordinal) || m.Contains("abgebrochen", StringComparison.Ordinal));
    }
}
