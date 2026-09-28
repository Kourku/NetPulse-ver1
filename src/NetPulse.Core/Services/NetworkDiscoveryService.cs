using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class NetworkDiscoveryService
{
    public event EventHandler<NetworkInterfaceInfo>? NetworkChanged;

    public NetworkDiscoveryService()
    {
        try
        {
            NetworkChange.NetworkAddressChanged += (s, e) => OnNetworkChanged();
            NetworkChange.NetworkAvailabilityChanged += (s, e) => OnNetworkChanged();
        }
        catch
        {
            // Ignore if running in restricted sandbox
        }
    }

    private void OnNetworkChanged()
    {
        try
        {
            var info = GetActiveInterfaceInfo();
            NetworkChanged?.Invoke(this, info);
        }
        catch
        {
            // suppress background event errors
        }
    }

    public NetworkInterfaceInfo GetActiveInterfaceInfo()
    {
        var result = new NetworkInterfaceInfo
        {
            Name = "Unknown",
            Description = "No active interface found",
            OperationalStatus = "Down",
            InterfaceType = "None"
        };

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .ToList();

            // Find the interface with a valid IPv4 default gateway
            NetworkInterface? activeNic = null;
            IPAddress? activeGateway = null;

            foreach (var nic in interfaces)
            {
                var props = nic.GetIPProperties();
                var gw = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                         !g.Address.Equals(IPAddress.Any) &&
                                         !g.Address.Equals(IPAddress.None));

                if (gw != null)
                {
                    activeNic = nic;
                    activeGateway = gw.Address;
                    break;
                }
            }

            // Fallback to first up interface if no gateway found
            if (activeNic == null && interfaces.Count > 0)
            {
                activeNic = interfaces[0];
            }

            if (activeNic != null)
            {
                result.Id = activeNic.Id;
                result.Name = activeNic.Name;
                result.Description = activeNic.Description;
                result.InterfaceType = activeNic.NetworkInterfaceType.ToString();
                result.OperationalStatus = activeNic.OperationalStatus.ToString();
                result.SpeedBps = activeNic.Speed;
                result.SpeedDisplay = FormatSpeed(activeNic.Speed);
                result.IsWifi = activeNic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                var ipProps = activeNic.GetIPProperties();

                // IPv4 & Subnet
                var ipv4Info = ipProps.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4Info != null)
                {
                    result.Ipv4Address = ipv4Info.Address.ToString();
                    result.Ipv4Subnet = ipv4Info.IPv4Mask?.ToString() ?? "255.255.255.0";
                }

                // IPv6
                var ipv6Info = ipProps.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                         !u.Address.IsIPv6LinkLocal);
                if (ipv6Info != null)
                {
                    result.Ipv6Address = ipv6Info.Address.ToString();
                }

                // Gateway
                if (activeGateway != null)
                {
                    result.Ipv4Gateway = activeGateway.ToString();
                }
                else
                {
                    var gwIpv4 = ipProps.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (gwIpv4 != null) result.Ipv4Gateway = gwIpv4.Address.ToString();
                }

                var gwIpv6 = ipProps.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetworkV6);
                if (gwIpv6 != null) result.Ipv6Gateway = gwIpv6.Address.ToString();

                // DNS
                foreach (var dns in ipProps.DnsAddresses)
                {
                    if (dns.AddressFamily == AddressFamily.InterNetwork ||
                        (dns.AddressFamily == AddressFamily.InterNetworkV6 && !dns.IsIPv6LinkLocal))
                    {
                        result.DnsServers.Add(dns.ToString());
                    }
                }

                // If Wi-Fi, query netsh wlan show interfaces
                if (result.IsWifi || result.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                    result.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsWifi = true;
                    PopulateWifiDetails(result);
                }
            }
        }
        catch (Exception ex)
        {
            result.Description = $"Error reading interfaces: {ex.Message}";
        }

        return result;
    }

    private static void PopulateWifiDetails(NetworkInterfaceInfo info)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);

            var ssidMatch = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            if (ssidMatch.Success) info.WifiSsid = ssidMatch.Groups[1].Value.Trim();

            var signalMatch = Regex.Match(output, @"^\s*Signal\s*:\s*(\d+)%", RegexOptions.Multiline);
            if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out int sig))
            {
                info.WifiSignalPercent = sig;
            }

            var radioMatch = Regex.Match(output, @"^\s*Radio type\s*:\s*(.+)$", RegexOptions.Multiline);
            if (radioMatch.Success) info.WifiRadioType = radioMatch.Groups[1].Value.Trim();

            var channelMatch = Regex.Match(output, @"^\s*Channel\s*:\s*(\d+)", RegexOptions.Multiline);
            if (channelMatch.Success && int.TryParse(channelMatch.Groups[1].Value, out int ch))
            {
                info.WifiChannel = ch;
            }

            var bssidMatch = Regex.Match(output, @"^\s*BSSID\s*:\s*(.+)$", RegexOptions.Multiline);
            if (bssidMatch.Success) info.WifiBssid = bssidMatch.Groups[1].Value.Trim();

            var rxMatch = Regex.Match(output, @"^\s*Receive rate \(Mbps\)\s*:\s*(.+)$", RegexOptions.Multiline);
            if (rxMatch.Success) info.WifiReceiveRate = rxMatch.Groups[1].Value.Trim();

            var txMatch = Regex.Match(output, @"^\s*Transmit rate \(Mbps\)\s*:\s*(.+)$", RegexOptions.Multiline);
            if (txMatch.Success) info.WifiTransmitRate = txMatch.Groups[1].Value.Trim();
        }
        catch
        {
            // netsh wlan might not be available if WLAN service is stopped
        }
    }

    private static string FormatSpeed(long speedBps)
    {
        if (speedBps <= 0) return "Unknown";
        if (speedBps >= 1_000_000_000)
            return $"{(double)speedBps / 1_000_000_000:0.##} Gbps";
        if (speedBps >= 1_000_000)
            return $"{(double)speedBps / 1_000_000:0.##} Mbps";
        if (speedBps >= 1_000)
            return $"{(double)speedBps / 1_000:0.##} Kbps";
        return $"{speedBps} bps";
    }
}
