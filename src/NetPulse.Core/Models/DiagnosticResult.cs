using System;
using System.Collections.Generic;

namespace NetPulse.Core.Models;

public enum DiagnosticCategory
{
    Optimal,
    LocalNetworkIssue,
    IspInternetIssue,
    DnsIssue,
    TargetSpecificIssue,
    BufferbloatCongestion,
    BandwidthExhausted,
    Inconclusive,
    Disconnected
}

public enum DiagnosticStatus
{
    Healthy,
    Degraded,
    Critical
}

public class DiagnosticResult
{
    public DiagnosticCategory Category { get; set; } = DiagnosticCategory.Optimal;
    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Healthy;

    // Plain English headline for Beginner Mode (Level 1)
    public string Headline { get; set; } = "Your connection is healthy and stable.";

    // Detailed diagnostic summary (Level 2 & 3)
    public string DetailSummary { get; set; } = string.Empty;

    // Probable cause explanation with evidence-based phrasing
    public string LikelyCause { get; set; } = string.Empty;

    // Actionable recommendation for user or ISP technician
    public string RecommendedAction { get; set; } = string.Empty;

    // Specific evidence items
    public List<string> EvidenceItems { get; set; } = new();

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Quick metric highlights
    public double GatewayLatencyMs { get; set; }
    public double InternetAverageLatencyMs { get; set; }
    public double AverageJitterMs { get; set; }
    public double PacketLossPercent { get; set; }
    public double DnsLatencyMs { get; set; }
}
