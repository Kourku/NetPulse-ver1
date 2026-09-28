using System;

namespace NetPulse.Core.Models;

public class HttpsTimingBreakdown
{
    public double DnsLookupMs { get; set; }
    public double TcpConnectMs { get; set; }
    public double TlsHandshakeMs { get; set; }
    public double TtfbMs { get; set; }
    public double TotalMs { get; set; }
    public int StatusCode { get; set; }

    public override string ToString() =>
        $"DNS: {DnsLookupMs:F1}ms | TCP: {TcpConnectMs:F1}ms | TLS: {TlsHandshakeMs:F1}ms | TTFB: {TtfbMs:F1}ms | Total: {TotalMs:F1}ms";
}

public class Measurement
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string TargetHost { get; set; } = string.Empty;
    public NetworkProtocol Protocol { get; set; }
    public double RttMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public HttpsTimingBreakdown? HttpsBreakdown { get; set; }
    public string? ResolvedIp { get; set; }

    public override string ToString() =>
        $"[{Timestamp:HH:mm:ss}] {TargetName} ({Protocol}): {(Success ? $"{RttMs:F1} ms" : $"FAILED: {ErrorMessage}")}";
}
