namespace HaelpMi.E2E.Tests;

/// <summary>
/// Gemeinsame Werte für T11, T12, T18 und T19 mit drei Geräten: A (Admin), B und C (User). Die Testmethoden stehen in
/// GeraetATests, GeraetBTests und GeraetCTests, die Abstimmung läuft wie bei <see cref="ZweiGeraete"/> über
/// <see cref="Rendezvous"/>.
/// </summary>
internal static class DreiGeraete
{
    /// <summary>Wert des Traits "Teil"; der Workflow filtert diese Tests per "Teil!=DreiGeraete" heraus, wenn a, b oder c fehlt.</summary>
    public const string Teil = "DreiGeraete";

    /// <summary>Über T5 (1000), unter dem, was das Dashboard ab T6 daraus hochzählt, und unter T10 (5000).</summary>
    public const int ConfigVersion = 1100;

    /// <summary>T18: über T12, unter dem, was das Dashboard ab T6 daraus hochzählt, und unter T10 (5000).</summary>
    public const int ConfigVersionR136 = 1200;

    /// <summary>T19: über T18, unter dem, was das Dashboard ab T6 daraus hochzählt, und unter T10 (5000).</summary>
    public const int ConfigVersionMehrfach = 1300;

    /// <summary>Zwei Empfänger, beide müssen "Bin unterwegs" melden, bevor die Alarmfenster schließbar werden.</summary>
    public const int Schwelle = 2;

    public const string AlarmText = "E2E-Alarm A an B und C";

    /// <summary>
    /// Marke von C nach T12, auch bei einem Fehlschlag. Bewusst ohne Präfix "t12-": nach einem Abbruch von T12
    /// soll A trotzdem darauf warten können (<see cref="Rendezvous.WaitForAsync"/> endet sonst sofort).
    /// </summary>
    public const string CGestoppt = "c-t12-gestoppt";

    /// <summary>Wie <see cref="CGestoppt"/>, nach T18.</summary>
    public const string CGestopptT18 = "c-t18-gestoppt";

    /// <summary>Wie <see cref="CGestopptT18"/>, nach T19.</summary>
    public const string CGestopptT19 = "c-t19-gestoppt";

    /// <summary>Wartet auf die DeviceIds aus den Marken "a-installiert", "b-installiert" und "c-installiert".</summary>
    public static async Task<(Guid A, Guid B, Guid C)> DeviceIdsAsync() =>
        (await ZweiGeraete.DeviceIdDerGegenseiteAsync("a"),
         await ZweiGeraete.DeviceIdDerGegenseiteAsync("b"),
         await ZweiGeraete.DeviceIdDerGegenseiteAsync("c"));

    /// <summary>Profil A an B und C mit <see cref="Schwelle"/>; jedes der drei Geräte schreibt es selbst.</summary>
    /// <param name="version">ConfigVersion, Standard <see cref="ConfigVersion"/> (T12).</param>
    /// <returns>Die DeviceIds aus <see cref="DeviceIdsAsync"/>.</returns>
    public static async Task<(Guid A, Guid B, Guid C)> BereiteAlarmprofilVorAsync(int version = ConfigVersion)
    {
        var ids = await DeviceIdsAsync();
        await ZweiGeraete.BereiteAlarmprofilVorAsync(ids.A, new[] { ids.B, ids.C }, version, Schwelle, AlarmText);
        return ids;
    }
}
