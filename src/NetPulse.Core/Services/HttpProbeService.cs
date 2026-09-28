using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetPulse.Core.Models;

namespace NetPulse.Core.Services;

public class HttpProbeService
{
    public async Task<Measurement> ProbeAsync(TargetConfig target, int timeoutMs = 4000, CancellationToken ct = default)
    {
        var measurement = new Measurement
        {
            Timestamp = DateTime.UtcNow,
            TargetId = target.Id,
            TargetName = target.Name,
            TargetHost = target.Host,
            Protocol = NetworkProtocol.Https
        };

        var breakdown = new HttpsTimingBreakdown();
        measurement.HttpsBreakdown = breakdown;

        string host = target.Host.Replace("https://", "").Replace("http://", "").Split('/')[0];
        int port = target.Port > 0 ? target.Port : 443;
        bool isHttps = port == 443 || target.Protocol == NetworkProtocol.Https;

        var totalSw = Stopwatch.StartNew();
        var phaseSw = Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        try
        {
            // Phase 1: DNS Lookup
            phaseSw.Restart();
            var addresses = await Dns.GetHostAddressesAsync(host, cts.Token);
            phaseSw.Stop();
            breakdown.DnsLookupMs = Math.Max(0.1, phaseSw.Elapsed.TotalMilliseconds);

            if (addresses.Length == 0)
            {
                measurement.Success = false;
                measurement.ErrorMessage = "DNS lookup yielded no addresses";
                return measurement;
            }

            var endpoint = new IPEndPoint(addresses[0], port);
            measurement.ResolvedIp = addresses[0].ToString();

            // Phase 2: TCP Connect
            using var socket = new Socket(SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            socket.NoDelay = true;

            phaseSw.Restart();
            await socket.ConnectAsync(endpoint, cts.Token);
            phaseSw.Stop();
            breakdown.TcpConnectMs = Math.Max(0.1, phaseSw.Elapsed.TotalMilliseconds);

            using var netStream = new NetworkStream(socket, ownsSocket: false);
            Stream stream = netStream;

            // Phase 3: TLS Handshake (if HTTPS)
            if (isHttps)
            {
                var sslStream = new SslStream(netStream, false, (s, cert, chain, errs) => true);
                phaseSw.Restart();
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                          System.Security.Authentication.SslProtocols.Tls13
                }, cts.Token);
                phaseSw.Stop();
                breakdown.TlsHandshakeMs = Math.Max(0.1, phaseSw.Elapsed.TotalMilliseconds);
                stream = sslStream;
            }

            // Phase 4: HTTP Request & TTFB
            string request = $"HEAD / HTTP/1.1\r\nHost: {host}\r\nUser-Agent: NetPulse/1.0\r\nConnection: close\r\n\r\n";
            byte[] reqBytes = Encoding.ASCII.GetBytes(request);

            phaseSw.Restart();
            await stream.WriteAsync(reqBytes, 0, reqBytes.Length, cts.Token);
            await stream.FlushAsync(cts.Token);

            byte[] respBuf = new byte[512];
            int read = await stream.ReadAsync(respBuf, 0, respBuf.Length, cts.Token);
            phaseSw.Stop();
            breakdown.TtfbMs = Math.Max(0.1, phaseSw.Elapsed.TotalMilliseconds);

            totalSw.Stop();
            breakdown.TotalMs = Math.Max(0.1, totalSw.Elapsed.TotalMilliseconds);

            if (read > 0)
            {
                string headerLine = Encoding.ASCII.GetString(respBuf, 0, read).Split("\r\n")[0];
                if (headerLine.Contains(" "))
                {
                    var parts = headerLine.Split(' ');
                    if (parts.Length > 1 && int.TryParse(parts[1], out int code))
                    {
                        breakdown.StatusCode = code;
                    }
                }
                measurement.Success = true;
                measurement.RttMs = breakdown.TotalMs;
            }
            else
            {
                measurement.Success = false;
                measurement.ErrorMessage = "Empty HTTP response";
            }
        }
        catch (OperationCanceledException)
        {
            totalSw.Stop();
            breakdown.TotalMs = totalSw.Elapsed.TotalMilliseconds;
            measurement.Success = false;
            measurement.ErrorMessage = "Request timed out";
        }
        catch (Exception ex)
        {
            totalSw.Stop();
            breakdown.TotalMs = totalSw.Elapsed.TotalMilliseconds;
            measurement.Success = false;
            measurement.ErrorMessage = ex.GetBaseException().Message;
        }

        return measurement;
    }
}
