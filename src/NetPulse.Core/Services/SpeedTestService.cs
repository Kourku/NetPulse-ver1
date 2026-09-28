using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class SpeedTestProgress
{
    public string Phase { get; set; } = string.Empty; // "Download", "Upload", "Completed"
    public int Percent { get; set; }
    public double CurrentSpeedMbps { get; set; }
    public long BytesTransferred { get; set; }
    public string StatusText { get; set; } = string.Empty;
}

public class SpeedTestService
{
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<SpeedTestResult> RunSpeedTestAsync(
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new SpeedTestResult
        {
            Timestamp = DateTime.UtcNow
        };

        var totalSw = Stopwatch.StartNew();

        try
        {
            // ----------------------------------------------------
            // Phase 1: Download Test (5 seconds)
            // ----------------------------------------------------
            progress?.Report(new SpeedTestProgress
            {
                Phase = "Download",
                Percent = 10,
                StatusText = "Connecting to high-speed CDN chunk servers..."
            });

            long totalDownloadBytes = 0;
            var dlSw = Stopwatch.StartNew();

            using (var dlCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                dlCts.CancelAfter(TimeSpan.FromSeconds(5));

                var dlUrls = new[]
                {
                    "https://speed.cloudflare.com/__down?bytes=25000000",
                    "https://speed.cloudflare.com/__down?bytes=25000000",
                    "https://speed.cloudflare.com/__down?bytes=25000000",
                    "https://speed.cloudflare.com/__down?bytes=25000000"
                };

                var dlTasks = dlUrls.Select(async url =>
                {
                    byte[] buffer = new byte[65536];
                    try
                    {
                        using var resp = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, dlCts.Token);
                        using var stream = await resp.Content.ReadAsStreamAsync(dlCts.Token);
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, dlCts.Token)) > 0)
                        {
                            Interlocked.Add(ref totalDownloadBytes, read);
                        }
                    }
                    catch { }
                }).ToList();

                // Live progress ticker
                while (!dlCts.IsCancellationRequested && dlSw.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(200, ct);
                    double elapsedSec = Math.Max(0.1, dlSw.Elapsed.TotalSeconds);
                    double currentMbps = (Interlocked.Read(ref totalDownloadBytes) * 8.0) / (elapsedSec * 1_000_000.0);
                    int percent = Math.Min(50, 10 + (int)((dlSw.ElapsedMilliseconds / 5000.0) * 40));

                    progress?.Report(new SpeedTestProgress
                    {
                        Phase = "Download",
                        Percent = percent,
                        CurrentSpeedMbps = currentMbps,
                        BytesTransferred = Interlocked.Read(ref totalDownloadBytes),
                        StatusText = $"Testing Download: {currentMbps:F1} Mbps ({(Interlocked.Read(ref totalDownloadBytes) / 1_000_000.0):F1} MB transferred)"
                    });
                }

                try { await Task.WhenAll(dlTasks); } catch { }
            }

            dlSw.Stop();
            double dlSec = Math.Max(0.1, dlSw.Elapsed.TotalSeconds);
            result.BytesDownloaded = totalDownloadBytes;
            result.DownloadSpeedMbps = (totalDownloadBytes * 8.0) / (dlSec * 1_000_000.0);

            // ----------------------------------------------------
            // Phase 2: Upload Test (5 seconds)
            // ----------------------------------------------------
            progress?.Report(new SpeedTestProgress
            {
                Phase = "Upload",
                Percent = 55,
                StatusText = "Preparing upload test..."
            });

            long totalUploadBytes = 0;
            var ulSw = Stopwatch.StartNew();

            using (var ulCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                ulCts.CancelAfter(TimeSpan.FromSeconds(5));

                byte[] uploadBuffer = new byte[256 * 1024]; // 256 KB
                Random.Shared.NextBytes(uploadBuffer);

                var ulTasks = Enumerable.Range(0, 3).Select(async _ =>
                {
                    try
                    {
                        while (!ulCts.IsCancellationRequested)
                        {
                            using var content = new ByteArrayContent(uploadBuffer);
                            using var resp = await HttpClient.PostAsync("https://speed.cloudflare.com/__up", content, ulCts.Token);
                            Interlocked.Add(ref totalUploadBytes, uploadBuffer.Length);
                        }
                    }
                    catch { }
                }).ToList();

                // Live progress ticker
                while (!ulCts.IsCancellationRequested && ulSw.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(200, ct);
                    double elapsedSec = Math.Max(0.1, ulSw.Elapsed.TotalSeconds);
                    double currentMbps = (Interlocked.Read(ref totalUploadBytes) * 8.0) / (elapsedSec * 1_000_000.0);
                    int percent = Math.Min(95, 55 + (int)((ulSw.ElapsedMilliseconds / 5000.0) * 40));

                    progress?.Report(new SpeedTestProgress
                    {
                        Phase = "Upload",
                        Percent = percent,
                        CurrentSpeedMbps = currentMbps,
                        BytesTransferred = Interlocked.Read(ref totalUploadBytes),
                        StatusText = $"Testing Upload: {currentMbps:F1} Mbps ({(Interlocked.Read(ref totalUploadBytes) / 1_000_000.0):F1} MB sent)"
                    });
                }

                try { await Task.WhenAll(ulTasks); } catch { }
            }

            ulSw.Stop();
            double ulSec = Math.Max(0.1, ulSw.Elapsed.TotalSeconds);
            result.BytesUploaded = totalUploadBytes;
            result.UploadSpeedMbps = (totalUploadBytes * 8.0) / (ulSec * 1_000_000.0);

            totalSw.Stop();
            result.DurationSeconds = totalSw.Elapsed.TotalSeconds;
            result.CompletedSuccessfully = true;

            progress?.Report(new SpeedTestProgress
            {
                Phase = "Completed",
                Percent = 100,
                CurrentSpeedMbps = result.DownloadSpeedMbps,
                StatusText = $"Speed Test Finished: {result.DownloadSpeedMbps:F1} Mbps Download | {result.UploadSpeedMbps:F1} Mbps Upload"
            });
        }
        catch (OperationCanceledException)
        {
            result.CompletedSuccessfully = false;
            result.ErrorMessage = "Test was cancelled by user.";
        }
        catch (Exception ex)
        {
            result.CompletedSuccessfully = false;
            result.ErrorMessage = ex.GetBaseException().Message;
        }

        return result;
    }
}
