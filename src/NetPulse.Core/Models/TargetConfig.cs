using System;
using System.Collections.Generic;

namespace NetPulse.Core.Models;

public class TargetConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public NetworkProtocol Protocol { get; set; } = NetworkProtocol.Icmp;
    public bool IsEnabled { get; set; } = true;
    public bool IsGateway { get; set; }
    public bool IsDnsResolver { get; set; }
    public int IntervalMs { get; set; } = 1000;
    public string? Description { get; set; }
}

public class TargetMetrics
{
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string TargetHost { get; set; } = string.Empty;
    public NetworkProtocol Protocol { get; set; }
    public bool IsGateway { get; set; }
    public bool IsDnsResolver { get; set; }

    public double CurrentLatency { get; set; }
    public double RollingAvg { get; set; }
    public double MinLatency { get; set; } = double.MaxValue;
    public double MaxLatency { get; set; }
    public double MedianLatency { get; set; }
    public double P95Latency { get; set; }
    public double Jitter { get; set; }
    public double PacketLossPercent { get; set; }

    public int TotalProbes { get; set; }
    public int FailedProbes { get; set; }
    public DateTime LastUpdate { get; set; }
    public bool IsReachable { get; set; } = true;
    public string? LastError { get; set; }
    public HttpsTimingBreakdown? LastHttpsBreakdown { get; set; }

    public List<Measurement> RecentHistory { get; set; } = new();
}
