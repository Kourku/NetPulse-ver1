using System.Collections.Generic;
using NetPulse.Core.Models;
using NetPulse.Core.Services;
using Xunit;

namespace NetPulse.Tests;

public class RouteAnalysisTests
{
    private readonly RouteAnalysisService _service = new();

    [Fact]
    public void TestPathComparisonDivergence()
    {
        var pathA = new RoutePath
        {
            Destination = "1.1.1.1",
            TotalLatencyMs = 24.0,
            Hops = new List<RouteHop>
            {
                new() { HopNumber = 1, IpAddress = "192.168.1.1", CurrentRttMs = 1.0 },
                new() { HopNumber = 2, IpAddress = "10.0.0.1", CurrentRttMs = 5.0 },
                new() { HopNumber = 3, IpAddress = "172.16.1.1", CurrentRttMs = 12.0 },
                new() { HopNumber = 4, IpAddress = "1.1.1.1", CurrentRttMs = 24.0 }
            }
        };

        var pathB = new RoutePath
        {
            Destination = "8.8.8.8",
            TotalLatencyMs = 38.0,
            Hops = new List<RouteHop>
            {
                new() { HopNumber = 1, IpAddress = "192.168.1.1", CurrentRttMs = 1.0 },
                new() { HopNumber = 2, IpAddress = "10.0.0.1", CurrentRttMs = 5.0 },
                new() { HopNumber = 3, IpAddress = "198.51.100.1", CurrentRttMs = 25.0 }, // Divergence
                new() { HopNumber = 4, IpAddress = "8.8.8.8", CurrentRttMs = 38.0 }
            }
        };

        var comparison = _service.ComparePaths(pathA, pathB);

        Assert.Equal(2, comparison.CommonHopsCount);
        Assert.Equal(3, comparison.DivergenceHopNumber);
        Assert.Equal("172.16.1.1", comparison.DivergencePointIp);
    }

    [Fact]
    public void TestPersistentLatencyAnomalyDetection()
    {
        var hops = new List<RouteHop>
        {
            new() { HopNumber = 1, IpAddress = "192.168.1.1", CurrentRttMs = 1.0, IsResponding = true },
            new() { HopNumber = 2, IpAddress = "10.0.0.1", CurrentRttMs = 5.0, LatencyDeltaFromPrevious = 4.0, IsResponding = true },
            new() { HopNumber = 3, IpAddress = "41.100.1.1", CurrentRttMs = 65.0, LatencyDeltaFromPrevious = 60.0, IsResponding = true }, // Sustained jump
            new() { HopNumber = 4, IpAddress = "41.100.1.2", CurrentRttMs = 66.0, LatencyDeltaFromPrevious = 1.0, IsResponding = true },
            new() { HopNumber = 5, IpAddress = "1.1.1.1", CurrentRttMs = 67.0, LatencyDeltaFromPrevious = 1.0, IsResponding = true }
        };

        TracerouteService.AnalyzePersistentLatency(hops);

        Assert.True(hops[2].IsPersistentLatencyIncrease);
        Assert.False(hops[1].IsPersistentLatencyIncrease);
    }

    [Fact]
    public void TestNonRespondingHopDistinction()
    {
        var hops = new List<RouteHop>
        {
            new() { HopNumber = 1, IpAddress = "192.168.1.1", CurrentRttMs = 1.0, IsResponding = true },
            new() { HopNumber = 2, IpAddress = "*", CurrentRttMs = 0.0, IsResponding = false }, // Rate-limiting router
            new() { HopNumber = 3, IpAddress = "1.1.1.1", CurrentRttMs = 15.0, IsResponding = true }
        };

        var path = new RoutePath
        {
            Destination = "1.1.1.1",
            DestinationReached = true,
            Hops = hops
        };

        var anomalies = _service.DetectAnomalies(path);

        Assert.Contains(anomalies, a => a.Contains("ICMP rate-limiting rather than packet drop"));
    }
}
