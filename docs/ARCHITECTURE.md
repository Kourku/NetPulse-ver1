# 🏛️ NetPulse Architecture & Technical Design

## 1. Technology Stack Selection Rationale

When engineering a continuous background network diagnostic tool for Windows, the choice of technology dictates reliability, CPU/RAM footprint, and diagnostic precision:

| Technology Considered | Pros | Cons | Decision |
|---|---|---|---|
| **Electron / Web App Wrapper** | Rapid web UI prototyping | Massive RAM footprint (150MB - 300MB idle), heavy background CPU usage, poor low-level networking API access, cannot perform raw TTL manipulation or low-level socket timing without heavy C++ native addons. | ❌ Rejected |
| **C++ / Win32 Native** | Minimal footprint, direct Win32 API access | High development complexity, complex UI maintenance, slower iteration. | ⚖️ Considered |
| **.NET 10.0 + WPF Native** | Ultra-low idle CPU (<0.2%), low memory footprint (~40MB), high-precision `Stopwatch` (QPC), direct access to `System.Net.NetworkInformation.Ping`, `Socket`, `SslStream`, native Win32 registry and tray integration, self-contained single-file deployment (`win-x64`). | Requires Windows OS (by design). | ✅ **Selected** |

---

## 2. Architectural Blueprint

NetPulse follows a strict separation of concerns across decoupled architectural layers:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        USER PRESENTATION LAYER                         │
│   MainWindow.xaml   •   DarkTheme.xaml   •   SparklineChart (Vector)   │
│   Progressive Disclosure: [ Beginner Mode ] <---> [ Advanced Mode ]   │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Data Binding (MVVM)
┌───────────────────────────────────▼────────────────────────────────────┐
│                          VIEW MODEL LAYER                              │
│   MainViewModel  •  TargetMetricsViewModel  •  RelayCommand            │
└──────────────┬────────────────────┬───────────────────┬────────────────┘
               │                    │                   │
┌──────────────▼───────┐ ┌──────────▼─────────┐ ┌───────▼────────────────┐
│  MEASUREMENT ENGINE  │ │ ROUTING SUBSYSTEM  │ │ LOAD & DIAGNOSTICS     │
│  • Periodic Loop     │ │ • TracerouteService│ │ • BufferbloatService   │
│  • PingService (ICMP)│ │ • MtrService (MTR) │ │ • SpeedTestService     │
│  • TcpProbeService   │ │ • RouteAnalysis    │ │ • DiagnosticEngine     │
│  • HttpProbeService  │ │ • Cymru ASN Lookup │ │ • EventLogService      │
│  • DnsProbeService   │ │                    │ │ • StorageService       │
└──────────────┬───────┘ └──────────┬─────────┘ └───────┬────────────────┘
               │                    │                   │
┌──────────────▼────────────────────▼───────────────────▼────────────────┐
│                   OS & HARDWARE DISCOVERY LAYER                        │
│   NetworkDiscoveryService (IP Properties, WLAN API, Win32 Netsh)       │
│   WindowsIntegrationService (System Tray, Startup Registry, Themes)     │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Core Subsystems

### 3.1 MeasurementEngine & Threading Model
- **Non-blocking Asynchronous Probing:** Probes execute on thread pool threads using `Task.WhenAll`. The UI thread is never blocked during network I/O.
- **High-Resolution Timing:** Timing is measured using `System.Diagnostics.Stopwatch`, which interfaces with the Windows High-Precision Event Timer / QueryPerformanceCounter (QPC). Latencies are recorded with sub-millisecond precision.
- **Dynamic Gateway Binding:** When network state changes occur (e.g. laptop switching between Wi-Fi and Ethernet), `NetworkChange.NetworkAddressChanged` triggers dynamic re-resolution of the default gateway IP without requiring an application restart.

### 3.2 Protocol-Specific Probing Pipeline

1. **ICMP Echo (`PingService`):**
   - Configured with 32-byte standardized payload and `Don't Fragment` flag.
   - Measures pure Layer 3 round-trip time.
2. **TCP Socket Connect (`TcpProbeService`):**
   - Initiates an asynchronous TCP three-way handshake (`SYN` $\rightarrow$ `SYN/ACK` $\rightarrow$ `ACK`) directly to destination ports (e.g. 80, 443, 53) using unbuffered sockets.
   - Safely closes connection immediately upon completion.
3. **HTTPS Full Breakdown (`HttpProbeService`):**
   - Dissects HTTP/TLS performance into 4 sequential phases:
     1. DNS Resolution Time
     2. TCP Handshake Time
     3. TLS 1.2/1.3 Handshake Time (`SslStream`)
     4. Time to First Byte (TTFB)
4. **Direct DNS Probing (`DnsProbeService`):**
   - Benchmarks system DNS resolver via `Dns.GetHostAddressesAsync`.
   - Independently constructs RFC 1035 UDP DNS query packets (Transaction ID, Flags `0x0100`, QDCOUNT `1`, QNAME, QTYPE `A`, QCLASS `IN`) sent directly to DNS IPs (e.g. `1.1.1.1` or `8.8.8.8`) on port 53.

### 3.3 Routing & Path Diagnostics (Section 23)
- **TTL-Stepping Hop Discovery:** Emits ICMP probes with incrementing `TTL` from 1 up to `maxHops` (25-30). When intermediate routers decrement TTL to 0, they return `IPStatus.TtlExpired`, revealing the router's IP address.
- **Continuous MTR Execution:** `MtrService` sweeps hops continuously, tracking packets sent, packets received, loss %, best RTT, worst RTT, and rolling average RTT.
- **Autonomous System (ASN) Resolution:** Resolves Autonomous System numbers (e.g. AS13335 Cloudflare, AS15169 Google, AS8075 Microsoft) through instant offline classification for major transit backbones, backed by reverse DNS.
- **Control-Plane vs Forwarding-Plane Discrimination:** If an intermediate hop reports packet loss or high RTT, but subsequent hops and the final destination report 0% loss and low RTT, the hop is marked as having control-plane ICMP rate-limiting rather than transit degradation.

### 3.4 Diagnostic Reasoning Engine
- Implements an evidence-based multi-factor decision tree.
- Uses cautious, confidence-aware language ("Likely", "Consistent with", "No evidence of local network issues", "Evidence is currently inconclusive").
- Outputs a 4-tier diagnostic model: Category, Status, Plain-English Headline, and Detailed Evidence Bullets.

---

## 4. UI Vector Rendering & Zero-Allocation Pipeline

- **SparklineChart:** Custom WPF `FrameworkElement` utilizing `StreamGeometry` and Direct2D/DirectWrite hardware acceleration.
- Renders 40+ points of rolling history with smooth stroke lines, gradient fills, and packet loss alert dots with zero external chart library overhead.
- Total memory allocation per tick is bounded; rolling buffers recycle arrays to prevent garbage collection pressure.

---

## 5. Storage & Privacy Architecture

- **No Remote Telemetry:** NetPulse transmits zero data to third-party servers. All measurements reside solely in RAM within circular ring buffers.
- **Export Formats:**
  - **CSV:** Formatted timestamp, target name, protocol, latency in ms, status, resolved IP, and HTTPS breakdown.
  - **JSON:** Comprehensive snapshot of active network adapter, DNS servers, target metrics, route hops, and event log.
  - **Markdown Diagnostic Report:** Formatted executive summary, hardware profile, target statistics table, route hop analysis, bufferbloat results, and event timeline ready to provide to an ISP technician.
