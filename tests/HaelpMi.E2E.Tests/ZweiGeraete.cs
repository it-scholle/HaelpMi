using HaelpMi.Core.Licensing;
using HaelpMi.Core.Models;
using HaelpMi.Core.Storage;
using Xunit;

namespace HaelpMi.E2E.Tests;

/// <summary>
/// Gemeinsame Schritte für T4/T5. Jedes Gerät führt seinen Teil selbst aus und stimmt sich
/// über <see cref="Rendezvous"/> ab; die Testmethoden stehen in GeraetATests/GeraetBTests,
/// weil xUnit die Reihenfolge von Klassen innerhalb einer Sammlung nicht festlegt.
/// </summary>
internal static class ZweiGeraete
{
    /// <summary>Wert des Traits "Teil"; ein Lauf mit nur einer VM filtert diese Tests per "Teil!=ZweiGeraete" heraus.</summary>
    public const string Teil = "ZweiGeraete";

    public static readonly TimeSpan GegenseiteTimeout = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Für Marken innerhalb eines Tests; kurz genug, dass ein Fehlschlag auf der Gegenseite nicht den ganzen Lauf aufhält.</summary>
    public static readonly TimeSpan SchrittTimeout = TimeSpan.FromMinutes(3);

    // A und B schreiben dieselbe shared-config.json, deshalb feste Werte statt Zufall.
    public static readonly Guid ProfilId =new("e2e00000-0000-4000-8000-000000000005");

    private const HotkeyModifiers StrgAltUmschalt = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    /// <summary>Strg+Alt+Umschalt+F11, kollidiert mit keinem Windows-Standardkürzel.</summary>
    public static readonly HotkeyDefinition AlarmHotkey = new(StrgAltUmschalt, 0x7A);

    // Die Agents registrieren die Hotkeys aller Profile; eine registrierte Kombination erreicht das
    // Tastenkürzel-Feld im Dashboard nicht mehr. Deshalb bekommt jedes Profil eine eigene, neue Kombination.
    public static readonly HotkeyDefinition HotkeyP1 = new(StrgAltUmschalt, 0x78);
    public static readonly HotkeyDefinition HotkeyP2 = new(StrgAltUmschalt, 0x79);
    public static readonly HotkeyDefinition HotkeyP2Neu = new(StrgAltUmschalt, 0x7B);

    public const string GruppeName = "E2E-G-A";
    public const string P1Name = "E2E-P1";
    public const string P2Name = "E2E-P2";
    public const string P2NameNeu = "E2E-P2b";

    // Die Dateien schreibt der Agent bzw. das Dashboard nebenher; ein halb geschriebener Stand zählt als "noch nicht".
    public static SharedConfig? LiesConfig() => Lies(() => JsonFileStore.Load<SharedConfig>(AppPaths.SharedConfigFilePath));

    public static OwnSettings? LiesEinstellungen() => Lies(() => JsonFileStore.Load<OwnSettings>(AppPaths.SettingsFilePath));

    public static List<DeviceEntry> LiesGeraete() => Lies(() => new DeviceStore().Load()) ?? new List<DeviceEntry>();

