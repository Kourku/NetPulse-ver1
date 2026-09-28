using System;

namespace NetPulse.Core.Models;

public class ConnectionEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string EventType { get; set; } = "Info"; // "LatencySpike", "PacketLoss", "GatewayDegraded", "DnsFailure", "RouteChange", "InterfaceChanged", "Disconnected", "Restored"
    public string Severity { get; set; } = "Info"; // "Info", "Warning", "Critical"
    public string Description { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public double? MetricValue { get; set; }
    public string? ContextData { get; set; }

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] [{Severity.ToUpper()}] {EventType}: {Description}";
}
