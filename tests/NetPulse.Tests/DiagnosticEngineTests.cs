using System.Collections.Generic;
using NetPulse.Core.Models;
using NetPulse.Core.Services;
using Xunit;

namespace NetPulse.Tests;

public class DiagnosticEngineTests
{
    private readonly DiagnosticEngine _engine = new();

    private NetworkInterfaceInfo CreateHealthyNic() => new()
    {
        Name = "Ethernet 1",
        OperationalStatus = "Up",
        InterfaceType = "Ethernet",
        Ipv4Address = "192.168.1.100",
        Ipv4Gateway = "192.168.1.1",
        IsWifi = false
    };

    [Fact]
    public void TestDisconnectedInterface()
    {
        var nic = new NetworkInterfaceInfo
        {
            Name = "Wi-Fi",
            OperationalStatus = "Down"
        };

        var diagnosis = _engine.Analyze(nic, null, new List<TargetMetrics>(), null);

        Assert.Equal(DiagnosticCategory.Disconnected, diagnosis.Category);
        Assert.Equal(DiagnosticStatus.Critical, diagnosis.Status);
    }

    [Fact]
    public void TestLocalNetworkIssueDetected()
    {
        var nic = CreateHealthyNic();

        var gateway = new TargetMetrics
        {
            TargetId = "gw",
            TargetName = "Gateway",
            IsGateway = true,
            CurrentLatency = 45.0, // High for Ethernet
            PacketLossPercent = 12.0,
            TotalProbes = 50,
            IsReachable = true
        };

        var internet = new List<TargetMetrics>
        {
            new() { TargetId = "cf", TargetName = "Cloudflare", CurrentLatency = 60.0, PacketLossPercent = 10.0, TotalProbes = 50 }
        };

        var diagnosis = _engine.Analyze(nic, gateway, internet, null);

        Assert.Equal(DiagnosticCategory.LocalNetworkIssue, diagnosis.Category);
        Assert.Contains("local network", diagnosis.Headline.ToLowerInvariant());
    }

    [Fact]
    public void TestIspInternetIssueDetected()
    {
        var nic = CreateHealthyNic();

        var gateway = new TargetMetrics
        {
            TargetId = "gw",
            TargetName = "Gateway",
            IsGateway = true,
            CurrentLatency = 1.2, // Pristine router
            PacketLossPercent = 0.0,
            TotalProbes = 50,
            IsReachable = true
        };

        var internet = new List<TargetMetrics>
        {
            new() { TargetId = "cf", TargetName = "Cloudflare", CurrentLatency = 140.0, PacketLossPercent = 15.0, TotalProbes = 50, IsReachable = true },
            new() { TargetId = "gg", TargetName = "Google", CurrentLatency = 135.0, PacketLossPercent = 12.0, TotalProbes = 50, IsReachable = true }
        };

        var diagnosis = _engine.Analyze(nic, gateway, internet, null);

        Assert.Equal(DiagnosticCategory.IspInternetIssue, diagnosis.Category);
        Assert.Contains("upstream", diagnosis.Headline.ToLowerInvariant());
    }

    [Fact]
    public void TestDnsIssueDetected()
    {
        var nic = CreateHealthyNic();

        var gateway = new TargetMetrics
        {
            TargetId = "gw",
            TargetName = "Gateway",
            IsGateway = true,
            CurrentLatency = 1.0,
            PacketLossPercent = 0.0,
            TotalProbes = 50,
            IsReachable = true
        };

        var internet = new List<TargetMetrics>
        {
            new() { TargetId = "cf", TargetName = "Cloudflare", CurrentLatency = 15.0, PacketLossPercent = 0.0, TotalProbes = 50, IsReachable = true },
            new() { TargetId = "gg", TargetName = "Google", CurrentLatency = 16.0, PacketLossPercent = 0.0, TotalProbes = 50, IsReachable = true }
        };

        var dns = new TargetMetrics
        {
            TargetId = "dns",
            TargetName = "DNS",
            IsDnsResolver = true,
            CurrentLatency = 350.0, // High DNS latency
            PacketLossPercent = 30.0,
            TotalProbes = 20,
            IsReachable = true
        };

        var diagnosis = _engine.Analyze(nic, gateway, internet, dns);

        Assert.Equal(DiagnosticCategory.DnsIssue, diagnosis.Category);
        Assert.Contains("dns", diagnosis.Headline.ToLowerInvariant());
    }

