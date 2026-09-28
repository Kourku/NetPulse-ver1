using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class DnsProbeService
{
    private static readonly string[] TestDomains = new[]
    {
        "cloudflare.com",
        "google.com",
        "microsoft.com",
        "wikipedia.org",
        "amazon.com"
    };

    private static int _domainIndex;

    public async Task<Measurement> ProbeAsync(TargetConfig target, int timeoutMs = 2500, CancellationToken ct = default)
    {
        var measurement = new Measurement
        {
            Timestamp = DateTime.UtcNow,
            TargetId = target.Id,
            TargetName = target.Name,
            TargetHost = target.Host,
            Protocol = NetworkProtocol.Dns
        };

        string domainToResolve = target.Host;
        if (IPAddress.TryParse(target.Host, out var dnsServerIp))
        {
            string testDomain = TestDomains[Math.Abs(Interlocked.Increment(ref _domainIndex)) % TestDomains.Length];
            return await ProbeDirectDnsServerAsync(target, dnsServerIp, testDomain, timeoutMs, ct);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            var addresses = await Dns.GetHostAddressesAsync(domainToResolve, cts.Token);
            sw.Stop();

            if (addresses.Length > 0)
            {
                measurement.Success = true;
                measurement.RttMs = Math.Max(0.1, sw.Elapsed.TotalMilliseconds);
                measurement.ResolvedIp = addresses[0].ToString();
            }
            else
            {
                measurement.Success = false;
                measurement.ErrorMessage = "No DNS records returned";
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            measurement.Success = false;
            measurement.ErrorMessage = "DNS resolution timed out";
        }
        catch (Exception ex)
        {
            sw.Stop();
            measurement.Success = false;
            measurement.ErrorMessage = ex.GetBaseException().Message;
        }

        return measurement;
    }

    public async Task<Measurement> ProbeDirectDnsServerAsync(
        TargetConfig target,
        IPAddress dnsServerIp,
        string domainToResolve,
        int timeoutMs = 2500,
        CancellationToken ct = default)
    {
        var measurement = new Measurement
        {
            Timestamp = DateTime.UtcNow,
            TargetId = target.Id,
            TargetName = target.Name,
            TargetHost = target.Host,
            Protocol = NetworkProtocol.Dns
        };

        using var udpClient = new UdpClient();
        var sw = Stopwatch.StartNew();

        try
        {
            byte[] query = BuildDnsQuery(domainToResolve);
            var endpoint = new IPEndPoint(dnsServerIp, 53);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            sw.Restart();
            await udpClient.SendAsync(query, query.Length, endpoint);
            var receiveTask = udpClient.ReceiveAsync(cts.Token).AsTask();
            var result = await receiveTask;
            sw.Stop();

            if (result.Buffer.Length > 12)
            {
                measurement.Success = true;
                measurement.RttMs = Math.Max(0.1, sw.Elapsed.TotalMilliseconds);
                measurement.ResolvedIp = ParseDnsAnswerIp(result.Buffer);
            }
            else
            {
                measurement.Success = false;
                measurement.ErrorMessage = "Invalid DNS response format";
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            measurement.Success = false;
            measurement.ErrorMessage = "DNS server timed out";
        }
        catch (Exception ex)
        {
            sw.Stop();
            measurement.Success = false;
            measurement.ErrorMessage = ex.GetBaseException().Message;
        }

        return measurement;
    }

    private static byte[] BuildDnsQuery(string domain)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        ushort txId = (ushort)Random.Shared.Next(1, 65535);
        bw.Write((byte)(txId >> 8));
        bw.Write((byte)(txId & 0xFF));

        // Flags: 0x0100 (Standard Query, Recursion Desired)
        bw.Write((byte)0x01);
        bw.Write((byte)0x00);

        // QDCOUNT: 1
        bw.Write((byte)0x00);
        bw.Write((byte)0x01);

        // ANCOUNT, NSCOUNT, ARCOUNT: 0
        bw.Write(new byte[6]);

        // QNAME: length-prefixed labels
        foreach (var label in domain.Split('.'))
        {
            byte[] bytes = Encoding.ASCII.GetBytes(label);
            bw.Write((byte)bytes.Length);
            bw.Write(bytes);
        }
        bw.Write((byte)0x00); // null terminator

        // QTYPE: 1 (A Record)
        bw.Write((byte)0x00);
        bw.Write((byte)0x01);

        // QCLASS: 1 (IN)
        bw.Write((byte)0x00);
        bw.Write((byte)0x01);

        return ms.ToArray();
    }

    private static string? ParseDnsAnswerIp(byte[] response)
    {
        try
        {
            if (response.Length >= 16)
            {
                int rdataLen = (response[^6] << 8) | response[^5];
                if (rdataLen == 4)
                {
                    return new IPAddress(response[^4..]).ToString();
                }
            }
        }
        catch { }
        return null;
    }
}
