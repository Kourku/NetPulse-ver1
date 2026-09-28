using System.Collections.Generic;

namespace NetPulse.Core.Models;

public class NetworkInterfaceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InterfaceType { get; set; } = string.Empty; // Ethernet, Wireless80211, etc.
    public string OperationalStatus { get; set; } = string.Empty; // Up, Down
    public long SpeedBps { get; set; } // Link speed in bits/second
    public string SpeedDisplay { get; set; } = string.Empty; // e.g. "1 Gbps", "1200 Mbps"
    public string Ipv4Address { get; set; } = string.Empty;
    public string Ipv4Subnet { get; set; } = string.Empty;
    public string Ipv4Gateway { get; set; } = string.Empty;
    public string Ipv6Address { get; set; } = string.Empty;
    public string Ipv6Gateway { get; set; } = string.Empty;
    public List<string> DnsServers { get; set; } = new();

    // Wi-Fi specific information
    public bool IsWifi { get; set; }
    public string? WifiSsid { get; set; }
    public int? WifiSignalPercent { get; set; }
    public string? WifiRadioType { get; set; } // 802.11ax, 802.11ac, etc.
    public int? WifiChannel { get; set; }
    public string? WifiBssid { get; set; }
    public string? WifiReceiveRate { get; set; }
    public string? WifiTransmitRate { get; set; }
}
