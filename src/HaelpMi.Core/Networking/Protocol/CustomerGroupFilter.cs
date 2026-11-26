using HaelpMi.Core.Diagnostics;

namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Every network message carries a CustomerGroupId (Teil 2, Abschnitt 6); every
/// receiving service calls this before doing anything else with a message. A mismatch
/// is dropped without any reply - two independent HälpMi deployments sharing a physical
/// network must never be able to detect each other's existence. Locally the drop is
/// reported throttled to the Betriebs-Log (<see cref="FremdeKundengruppeMelder"/>), never
/// to the audit log.
/// </summary>
public static class CustomerGroupFilter
{
    private static bool Matches(Guid messageCustomerGroupId, Guid ownCustomerGroupId) =>
        messageCustomerGroupId == ownCustomerGroupId;

    /// <param name="nachrichtenart">Feste Bezeichnung für das Betriebs-Log, z. B. „Alarm“.</param>
    /// <param name="absenderGeraetId">Geräte-ID des Absenders, falls die Nachricht eine trägt.</param>
    public static bool Matches(Guid messageCustomerGroupId, Guid ownCustomerGroupId, string nachrichtenart, Guid? absenderGeraetId)
    {
        if (Matches(messageCustomerGroupId, ownCustomerGroupId))
        {
            return true;
        }

        FremdeKundengruppeMelder.Prozess.Verworfen(nachrichtenart, messageCustomerGroupId, absenderGeraetId);
        return false;
    }
}
