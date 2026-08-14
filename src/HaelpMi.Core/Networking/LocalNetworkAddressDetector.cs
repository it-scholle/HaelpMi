using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HaelpMi.Core.Networking;

/// <summary>
/// Vorschlagsliste für den "Automatisch erkennen"-Knopf im Admin-Dashboard Netzwerk-Tab
/// (Nutzerwunsch 15.08.2026): liest ausschließlich lokale NIC-Zustände, ändert nichts am
/// System. Bewusst nur ein Vorschlag, keine Silent-Auto-Übernahme - bei mehreren
/// NICs/VPN-/virtuellen Adaptern könnte sonst unbemerkt eine falsche Adresse in die
/// Bridge-Seed-Liste landen; der Admin bestätigt jeden Vorschlag per Klick
/// (siehe AdminDashboardWindow). Bewusst nicht im Install-Creator verwendet - der läuft
/// laut Nutzer immer auf einer eigenen Dev-Maschine, nie im künftigen Kundennetz, eine
/// Erkennung dort würde strukturell die falsche Adresse vorschlagen.
/// </summary>
public static class LocalNetworkAddressDetector
{
    /// <summary>
    /// IPv4-Unicast-Adressen aller aktiven, nicht-Loopback-Schnittstellen, ohne
    /// APIPA/Link-Local (169.254.0.0/16 - eine fehlgeschlagene DHCP-Selbstzuweisung ist nie
    /// ein sinnvoller Bridge-Seed-Vorschlag). Reihenfolge nicht garantiert, keine Duplikate.
    /// </summary>
    public static IReadOnlyList<string> GetCandidateAddresses()
    {
        var result = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(address) ||
                    IsLinkLocal(address))
                {
                    continue;
                }

                var text = address.ToString();
                if (!result.Contains(text))
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}
