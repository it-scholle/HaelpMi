namespace HaelpMi.Core.Licensing;

/// <summary>Warum ein Gerät weder senden noch empfangen darf - bestimmt den Hinweistext auf Geräte- und Admin-Seite.</summary>
public enum LicenseBlockReason
{
    None,
    ManuallyDisabled,
    NoLicense,
    LimitExceeded,
}
