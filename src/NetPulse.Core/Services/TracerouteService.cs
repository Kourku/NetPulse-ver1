using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class TracerouteService
{
    private static readonly ConcurrentDictionary<string, string> HostnameCache = new();
    private static readonly ConcurrentDictionary<string, (string Asn, string Org)> AsnCache = new();

    public async Task<RoutePath> TraceRouteAsync(
        string destinationHost,
        int maxHops = 30,
        int timeoutMs = 1500,
        IProgress<RouteHop>? progress = null,
        CancellationToken ct = default)
    {
        var path = new RoutePath
        {
            Destination = destinationHost,
            Timestamp = DateTime.UtcNow
        };

        IPAddress targetIp;
        try
        {
            if (IPAddress.TryParse(destinationHost, out var parsedIp))
            {
                targetIp = parsedIp;
            }
            else
            {
                var addresses = await Dns.GetHostAddressesAsync(destinationHost, ct);
                targetIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                           ?? addresses[0];
            }
            path.DestinationIp = targetIp.ToString();
        }
        catch (Exception)
        {
            return path;
        }

        byte[] pingBuffer = new byte[32];
        new Random(42).NextBytes(pingBuffer);

        double previousLatency = 0;

        for (int ttl = 1; ttl <= maxHops; ttl++)
        {
            if (ct.IsCancellationRequested) break;

            using var ping = new Ping();
            var options = new PingOptions(ttl, true);
            var sw = Stopwatch.StartNew();

            RouteHop hop = new RouteHop
            {
                HopNumber = ttl,
                SentCount = 1
            };

            try
            {
                var reply = await ping.SendPingAsync(targetIp, timeoutMs, pingBuffer, options);
                sw.Stop();

                double rtt = Math.Max(0.1, sw.Elapsed.TotalMilliseconds);
                if (reply.RoundtripTime > 0 && Math.Abs(sw.Elapsed.TotalMilliseconds - reply.RoundtripTime) > 40)
                {
                    rtt = reply.RoundtripTime;
                }

                if (reply.Status == IPStatus.TtlExpired || reply.Status == IPStatus.Success)
                {
                    hop.IpAddress = reply.Address.ToString();
                    hop.CurrentRttMs = rtt;
                    hop.AvgRttMs = rtt;
                    hop.MinRttMs = rtt;
                    hop.MaxRttMs = rtt;
                    hop.ReceivedCount = 1;
                    hop.LossPercent = 0;
                    hop.IsResponding = true;
                    hop.LatencyDeltaFromPrevious = previousLatency > 0 ? (rtt - previousLatency) : 0;
                    previousLatency = rtt;

                    // Resolve Hostname & ASN asynchronously
                    ResolveHopMetadata(hop);

                    path.Hops.Add(hop);
                    progress?.Report(hop);

                    if (reply.Status == IPStatus.Success || reply.Address.Equals(targetIp))
                    {
                        path.DestinationReached = true;
                        path.TotalLatencyMs = rtt;
                        break;
                    }
                }
                else
                {
                    hop.IpAddress = "*";
                    hop.IsResponding = false;
                    hop.LossPercent = 100;
                    hop.CurrentRttMs = 0;
                    path.Hops.Add(hop);
                    progress?.Report(hop);
                }
            }
            catch (Exception)
            {
                sw.Stop();
                hop.IpAddress = "*";
                hop.IsResponding = false;
                hop.LossPercent = 100;
                path.Hops.Add(hop);
                progress?.Report(hop);
            }
        }

        // Post-process hop analysis for persistent latency spikes
        AnalyzePersistentLatency(path.Hops);

        return path;
    }

    public static void AnalyzePersistentLatency(List<RouteHop> hops)
    {
        for (int i = 0; i < hops.Count; i++)
        {
            var current = hops[i];
            if (!current.IsResponding) continue;

            // Check if there is a jump > 20ms from previous hop
            if (current.LatencyDeltaFromPrevious >= 20.0)
            {
                // Verify whether this increase persists to subsequent responding hops
                bool persists = true;
                int subsequentCount = 0;
                for (int j = i + 1; j < hops.Count; j++)
                {
                    if (hops[j].IsResponding)
                    {
                        subsequentCount++;
                        if (hops[j].CurrentRttMs < current.CurrentRttMs - 15.0)
                        {
                            persists = false;
                            break;
                        }
                    }
                }

                current.IsPersistentLatencyIncrease = persists && subsequentCount > 0;
            }
        }
    }

    private static void ResolveHopMetadata(RouteHop hop)
    {
        if (string.IsNullOrEmpty(hop.IpAddress) || hop.IpAddress == "*") return;

        // Check private IP ranges
        if (IsPrivateIp(hop.IpAddress))
        {
            hop.Hostname = hop.HopNumber == 1 ? "router.local (LAN Gateway)" : "internal.net (Private Subnet)";
            hop.AsNumber = "LAN";
            hop.AsOrg = "Local Network";
            return;
        }

        if (IsCgnatIp(hop.IpAddress))
        {
            hop.Hostname = "cgnat.isp.net (Carrier-Grade NAT)";
            hop.AsNumber = "CGNAT";
            hop.AsOrg = "ISP Access Network";
            return;
        }

        // Try Hostname Cache
        if (HostnameCache.TryGetValue(hop.IpAddress, out var cachedName))
        {
            hop.Hostname = cachedName;
        }
        else
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var entry = await Dns.GetHostEntryAsync(hop.IpAddress);
                    hop.Hostname = entry.HostName;
                    HostnameCache[hop.IpAddress] = entry.HostName;
                }
                catch
                {
                    HostnameCache[hop.IpAddress] = string.Empty;
                }
            });
        }

        // Try ASN Cache
        if (AsnCache.TryGetValue(hop.IpAddress, out var cachedAsn))
        {
            hop.AsNumber = cachedAsn.Asn;
            hop.AsOrg = cachedAsn.Org;
        }
        else
        {
            var known = IdentifyWellKnownAsn(hop.IpAddress);
            if (known.HasValue)
            {
                hop.AsNumber = known.Value.Asn;
                hop.AsOrg = known.Value.Org;
                AsnCache[hop.IpAddress] = known.Value;
            }
        }
    }

    private static bool IsPrivateIp(string ipStr)
    {
        if (!IPAddress.TryParse(ipStr, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return false;

        // 10.0.0.0/8
        if (bytes[0] == 10) return true;
        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168) return true;
        // 127.0.0.0/8
        if (bytes[0] == 127) return true;

        return false;
    }

    private static bool IsCgnatIp(string ipStr)
    {
        if (!IPAddress.TryParse(ipStr, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return false;

        // 100.64.0.0/10 (100.64.0.0 to 100.127.255.255)
        return bytes[0] == 100 && (bytes[1] >= 64 && bytes[1] <= 127);
    }

    private static (string Asn, string Org)? IdentifyWellKnownAsn(string ipStr)
    {
        if (ipStr.StartsWith("1.1.") || ipStr.StartsWith("1.0.") || ipStr.StartsWith("104.16.") || ipStr.StartsWith("172.67."))
            return ("AS13335", "Cloudflare");
        if (ipStr.StartsWith("8.8.") || ipStr.StartsWith("8.34.") || ipStr.StartsWith("142.250.") || ipStr.StartsWith("172.217."))
            return ("AS15169", "Google");
        if (ipStr.StartsWith("9.9.9.") || ipStr.StartsWith("149.112."))
            return ("AS19281", "Quad9");
        if (ipStr.StartsWith("208.67."))
            return ("AS36692", "Cisco OpenDNS");
        if (ipStr.StartsWith("13.107.") || ipStr.StartsWith("20.190.") || ipStr.StartsWith("40.126."))
            return ("AS8075", "Microsoft");
        if (ipStr.StartsWith("151.101."))
            return ("AS54113", "Fastly");
        if (ipStr.StartsWith("52.") || ipStr.StartsWith("54.") || ipStr.StartsWith("13."))
            return ("AS16509", "Amazon AWS");
        if (ipStr.StartsWith("4.68.") || ipStr.StartsWith("4.69.") || ipStr.StartsWith("128.242."))
            return ("AS3356", "Lumen / Level3");
        if (ipStr.StartsWith("154.54.") || ipStr.StartsWith("130.117."))
            return ("AS174", "Cogent");

        return null;
    }
}
