using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task ExportToCsvAsync(string filePath, IReadOnlyList<Measurement> measurements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TimestampUTC,TargetId,TargetName,TargetHost,Protocol,Success,RttMs,ResolvedIp,Error,HttpsDetails");

        foreach (var m in measurements)
        {
            string httpsInfo = m.HttpsBreakdown != null ? $"\"{m.HttpsBreakdown}\"" : "";
            string error = !string.IsNullOrEmpty(m.ErrorMessage) ? $"\"{m.ErrorMessage.Replace("\"", "\"\"")}\"" : "";
            string targetName = $"\"{m.TargetName.Replace("\"", "\"\"")}\"";

            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:O},{1},{2},{3},{4},{5},{6:F2},{7},{8},{9}",
                m.Timestamp,
                m.TargetId,
                targetName,
                m.TargetHost,
                m.Protocol,
                m.Success,
                m.RttMs,
                m.ResolvedIp ?? "",
                error,
                httpsInfo));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    public async Task ExportToJsonAsync(string filePath, object data)
    {
        string json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
    }

    public async Task<string> GenerateDiagnosticReportAsync(
        NetworkInterfaceInfo nic,
        DiagnosticResult diagnosis,
        IReadOnlyList<TargetMetrics> targetMetrics,
        IReadOnlyList<ConnectionEvent> events,
        RoutePath? routePath = null,
        BufferbloatResult? bufferbloat = null,
        SpeedTestResult? speedTest = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# NetPulse Network Quality & Diagnostic Report");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Application Version: NetPulse 1.0.0");
        sb.AppendLine();

        sb.AppendLine("## 1. Executive Summary");
        sb.AppendLine($"* **Overall Status:** {diagnosis.Status.ToString().ToUpperInvariant()} ({diagnosis.Category})");
        sb.AppendLine($"* **Diagnostic Headline:** {diagnosis.Headline}");
        sb.AppendLine($"* **Likely Cause:** {diagnosis.LikelyCause}");
        sb.AppendLine($"* **Recommended Action:** {diagnosis.RecommendedAction}");
        sb.AppendLine();

        sb.AppendLine("## 2. Active Network Interface");
        sb.AppendLine($"* **Adapter:** {nic.Name} ({nic.Description})");
        sb.AppendLine($"* **Type:** {nic.InterfaceType}");
        sb.AppendLine($"* **Operational Status:** {nic.OperationalStatus}");
        sb.AppendLine($"* **Link Speed:** {nic.SpeedDisplay}");
        sb.AppendLine($"* **IPv4 Address:** {nic.Ipv4Address} (Mask: {nic.Ipv4Subnet})");
        sb.AppendLine($"* **IPv4 Default Gateway:** {nic.Ipv4Gateway}");
        if (!string.IsNullOrEmpty(nic.Ipv6Address))
            sb.AppendLine($"* **IPv6 Address:** {nic.Ipv6Address}");
        if (!string.IsNullOrEmpty(nic.Ipv6Gateway))
            sb.AppendLine($"* **IPv6 Default Gateway:** {nic.Ipv6Gateway}");
        sb.AppendLine($"* **DNS Servers:** {(nic.DnsServers.Count > 0 ? string.Join(", ", nic.DnsServers) : "None")}");

        if (nic.IsWifi)
        {
            sb.AppendLine();
            sb.AppendLine("### Wi-Fi Link Details");
            sb.AppendLine($"* **SSID:** {nic.WifiSsid ?? "Unknown"}");
            sb.AppendLine($"* **Signal Quality:** {(nic.WifiSignalPercent.HasValue ? $"{nic.WifiSignalPercent}%" : "Unknown")}");
            sb.AppendLine($"* **Radio Type:** {nic.WifiRadioType ?? "Unknown"}");
            sb.AppendLine($"* **Channel:** {(nic.WifiChannel.HasValue ? nic.WifiChannel.ToString() : "Unknown")}");
            sb.AppendLine($"* **BSSID:** {nic.WifiBssid ?? "Unknown"}");
        }
        sb.AppendLine();

        sb.AppendLine("## 3. Real-Time Probing Statistics");
        sb.AppendLine("| Target | Protocol | Current (ms) | Avg (ms) | Min (ms) | Max (ms) | Median (ms) | P95 (ms) | Jitter (ms) | Loss (%) | Probes |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var t in targetMetrics)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "| {0} | {1} | {2:F1} | {3:F1} | {4:F1} | {5:F1} | {6:F1} | {7:F1} | {8:F1} | {9:F1}% | {10} |",
                t.TargetName,
                t.Protocol,
                t.CurrentLatency,
                t.RollingAvg,
                t.MinLatency == double.MaxValue ? 0 : t.MinLatency,
                t.MaxLatency,
                t.MedianLatency,
                t.P95Latency,
                t.Jitter,
                t.PacketLossPercent,
                t.TotalProbes));
        }
        sb.AppendLine();

        if (routePath != null && routePath.Hops.Count > 0)
        {
            sb.AppendLine($"## 4. Route Path Analysis (Destination: {routePath.Destination} - {routePath.DestinationIp})");
            sb.AppendLine($"* Total Hops: {routePath.HopCount}");
            sb.AppendLine($"* Total End-to-End Latency: {routePath.TotalLatencyMs:F1} ms");
            sb.AppendLine();
            sb.AppendLine("| Hop | IP Address | Hostname / rDNS | RTT (ms) | Loss (%) | ASN / Organization | Notes |");
            sb.AppendLine("|---|---|---|---|---|---|---|");

            foreach (var h in routePath.Hops)
            {
                string note = h.IsPersistentLatencyIncrease ? "⚠️ Sustained Latency Jump" : (!h.IsResponding ? "No ICMP reply" : "Normal");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3:F1} | {4:F0}% | {5} | {6} |",
                    h.HopNumber,
                    h.IpAddress,
                    h.DisplayHost,
                    h.CurrentRttMs,
                    h.LossPercent,
                    h.AsDisplay,
                    note));
            }
            sb.AppendLine();
        }

        if (bufferbloat != null)
        {
            sb.AppendLine("## 5. Bufferbloat & Loaded Latency Evaluation");
            sb.AppendLine($"* **Overall Grade:** {bufferbloat.Grade}");
            sb.AppendLine($"* **Baseline Unloaded Latency:** {bufferbloat.BaselineLatencyMs:F1} ms");
            sb.AppendLine($"* **Download Loaded Latency:** {bufferbloat.DownloadLoadedLatencyMs:F1} ms (+{bufferbloat.DownloadDeltaMs:F1} ms)");
            sb.AppendLine($"* **Upload Loaded Latency:** {bufferbloat.UploadLoadedLatencyMs:F1} ms (+{bufferbloat.UploadDeltaMs:F1} ms)");
            sb.AppendLine($"* **Summary:** {bufferbloat.Summary}");
            sb.AppendLine($"* **Technical Explanation:** {bufferbloat.Explanation}");
            sb.AppendLine($"* **Actionable Advice:** {bufferbloat.Recommendation}");
            sb.AppendLine();
        }

        if (speedTest != null && speedTest.CompletedSuccessfully)
        {
            sb.AppendLine("## 6. Throughput Measurement");
            sb.AppendLine($"* **Download Speed:** {speedTest.DownloadSpeedMbps:F1} Mbps ({(speedTest.BytesDownloaded / 1_000_000.0):F1} MB)");
            sb.AppendLine($"* **Upload Speed:** {speedTest.UploadSpeedMbps:F1} Mbps ({(speedTest.BytesUploaded / 1_000_000.0):F1} MB)");
            sb.AppendLine($"* **Test Duration:** {speedTest.DurationSeconds:F1} s");
            sb.AppendLine();
        }

        sb.AppendLine("## 7. Timeline of Connection Events");
        if (events.Count == 0)
        {
            sb.AppendLine("* No anomalous connection events recorded during this session.");
        }
        else
        {
            foreach (var e in events.Take(50))
            {
                sb.AppendLine($"* `[{e.Timestamp:yyyy-MM-dd HH:mm:ss}]` **[{e.Severity.ToUpperInvariant()}]** {e.EventType}: {e.Description}");
            }
        }
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine("*Report generated by NetPulse Diagnostic Tool - 100% Local & Privacy-Preserving.*");

        return sb.ToString();
    }
}
