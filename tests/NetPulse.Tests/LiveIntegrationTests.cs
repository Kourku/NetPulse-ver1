using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NetPulse.Core.Models;
using NetPulse.Core.Services;
using Xunit;

namespace NetPulse.Tests;

public class LiveIntegrationTests
{
    [Fact]
    public void TestActiveNetworkDiscovery()
    {
        var discovery = new NetworkDiscoveryService();
        var nic = discovery.GetActiveInterfaceInfo();

        Assert.NotNull(nic);
        Assert.False(string.IsNullOrEmpty(nic.Name));
        Assert.False(string.IsNullOrEmpty(nic.Ipv4Address));
        Assert.False(string.IsNullOrEmpty(nic.Ipv4Gateway));
    }

    [Fact]
    public async Task TestPingGatewayAndInternet()
    {
        var discovery = new NetworkDiscoveryService();
        var nic = discovery.GetActiveInterfaceInfo();
        var ping = new PingService();

        // Gateway ping
        var gwTarget = new TargetConfig
        {
            Id = "gw",
            Name = "Gateway",
            Host = nic.Ipv4Gateway,
            Protocol = NetworkProtocol.Icmp
        };
        var gwResult = await ping.ProbeAsync(gwTarget, 2000);
        Assert.True(gwResult.Success);
        Assert.True(gwResult.RttMs >= 0.0);

        // Internet ping (1.1.1.1)
        var inetTarget = new TargetConfig
        {
            Id = "cf",
            Name = "Cloudflare",
            Host = "1.1.1.1",
            Protocol = NetworkProtocol.Icmp
        };
        var inetResult = await ping.ProbeAsync(inetTarget, 2000);
        Assert.True(inetResult.Success);
        Assert.True(inetResult.RttMs > 0.0);
    }

    [Fact]
    public async Task TestDnsResolution()
    {
        var dnsService = new DnsProbeService();
        var target = new TargetConfig
        {
            Id = "dns",
            Name = "DNS System",
            Host = "cloudflare.com",
            Protocol = NetworkProtocol.Dns
        };

        var result = await dnsService.ProbeAsync(target, 3000);
        Assert.True(result.Success);
        Assert.NotNull(result.ResolvedIp);
        Assert.True(result.RttMs > 0);
    }

    [Fact]
    public async Task TestHttpProbeBreakdown()
    {
        var httpService = new HttpProbeService();
        var target = new TargetConfig
        {
            Id = "https-test",
            Name = "Cloudflare HTTPS",
            Host = "cloudflare.com",
            Port = 443,
            Protocol = NetworkProtocol.Https
        };

        var result = await httpService.ProbeAsync(target, 5000);
        Assert.True(result.Success);
        Assert.NotNull(result.HttpsBreakdown);
        Assert.True(result.HttpsBreakdown.TotalMs > 0);
        Assert.True(result.HttpsBreakdown.DnsLookupMs >= 0);
        Assert.True(result.HttpsBreakdown.TcpConnectMs > 0);
        Assert.True(result.HttpsBreakdown.TlsHandshakeMs > 0);
    }

    [Fact]
    public async Task TestTracerouteLive()
    {
        var traceroute = new TracerouteService();
        var path = await traceroute.TraceRouteAsync("1.1.1.1", maxHops: 5, timeoutMs: 1500);

        Assert.NotNull(path);
        Assert.NotEmpty(path.Hops);
        Assert.True(path.Hops[0].HopNumber == 1);
        Assert.True(path.Hops[0].IsResponding);
    }

    [Fact]
    public async Task TestDiagnosticReportGeneration()
    {
        var discovery = new NetworkDiscoveryService();
        var nic = discovery.GetActiveInterfaceInfo();
        var engine = new DiagnosticEngine();

        var gwMetrics = new TargetMetrics
        {
            TargetId = "gw",
            TargetName = "Gateway",
            IsGateway = true,
            CurrentLatency = 1.2,
            TotalProbes = 10,
            IsReachable = true
        };

        var inetMetrics = new List<TargetMetrics>
        {
            new() { TargetId = "cf", TargetName = "Cloudflare", CurrentLatency = 14.5, TotalProbes = 10, IsReachable = true }
        };

        var diagnosis = engine.Analyze(nic, gwMetrics, inetMetrics, null);
        var storage = new StorageService();

        string report = await storage.GenerateDiagnosticReportAsync(
            nic,
            diagnosis,
            inetMetrics,
            new List<ConnectionEvent>());

        Assert.Contains("NetPulse Network Quality & Diagnostic Report", report);
        Assert.Contains("Active Network Interface", report);
        Assert.Contains("Real-Time Probing Statistics", report);

        string testPath = Path.Combine(Path.GetTempPath(), $"netpulse_test_report_{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(testPath, report);
        Assert.True(File.Exists(testPath));
        File.Delete(testPath);
    }
}
