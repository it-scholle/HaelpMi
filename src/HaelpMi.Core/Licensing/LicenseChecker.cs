using HaelpMi.Core.Storage;

namespace HaelpMi.Core.Licensing;

/// <summary>
/// Nur vom Agent verwendet (24/7-Prozess, einziger AuditLog-Schreiber pro Gerät - siehe
/// AuditLog-Klassendoku, Hash-Chain ohne Cross-Process-Lock). Config macht ihre eigene,
/// leichtere Prüfung ohne AuditLog/Throttle-Persistenz (siehe HaelpMi.Config/App.xaml.cs).
///
/// Drosselt wiederholte Benachrichtigungen über <see cref="OwnSettings.LicenseLastNotifiedStage"/>
/// (gleiches Muster wie AppliedConfigVersion) - nur ein Stufenwechsel (rauf oder runter) ist
/// meldenswert, ein unveränderter Zustand bei jedem der alle 6h laufenden Checks würde sonst
/// den Admin zuspammen.
///
/// Äußerstes catch (Exception) als zweite Sicherung neben LicenseFileLoader - diese Klasse
/// darf unter keinen Umständen etwas werfen, das den Agent-Prozess stören könnte.
/// </summary>
public sealed class LicenseChecker
{
    private readonly SettingsStore _settingsStore;
    private readonly Action<string>? _auditLogAppend;

    public LicenseChecker(SettingsStore settingsStore, Action<string>? auditLogAppend = null)
    {
        _settingsStore = settingsStore;
        _auditLogAppend = auditLogAppend;
    }

    public (LicenseCheckResult Result, bool ShouldNotify) CheckOnce(DateOnly todayUtc)
    {
        try
        {
            var result = LicenseEvaluator.Evaluate(LicenseFileLoader.LoadAndVerify(), todayUtc);
            var settings = _settingsStore.Load();
            var previousStage = settings.LicenseLastNotifiedStage;

            if (result.StageIndex == previousStage)
            {
                return (result, false);
            }

            settings.LicenseLastNotifiedStage = result.StageIndex;
            _settingsStore.Save(settings);

            if (result.StageIndex > previousStage)
            {
                _auditLogAppend?.Invoke($"Lizenz-Warnstufe erreicht: Stufe {result.StageIndex} ({result.Standing})");
                return (result, true);
            }

            // Rückstufung (z. B. neue/verlängerte Lizenz eingespielt) - meldenswert fürs
            // Audit-Log, aber kein erneuter Balloon nötig ("gut" braucht keine Warnung).
            _auditLogAppend?.Invoke("Lizenz wieder in Ordnung (erneuert/repariert).");
            return (result, false);
        }
        catch (Exception)
        {
            return (new LicenseCheckResult(LicenseStanding.Good, null, null, null, 0), false);
        }
    }
}
