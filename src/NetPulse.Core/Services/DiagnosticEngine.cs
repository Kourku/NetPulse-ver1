using System;
using System.Collections.Generic;
using System.Linq;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class DiagnosticEngine
{
    public DiagnosticResult Analyze(
        NetworkInterfaceInfo? nic,
        TargetMetrics? gatewayMetrics,
        IReadOnlyList<TargetMetrics> internetMetrics,
        TargetMetrics? dnsMetrics,
        BufferbloatResult? bufferbloatResult = null)
    {
        var result = new DiagnosticResult
        {
            Timestamp = DateTime.UtcNow
        };

        var evidence = new List<string>();

        // Check 1: Interface state
        if (nic == null || nic.OperationalStatus.Equals("Down", StringComparison.OrdinalIgnoreCase))
        {
            result.Category = DiagnosticCategory.Disconnected;
            result.Status = DiagnosticStatus.Critical;
            result.Headline = "No active network connection detected.";
            result.LikelyCause = "Network interface is down or disconnected.";
            result.RecommendedAction = "Check your Wi-Fi connection or ensure your Ethernet cable is securely plugged in.";
            evidence.Add("Network adapter status is reported as DOWN by Windows.");
            result.EvidenceItems = evidence;
            result.DetailSummary = string.Join("\n", evidence);
            return result;
        }

        // Gather gateway stats
        double gwLatency = gatewayMetrics?.CurrentLatency ?? 0;
        double gwLoss = gatewayMetrics?.PacketLossPercent ?? 0;
        double gwJitter = gatewayMetrics?.Jitter ?? 0;
        bool gwReachable = gatewayMetrics?.IsReachable ?? false;

        result.GatewayLatencyMs = gwLatency;

        // Gather internet stats (excluding gateway and pure DNS)
        var validInternet = internetMetrics
            .Where(t => !t.IsGateway && !t.IsDnsResolver && t.TotalProbes > 0)
            .ToList();

        double inetAvgLatency = validInternet.Count > 0 ? validInternet.Average(t => t.CurrentLatency) : 0;
        double inetAvgLoss = validInternet.Count > 0 ? validInternet.Average(t => t.PacketLossPercent) : 0;
        double avgJitter = validInternet.Count > 0 ? validInternet.Average(t => t.Jitter) : gwJitter;

        result.InternetAverageLatencyMs = inetAvgLatency;
        result.PacketLossPercent = inetAvgLoss;
        result.AverageJitterMs = avgJitter;

        // Gather DNS stats
        double dnsLatency = dnsMetrics?.CurrentLatency ?? 0;
        double dnsLoss = dnsMetrics?.PacketLossPercent ?? 0;
        result.DnsLatencyMs = dnsLatency;

        // Evidence collection
        if (gatewayMetrics != null && gatewayMetrics.TotalProbes > 0)
        {
            evidence.Add(gwReachable
                ? $"• Local router (gateway) responding: {gwLatency:F1} ms (Jitter: {gwJitter:F1} ms, Loss: {gwLoss:F0}%)"
                : "• Local router (gateway) is UNREACHABLE (100% packet loss)");
        }

        if (validInternet.Count > 0)
        {
            int reaching = validInternet.Count(t => t.IsReachable);
            evidence.Add($"• Internet endpoints reachable: {reaching}/{validInternet.Count} targets (Avg Latency: {inetAvgLatency:F1} ms, Loss: {inetAvgLoss:F0}%)");
        }

        if (dnsMetrics != null && dnsMetrics.TotalProbes > 0)
        {
            evidence.Add(dnsMetrics.IsReachable
                ? $"• DNS resolution: {dnsLatency:F1} ms"
                : "• DNS resolution: FAILING / UNRESPONSIVE");
        }

        if (nic.IsWifi && nic.WifiSignalPercent.HasValue)
        {
            evidence.Add($"• Wi-Fi Signal Strength: {nic.WifiSignalPercent.Value}% ({nic.WifiRadioType ?? "802.11"})");
        }

        // ====================================================
        // Rule 1: Disconnected from Router and Internet
        // ====================================================
        if (!gwReachable && validInternet.All(t => !t.IsReachable) && validInternet.Count > 0)
        {
            result.Category = DiagnosticCategory.Disconnected;
            result.Status = DiagnosticStatus.Critical;
            result.Headline = "Disconnected from router and Internet.";
            result.LikelyCause = "Your computer cannot communicate with your home router or the local gateway.";
            result.RecommendedAction = "Verify your Wi-Fi or Ethernet connection to your router. Restart the router if necessary.";
            result.EvidenceItems = evidence;
            result.DetailSummary = string.Join("\n", evidence);
            return result;
        }

        // ====================================================
        // Rule 2: Local Network / Wi-Fi / Router Issue
        // ====================================================
        bool wifiWeak = nic.IsWifi && nic.WifiSignalPercent.HasValue && nic.WifiSignalPercent.Value < 30;
        bool gwHighLatency = (nic.IsWifi && gwLatency > 25.0) || (!nic.IsWifi && gwLatency > 6.0);
        bool gwHighLoss = gwLoss > 4.0;

        if (gwHighLoss || gwHighLatency || wifiWeak)
        {
            result.Category = DiagnosticCategory.LocalNetworkIssue;
            result.Status = (gwLoss > 15.0 || gwLatency > 60.0) ? DiagnosticStatus.Critical : DiagnosticStatus.Degraded;
            result.Headline = "Problem appears to originate on your local network or Wi-Fi.";
            result.LikelyCause = wifiWeak
                ? "Weak Wi-Fi signal is causing local packet loss and latency spikes before traffic even reaches your router."
                : "Elevated latency or packet loss was measured between your PC and the local router/gateway.";
            result.RecommendedAction = wifiWeak
                ? "Move closer to your Wi-Fi router, reduce physical obstructions, or connect using an Ethernet cable."
                : "Check your local network cable/Wi-Fi connection, or restart your router.";
            result.EvidenceItems = evidence;
            result.DetailSummary = string.Join("\n", evidence);
            return result;
        }

        // ====================================================
        // Rule 3: DNS Specific Issue
        // ====================================================
        if (dnsMetrics != null && dnsMetrics.TotalProbes > 3 && (!dnsMetrics.IsReachable || dnsLoss > 20.0 || dnsLatency > 180.0))
        {
            if (gwReachable && gwLoss < 2.0 && validInternet.Any(t => t.IsReachable && t.PacketLossPercent < 5.0))
            {
                result.Category = DiagnosticCategory.DnsIssue;
                result.Status = DiagnosticStatus.Degraded;
                result.Headline = "DNS resolver is slow or failing.";
                result.LikelyCause = "Your computer can reach the Internet, but domain name resolution is timing out or taking unusually long.";
                result.RecommendedAction = "Change your DNS resolver settings to Cloudflare (1.1.1.1) or Google (8.8.8.8) in Windows Network Settings.";
                evidence.Add($"• DNS resolution latency ({dnsLatency:F1} ms) is disproportionately higher than IP packet latency ({inetAvgLatency:F1} ms).");
                result.EvidenceItems = evidence;
                result.DetailSummary = string.Join("\n", evidence);
                return result;
            }
        }

        // ====================================================
        // Rule 4: ISP / Upstream Internet Degradation
        // ====================================================
        if (gwReachable && gwLoss <= 1.0 && gwLatency < 10.0)
        {
            // Router is pristine, but multiple Internet endpoints degrade
            int degradedCount = validInternet.Count(t => t.PacketLossPercent > 5.0 || t.CurrentLatency > 100.0);
            if (degradedCount >= 2 || (validInternet.Count > 0 && degradedCount == validInternet.Count))
            {
                result.Category = DiagnosticCategory.IspInternetIssue;
                result.Status = inetAvgLoss > 10.0 ? DiagnosticStatus.Critical : DiagnosticStatus.Degraded;
                result.Headline = "Local network is healthy; degradation detected upstream on the ISP or Internet path.";
                result.LikelyCause = "Data passes cleanly between your PC and router, but degradation occurs across multiple independent Internet routes.";
                result.RecommendedAction = "This issue lies outside your home network. Save and share this diagnostic report with your Internet Service Provider (ISP).";
                result.EvidenceItems = evidence;
                result.DetailSummary = string.Join("\n", evidence);
                return result;
            }
        }

        // ====================================================
        // Rule 5: Target-Specific Degradation
        // ====================================================
        var failingTargets = validInternet.Where(t => t.TotalProbes > 3 && (!t.IsReachable || t.PacketLossPercent > 20.0 || t.CurrentLatency > (inetAvgLatency * 3.5))).ToList();
        if (failingTargets.Count == 1 && validInternet.Count >= 3)
        {
            var singleTarget = failingTargets[0];
            result.Category = DiagnosticCategory.TargetSpecificIssue;
            result.Status = DiagnosticStatus.Degraded;
            result.Headline = $"Issue appears specific to '{singleTarget.TargetName}'. General Internet is healthy.";
            result.LikelyCause = $"Only {singleTarget.TargetName} is experiencing packet loss or high latency. Other diverse endpoints are responding normally.";
            result.RecommendedAction = "No action required on your local network. The remote service or its specific host provider is having issues.";
            evidence.Add($"• Target '{singleTarget.TargetName}' is degraded, while other endpoints are operating normally.");
            result.EvidenceItems = evidence;
            result.DetailSummary = string.Join("\n", evidence);
            return result;
        }

        // ====================================================
        // Rule 6: Bufferbloat / Queue Congestion Check
        // ====================================================
        if (bufferbloatResult != null && (bufferbloatResult.Grade == "D" || bufferbloatResult.Grade == "F"))
        {
            result.Category = DiagnosticCategory.BufferbloatCongestion;
            result.Status = DiagnosticStatus.Degraded;
            result.Headline = "Significant latency increase under load (Bufferbloat) detected.";
            result.LikelyCause = "When large downloads or uploads occur, your router queues packets excessively, causing high ping spikes.";
            result.RecommendedAction = "Enable Smart Queue Management (SQM like CAKE or fq_codel) on your router to keep latency low during active usage.";
            evidence.Add($"• Bufferbloat Grade: {bufferbloatResult.Grade} (+{Math.Max(bufferbloatResult.DownloadDeltaMs, bufferbloatResult.UploadDeltaMs):F0} ms latency jump under load).");
            result.EvidenceItems = evidence;
            result.DetailSummary = string.Join("\n", evidence);
            return result;
        }

        // ====================================================
        // Rule 7: Optimal Connection
        // ====================================================
        if (gwReachable && gwLatency <= 10.0 && gwLoss == 0 && inetAvgLoss == 0 && inetAvgLatency < 80.0 && avgJitter < 8.0)
        {
            result.Category = DiagnosticCategory.Optimal;
            result.Status = DiagnosticStatus.Healthy;
            result.Headline = "Your connection is healthy, fast, and stable.";
            result.LikelyCause = "All monitored layers (local router, DNS, and diverse Internet backbones) are performing within ideal parameters.";
            result.RecommendedAction = "No action needed. Your connection is in excellent condition.";
            result.EvidenceItems = evidence;
            result.DetailSummary = string.Join("\n", evidence);
            return result;
        }

        // ====================================================
        // Rule 8: Inconclusive / Minor Fluctuations
        // ====================================================
        result.Category = DiagnosticCategory.Inconclusive;
        result.Status = DiagnosticStatus.Healthy;
        result.Headline = "Connection is generally stable with minor transient fluctuations.";
        result.LikelyCause = "Minor latency variations observed, but measurements cannot confidently attribute causality to a specific fault.";
        result.RecommendedAction = "Monitoring is active. No immediate intervention is required.";
        result.EvidenceItems = evidence;
        result.DetailSummary = string.Join("\n", evidence);
        return result;
    }
}
