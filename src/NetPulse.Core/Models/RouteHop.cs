using System;
using System.Collections.Generic;

namespace NetPulse.Core.Models;

public class RouteHop
{
    public int HopNumber { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public double CurrentRttMs { get; set; }
    public double MinRttMs { get; set; } = double.MaxValue;
    public double MaxRttMs { get; set; }
    public double AvgRttMs { get; set; }
    public double LossPercent { get; set; }
    public int SentCount { get; set; }
    public int ReceivedCount { get; set; }
    public string? AsNumber { get; set; }
    public string? AsOrg { get; set; }
    public bool IsResponding { get; set; } = true;
    public double LatencyDeltaFromPrevious { get; set; }
    public bool IsPersistentLatencyIncrease { get; set; }
    public string DisplayLocation { get; set; } = string.Empty;

    public string DisplayHost =>
        !string.IsNullOrEmpty(Hostname) ? Hostname : (!string.IsNullOrEmpty(IpAddress) ? IpAddress : "*");

    public string AsDisplay =>
        !string.IsNullOrEmpty(AsNumber) ? $"{AsNumber} ({AsOrg})" : string.Empty;
}

public class RoutePath
{
    public string Destination { get; set; } = string.Empty;
    public string DestinationIp { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<RouteHop> Hops { get; set; } = new();
    public double TotalLatencyMs { get; set; }
    public bool DestinationReached { get; set; }
    public int HopCount => Hops.Count;
}

public class RouteChangeEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Destination { get; set; } = string.Empty;
    public int PreviousHopCount { get; set; }
    public int CurrentHopCount { get; set; }
    public double PreviousLatencyMs { get; set; }
    public double CurrentLatencyMs { get; set; }
    public string ChangeDescription { get; set; } = string.Empty;
    public List<string> AddedHops { get; set; } = new();
    public List<string> RemovedHops { get; set; } = new();
    public double LatencyDelta => CurrentLatencyMs - PreviousLatencyMs;
}
