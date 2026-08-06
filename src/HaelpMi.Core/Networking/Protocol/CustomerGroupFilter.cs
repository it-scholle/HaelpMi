namespace HaelpMi.Core.Networking.Protocol;

/// <summary>
/// Every network message carries a CustomerGroupId (Teil 2, Abschnitt 6); every
/// receiving service calls this before doing anything else with a message. A mismatch
/// is dropped silently - no log entry, no reply, nothing - by design: two independent
/// HälpMi deployments sharing a physical network must never be able to detect each
/// other's existence, not even via "I got a message I didn't understand" noise.
/// </summary>
public static class CustomerGroupFilter
{
    public static bool Matches(Guid messageCustomerGroupId, Guid ownCustomerGroupId) =>
        messageCustomerGroupId == ownCustomerGroupId;
}
