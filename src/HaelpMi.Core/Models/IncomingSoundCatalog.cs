namespace HaelpMi.Core.Models;

/// <summary>
/// Fixed catalog of alarm tones a device can pick from for incoming alarms (FR-12).
/// Tones are synthesized on the fly (see Audio/AlarmToneGenerator in this project) as
/// simple beep patterns rather than shipping bundled binary .wav assets.
/// </summary>
public static class IncomingSoundCatalog
{
    public sealed record Option(string Id, string DisplayName, int[] BeepFrequenciesHz, int BeepDurationMs, int GapMs, int RepeatCount);

    public const string DefaultId = "ton-1";

    public static readonly IReadOnlyList<Option> Options = new[]
    {
        new Option("ton-1", "Ton 1 (kurze Pieptöne)", new[] { 880 }, 180, 120, 4),
        new Option("ton-2", "Ton 2 (Zweiklang)", new[] { 660, 990 }, 220, 90, 3),
        new Option("ton-3", "Ton 3 (Sirene, lang)", new[] { 520, 780, 520, 780 }, 260, 40, 3),
    };

    public static Option Resolve(string id) =>
        Options.FirstOrDefault(o => o.Id == id) ?? Options[0];
}