    [Fact]
    public void TestTargetSpecificIssueDetected()
    {
        var nic = CreateHealthyNic();

        var gateway = new TargetMetrics
        {
            TargetId = "gw",
            TargetName = "Gateway",
            IsGateway = true,
            CurrentLatency = 1.0,
            PacketLossPercent = 0.0,
            TotalProbes = 50,
            IsReachable = true
        };

        var internet = new List<TargetMetrics>
        {
            new() { TargetId = "cf", TargetName = "Cloudflare", CurrentLatency = 15.0, PacketLossPercent = 0.0, TotalProbes = 50, IsReachable = true },
            new() { TargetId = "gg", TargetName = "Google", CurrentLatency = 16.0, PacketLossPercent = 0.0, TotalProbes = 50, IsReachable = true },
            new() { TargetId = "custom", TargetName = "Game Server", CurrentLatency = 250.0, PacketLossPercent = 40.0, TotalProbes = 50, IsReachable = true }
        };

        var diagnosis = _engine.Analyze(nic, gateway, internet, null);

        Assert.Equal(DiagnosticCategory.TargetSpecificIssue, diagnosis.Category);
        Assert.Contains("Game Server", diagnosis.Headline);
    }

    [Fact]
    public void TestBufferbloatCongestionDetected()
    {
        var nic = CreateHealthyNic();

        var gateway = new TargetMetrics
        {
            TargetId = "gw",
            TargetName = "Gateway",
            IsGateway = true,
            CurrentLatency = 1.0,
            TotalProbes = 50,
            IsReachable = true
        };

        var internet = new List<TargetMetrics>
        {
            new() { TargetId = "cf", TargetName = "Cloudflare", CurrentLatency = 15.0, TotalProbes = 50, IsReachable = true }
        };

        var bufferbloat = new BufferbloatResult
        {
            Grade = "F",
            BaselineLatencyMs = 15.0,
            DownloadLoadedLatencyMs = 195.0,
            DownloadDeltaMs = 180.0
        };

        var diagnosis = _engine.Analyze(nic, gateway, internet, null, bufferbloat);

        Assert.Equal(DiagnosticCategory.BufferbloatCongestion, diagnosis.Category);
        Assert.Contains("bufferbloat", diagnosis.Headline.ToLowerInvariant());
    }

    [Fact]
    public void TestOptimalConnection()
    {
        var nic = CreateHealthyNic();

        var gateway = new TargetMetrics
        {
            TargetId = "gw",
            TargetName = "Gateway",
            IsGateway = true,
            CurrentLatency = 1.0,
            PacketLossPercent = 0.0,
            TotalProbes = 50,
            IsReachable = true
        };

        var internet = new List<TargetMetrics>
        {
            new() { TargetId = "cf", TargetName = "Cloudflare", CurrentLatency = 15.0, PacketLossPercent = 0.0, Jitter = 1.2, TotalProbes = 50, IsReachable = true },
            new() { TargetId = "gg", TargetName = "Google", CurrentLatency = 16.0, PacketLossPercent = 0.0, Jitter = 1.5, TotalProbes = 50, IsReachable = true }
        };

        var diagnosis = _engine.Analyze(nic, gateway, internet, null);

        Assert.Equal(DiagnosticCategory.Optimal, diagnosis.Category);
        Assert.Equal(DiagnosticStatus.Healthy, diagnosis.Status);
    }
}