    private static T? Lies<T>(Func<T?> laden) where T : class
    {
        try
        {
            return laden();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Wartet bis <paramref name="bedingung"/> gilt und liefert die Wartezeit.</summary>
    public static async Task<TimeSpan> WarteMitDauerAsync(Func<bool> bedingung, TimeSpan timeout, Func<string> meldung)
    {
        var start = DateTime.UtcNow;
        await HaelpMiApp.WarteBisAsync(bedingung, timeout, meldung);
        return DateTime.UtcNow - start;
    }

    // Hotkeys und Boot-Call kommen erst nach dem Prozessstart im Hintergrund (StartBackgroundServices).
    public static readonly TimeSpan AgentAnlaufzeit = TimeSpan.FromSeconds(10);

    /// <summary><see cref="HaelpMiApp.AgentNeustartAsync"/> plus <see cref="AgentAnlaufzeit"/>, danach empfängt und sendet der Agent.</summary>
    public static async Task AgentNeustartMitAnlaufAsync()
    {
        await HaelpMiApp.AgentNeustartAsync();
        await Task.Delay(AgentAnlaufzeit);
    }

    public static Guid EigeneDeviceId() =>
        JsonFileStore.Load<OwnSettings>(AppPaths.SettingsFilePath)?.DeviceId
        ?? throw new InvalidOperationException($"{AppPaths.SettingsFilePath} fehlt, T1 lief nicht.");

    /// <param name="geraet">"a" oder "b"; T1 legt die DeviceId als Inhalt der Marke "&lt;geraet&gt;-installiert" ab.</param>
    public static async Task<Guid> DeviceIdDerGegenseiteAsync(string geraet) =>
        Guid.Parse((await Rendezvous.WaitForAsync($"{geraet}-installiert", GegenseiteTimeout)).Trim());

    /// <summary>Wartet, bis devices.json das Gerät mit Adresse enthält.</summary>
    public static async Task<DeviceEntry> WarteAufGeraetAsync(Guid deviceId, TimeSpan timeout)
    {
        var frist = DateTime.UtcNow + timeout;
        while (true)
        {
            var eintrag = new DeviceStore().Load().FirstOrDefault(d => d.DeviceId == deviceId && !string.IsNullOrEmpty(d.IpAddress));
            if (eintrag is not null)
            {
                return eintrag;
            }

            if (DateTime.UtcNow > frist)
            {
                Assert.Fail($"{AppPaths.DevicesFilePath} enthält Gerät {deviceId} nach {timeout} nicht (Boot-Call nicht angekommen, siehe netzwerk.txt).");
            }

            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// Agent stoppen, auf A und B identische shared-config.json mit einem Profil A→B
    /// (Schwelle 1, <see cref="AlarmHotkey"/>) schreiben, Agent über den Autostart neu starten.
    /// </summary>
    /// <param name="version">
    /// ConfigVersion der geschriebenen Datei. Muss über der AppliedConfigVersion beider Geräte liegen,
    /// sobald vorher schon Config-Sync lief, sonst gilt die Datei bei einem späteren Abgleich als veraltet.
    /// </param>
    public static async Task BereiteAlarmprofilVorAsync(Guid sender, Guid empfaenger, int version = 1000)
    {
        PruefeLizenz();
        HaelpMiApp.BeendeAlles();

        var config = new SharedConfig { ConfigVersion = version };
        config.AlarmProfiles.Add(new AlarmProfile
        {
            Id = ProfilId,
            Name = "E2E-Alarm",
            Text = "E2E-Alarm A an B",
            Hotkey = AlarmHotkey,
            ResponseThreshold = 1,
            RecipientAssignments =
            {
                new RecipientAssignment
                {
                    Sender = new EntityRef(EntityKind.Device, sender),
                    Recipients = { new EntityRef(EntityKind.Device, empfaenger) },
                },
            },
        });
        new SharedConfigStore().Save(config);

        await HaelpMiApp.StarteAgentAsync();
        await Task.Delay(AgentAnlaufzeit);
    }

    /// <summary>Ohne gültige Lizenz gilt für User-Geräte Kontingent 0 (LicenseLimitGuard), B könnte weder senden noch empfangen.</summary>
    private static void PruefeLizenz()
    {
        var ergebnis = Lizenz.Status();
        Assert.True(ergebnis.Status == LicenseStatus.Valid,
            $"Lizenzstatus {ergebnis.Status} statt Valid. Der Workflow erzeugt pro Lauf eine Test-Kundengruppe samt Lizenz " +
            "(HAELPMI_E2E_LIZENZ); ohne sie sperrt LicenseLimitGuard das User-Gerät B.");
    }
}
