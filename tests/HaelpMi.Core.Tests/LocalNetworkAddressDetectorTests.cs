using System.Net;
using System.Net.Sockets;
using HaelpMi.Core.Networking;
using Xunit;

namespace HaelpMi.Core.Tests;

/// <summary>
/// Maschinenabhängig - welche Adressen tatsächlich zurückkommen, hängt von den NICs der
/// jeweiligen Testmaschine ab, deshalb nur Filterkorrektheit prüfbar, keine konkreten Werte.
/// </summary>
public class LocalNetworkAddressDetectorTests
{
    [Fact]
    public void GetCandidateAddresses_DoesNotThrow_AndReturnsOnlyPlausibleIPv4()
    {
        var candidates = LocalNetworkAddressDetector.GetCandidateAddresses();

        Assert.All(candidates, address =>
        {
            Assert.True(IPAddress.TryParse(address, out var parsed));
            Assert.Equal(AddressFamily.InterNetwork, parsed!.AddressFamily);
            Assert.False(IPAddress.IsLoopback(parsed));
            Assert.False(address.StartsWith("169.254."));
        });
    }

    [Fact]
    public void GetCandidateAddresses_NoDuplicates()
    {
        var candidates = LocalNetworkAddressDetector.GetCandidateAddresses();
        Assert.Equal(candidates.Distinct().Count(), candidates.Count);
    }
}
