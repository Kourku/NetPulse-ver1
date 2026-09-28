using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class PathComparisonResult
{
    public string DestinationA { get; set; } = string.Empty;
    public string DestinationB { get; set; } = string.Empty;
    public int CommonHopsCount { get; set; }
    public string? DivergencePointIp { get; set; }
    public int DivergenceHopNumber { get; set; }
    public double LatencyA { get; set; }
    public double LatencyB { get; set; }
    public int HopsCountA { get; set; }
    public int HopsCountB { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class RouteAnalysisService
{
    private readonly ConcurrentDictionary<string, List<RoutePath>> _routeHistory = new();

    public PathComparisonResult ComparePaths(RoutePath pathA, RoutePath pathB)
    {
        var result = new PathComparisonResult
        {
            DestinationA = pathA.Destination,
            DestinationB = pathB.Destination,
            LatencyA = pathA.TotalLatencyMs,
            LatencyB = pathB.TotalLatencyMs,
            HopsCountA = pathA.HopCount,
            HopsCountB = pathB.HopCount
        };

        int minLen = Math.Min(pathA.Hops.Count, pathB.Hops.Count);
        int common = 0;
        int divergenceHop = 0;
        string? divergenceIp = null;

        for (int i = 0; i < minLen; i++)
        {
            var hopA = pathA.Hops[i];
            var hopB = pathB.Hops[i];

            if (hopA.IpAddress != "*" && hopB.IpAddress != "*" && hopA.IpAddress == hopB.IpAddress)
            {
                common++;
            }
            else
            {
                divergenceHop = i + 1;
                divergenceIp = hopA.IpAddress != "*" ? hopA.IpAddress : hopB.IpAddress;
                break;
            }
        }

        result.CommonHopsCount = common;
        result.DivergenceHopNumber = divergenceHop;
        result.DivergencePointIp = divergenceIp;

        if (divergenceHop > 0)
        {
            result.Summary = $"Routes share the first {common} hops before diverging at hop {divergenceHop}. " +
                             $"{pathA.Destination} is {pathA.TotalLatencyMs:F1}ms ({pathA.HopCount} hops), " +
                             $"while {pathB.Destination} is {pathB.TotalLatencyMs:F1}ms ({pathB.HopCount} hops).";
        }
        else
        {
            result.Summary = $"Both destinations share identical routing paths for all measured hops.";
        }

        return result;
    }

    public List<string> DetectAnomalies(RoutePath path)
    {
        var anomalies = new List<string>();

        // Check for routing loop
        var seenIps = new HashSet<string>();
        foreach (var hop in path.Hops.Where(h => h.IsResponding && h.IpAddress != "*"))
        {
            if (!seenIps.Add(hop.IpAddress))
            {
                anomalies.Add($"Routing loop detected: IP {hop.IpAddress} appears multiple times in the route at hop {hop.HopNumber}.");
                break;
            }
        }

        // Check for persistent latency jump
        foreach (var hop in path.Hops)
        {
            if (hop.IsPersistentLatencyIncrease)
            {
                anomalies.Add($"Sustained latency increase (+{hop.LatencyDeltaFromPrevious:F1} ms) begins around hop {hop.HopNumber} ({hop.DisplayHost}).");
            }
        }

        // Check for non-responding intermediate hops vs destination
        int nonRespondingIntermediate = path.Hops.Take(path.Hops.Count - 1).Count(h => !h.IsResponding);
        if (nonRespondingIntermediate > 0 && path.DestinationReached)
        {
            anomalies.Add($"{nonRespondingIntermediate} intermediate hop(s) did not respond to ICMP. Destination was reached normally, indicating intermediate router ICMP rate-limiting rather than packet drop.");
        }

        if (path.HopCount >= 20)
        {
            anomalies.Add($"Unusually long route: Path requires {path.HopCount} hops to reach destination.");
        }

        return anomalies;
    }

    public RouteChangeEvent? RecordRoute(RoutePath path)
    {
        var history = _routeHistory.GetOrAdd(path.Destination, _ => new List<RoutePath>());
        RouteChangeEvent? changeEvent = null;

        lock (history)
        {
            if (history.Count > 0)
            {
                var previous = history[^1];
                var prevIps = previous.Hops.Where(h => h.IsResponding).Select(h => h.IpAddress).ToList();
                var currIps = path.Hops.Where(h => h.IsResponding).Select(h => h.IpAddress).ToList();

                if (!prevIps.SequenceEqual(currIps))
                {
                    var added = currIps.Except(prevIps).ToList();
                    var removed = prevIps.Except(currIps).ToList();

                    changeEvent = new RouteChangeEvent
                    {
                        Destination = path.Destination,
                        PreviousHopCount = previous.HopCount,
                        CurrentHopCount = path.HopCount,
                        PreviousLatencyMs = previous.TotalLatencyMs,
                        CurrentLatencyMs = path.TotalLatencyMs,
                        ChangeDescription = $"Route to {path.Destination} changed ({added.Count} added, {removed.Count} removed)",
                        AddedHops = added,
                        RemovedHops = removed
                    };
                }
            }

            history.Add(path);
            if (history.Count > 100)
            {
                history.RemoveAt(0);
            }
        }

        return changeEvent;
    }

    public IReadOnlyList<RoutePath> GetRouteHistory(string destination)
    {
        if (_routeHistory.TryGetValue(destination, out var list))
        {
            lock (list)
            {
                return list.ToList();
            }
        }
        return Array.Empty<RoutePath>();
    }
}
