using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;
using NetPulse.Core.Statistics;

namespace NetPulse.Core.Services;

public class BufferbloatProgress
{
    public string Phase { get; set; } = string.Empty; // "Baseline", "Download", "Upload", "Completed"
    public int Percent { get; set; }
    public double CurrentLatencyMs { get; set; }
    public double CurrentThroughputMbps { get; set; }
    public string StatusText { get; set; } = string.Empty;
}

public class BufferbloatService
{
    private readonly PingService _pingService;
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    public BufferbloatService(PingService pingService)
    {
        _pingService = pingService;
    }

    public async Task<BufferbloatResult> RunTestAsync(
        string testHost = "1.1.1.1",
        IProgress<BufferbloatProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new BufferbloatResult { Timestamp = DateTime.UtcNow };
        var pingTarget = new TargetConfig
        {
            Id = "bufferbloat-target",
            Name = "Bufferbloat Target",
            Host = testHost,
            Protocol = NetworkProtocol.Icmp
        };

        // ----------------------------------------------------
        // Phase 1: Measure Unloaded Baseline Latency
        // ----------------------------------------------------
        progress?.Report(new BufferbloatProgress
        {
            Phase = "Baseline",
            Percent = 10,
            StatusText = "Measuring unloaded baseline latency..."
        });

        var baselineSamples = new List<double>();
        for (int i = 0; i < 8; i++)
        {
            if (ct.IsCancellationRequested) break;
            var m = await _pingService.ProbeAsync(pingTarget, 1000, ct);
            if (m.Success) baselineSamples.Add(m.RttMs);
            progress?.Report(new BufferbloatProgress
            {
                Phase = "Baseline",
                Percent = 10 + (i * 2),
                CurrentLatencyMs = m.Success ? m.RttMs : 0,
                StatusText = $"Baseline probe {i + 1}/8: {(m.Success ? $"{m.RttMs:F1} ms" : "Failed")}"
            });
            await Task.Delay(100, ct);
        }

        result.BaselineLatencyMs = baselineSamples.Count > 0
            ? StatisticalCalculator.CalculateMedian(baselineSamples)
            : 20.0;

        // ----------------------------------------------------
        // Phase 2: Measure Download Loaded Latency
        // ----------------------------------------------------
        progress?.Report(new BufferbloatProgress
        {
            Phase = "Download",
            Percent = 30,
            StatusText = "Saturating download queue while measuring latency..."
        });

        var downloadLoadedSamples = new List<double>();
        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        downloadCts.CancelAfter(TimeSpan.FromSeconds(5));

        // Start background download tasks
        var downloadTask = RunDownloadLoadAsync(downloadCts.Token);

        // Ping while download is running
        var dlSw = Stopwatch.StartNew();
        int dlProbes = 0;
        while (!downloadCts.IsCancellationRequested && dlSw.ElapsedMilliseconds < 5000)
        {
            try
            {
                var m = await _pingService.ProbeAsync(pingTarget, 1000, downloadCts.Token);
                if (m.Success)
                {
                    downloadLoadedSamples.Add(m.RttMs);
                    progress?.Report(new BufferbloatProgress
                    {
                        Phase = "Download",
                        Percent = 30 + Math.Min(35, (int)(dlSw.ElapsedMilliseconds / 140)),
                        CurrentLatencyMs = m.RttMs,
                        StatusText = $"Under Download Load: {m.RttMs:F1} ms (Baseline: {result.BaselineLatencyMs:F1} ms)"
                    });
                }
                dlProbes++;
                await Task.Delay(150, downloadCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        try { await downloadTask; } catch { }

        result.DownloadLoadedLatencyMs = downloadLoadedSamples.Count > 0
            ? StatisticalCalculator.CalculateMedian(downloadLoadedSamples)
            : result.BaselineLatencyMs;
        result.DownloadDeltaMs = Math.Max(0, result.DownloadLoadedLatencyMs - result.BaselineLatencyMs);

        // ----------------------------------------------------
        // Phase 3: Measure Upload Loaded Latency
        // ----------------------------------------------------
        progress?.Report(new BufferbloatProgress
        {
            Phase = "Upload",
            Percent = 65,
            StatusText = "Saturating upload queue while measuring latency..."
        });

        var uploadLoadedSamples = new List<double>();
        using var uploadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        uploadCts.CancelAfter(TimeSpan.FromSeconds(5));

        var uploadTask = RunUploadLoadAsync(uploadCts.Token);

        var ulSw = Stopwatch.StartNew();
        while (!uploadCts.IsCancellationRequested && ulSw.ElapsedMilliseconds < 5000)
        {
            try
            {
                var m = await _pingService.ProbeAsync(pingTarget, 1000, uploadCts.Token);
                if (m.Success)
                {
                    uploadLoadedSamples.Add(m.RttMs);
                    progress?.Report(new BufferbloatProgress
                    {
                        Phase = "Upload",
                        Percent = 65 + Math.Min(30, (int)(ulSw.ElapsedMilliseconds / 170)),
                        CurrentLatencyMs = m.RttMs,
                        StatusText = $"Under Upload Load: {m.RttMs:F1} ms (Baseline: {result.BaselineLatencyMs:F1} ms)"
                    });
                }
                await Task.Delay(150, uploadCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        try { await uploadTask; } catch { }

        result.UploadLoadedLatencyMs = uploadLoadedSamples.Count > 0
            ? StatisticalCalculator.CalculateMedian(uploadLoadedSamples)
            : result.BaselineLatencyMs;
        result.UploadDeltaMs = Math.Max(0, result.UploadLoadedLatencyMs - result.BaselineLatencyMs);

        // ----------------------------------------------------
        // Phase 4: Grade and Interpret
        // ----------------------------------------------------
        double maxDelta = Math.Max(result.DownloadDeltaMs, result.UploadDeltaMs);
        result.Grade = BufferbloatResult.CalculateGrade(maxDelta);

        FormatBufferbloatExplanation(result);

        progress?.Report(new BufferbloatProgress
        {
            Phase = "Completed",
            Percent = 100,
            CurrentLatencyMs = result.BaselineLatencyMs,
            StatusText = $"Test Complete: Grade {result.Grade} (+{maxDelta:F1} ms latency increase under load)"
        });

        return result;
    }

    private static void FormatBufferbloatExplanation(BufferbloatResult r)
    {
        double maxDelta = Math.Max(r.DownloadDeltaMs, r.UploadDeltaMs);
        if (maxDelta <= 5)
        {
            r.Summary = "Outstanding queue management. Virtually no latency increase detected under heavy load.";
            r.Explanation = "When your connection is saturated with large downloads or uploads, your router handles queues efficiently without causing ping spikes.";
            r.Recommendation = "Your network is ideal for competitive gaming, Discord calls, and 4K streaming simultaneously.";
        }
        else if (maxDelta <= 15)
        {
            r.Summary = "Great performance. Minimal latency increase under load (+{maxDelta:F0} ms).";
            r.Explanation = "Slight buffering occurs during peak saturation, but it is well within acceptable real-time limits.";
            r.Recommendation = "No configuration changes needed.";
        }
        else if (maxDelta <= 30)
        {
            r.Summary = "Moderate queue build-up (+{maxDelta:F0} ms increase under load).";
            r.Explanation = "You may notice brief audio stuttering or slight gaming lag spikes if someone on your network initiates a large download/upload.";
            r.Recommendation = "If anyone frequently downloads games or backs up files while you game/stream, enabling SQM (Smart Queue Management) on your router is beneficial.";
        }
        else if (maxDelta <= 60)
        {
            r.Summary = "Noticeable bufferbloat (+{maxDelta:F0} ms increase under load).";
            r.Explanation = "Router buffers are holding packets too long during bandwidth saturation, introducing noticeable latency.";
            r.Recommendation = "Enable Smart Queue Management (CAKE or fq_codel) on your router to prioritize interactive traffic over bulk transfers.";
        }
        else
        {
            r.Summary = $"Severe bufferbloat detected (+{maxDelta:F0} ms latency increase under load).";
            r.Explanation = "Your router's packet buffer grows excessively large during downloads/uploads, causing dramatic lag spikes, voice call dropouts, and stuttering video.";
            r.Recommendation = "Enabling SQM (CAKE / fq_codel) or QoS on your home router is strongly recommended to maintain low latency during network activity.";
        }
    }

    private static async Task RunDownloadLoadAsync(CancellationToken ct)
    {
        var urls = new[]
        {
            "https://speed.cloudflare.com/__down?bytes=15000000",
            "https://speed.cloudflare.com/__down?bytes=15000000",
            "https://speed.cloudflare.com/__down?bytes=15000000"
        };

        var tasks = urls.Select(async url =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var resp = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    byte[] buffer = new byte[81920];
                    while (await stream.ReadAsync(buffer, 0, buffer.Length, ct) > 0)
                    {
                        if (ct.IsCancellationRequested) break;
                    }
                }
            }
            catch { }
        });

        await Task.WhenAll(tasks);
    }

    private static async Task RunUploadLoadAsync(CancellationToken ct)
    {
        byte[] payload = new byte[256 * 1024]; // 256 KB chunks
        Random.Shared.NextBytes(payload);

        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var content = new ByteArrayContent(payload);
                    using var resp = await HttpClient.PostAsync("https://speed.cloudflare.com/__up", content, ct);
                }
            }
            catch { }
        });

        await Task.WhenAll(tasks);
    }
}
