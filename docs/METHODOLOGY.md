# 🔬 NetPulse Measurement Methodology & Statistical Definitions

## 1. Measurement Validity & Multi-Protocol Philosophy

A fundamental flaw of basic ping utilities is the assumption that an ICMP echo response accurately represents real-world Internet connection health.

### Why ICMP Ping Alone is Insufficient:
1. **Control-Plane Deprioritization:** In modern carrier and enterprise routers, transit packets are forwarded entirely in dedicated hardware (ASICs/NPUs) on the *data plane*. Generating an ICMP Echo Reply requires handing the packet to the router's main CPU on the *control plane*. Many routers rate-limit or deprioritize ICMP generation to protect their CPU, resulting in synthetic packet loss or artificial latency spikes that do not affect user traffic.
2. **Protocol Discrimination:** Firewalls, ISPs, and carrier middleboxes frequently drop or rate-limit ICMP while allowing TCP and HTTPS traffic to pass unimpeded.
3. **Asymmetric Routing:** The path taken by packets from your PC to a destination often differs from the return path taken by response packets.

### The NetPulse Multi-Protocol Solution:
NetPulse measures connection quality across multiple independent layers:
- **Layer 3 (ICMP Echo):** Baseline reachability and hop discovery.
- **Layer 4 (TCP SYN/ACK Handshake):** Measures Layer 4 connection establishment to standard ports (80, 443).
- **Layer 7 (HTTPS Handshake & TTFB):** Isolates DNS lookup, TCP connect, TLS 1.3 cryptographic negotiation, and Time to First Byte (TTFB).
- **DNS Resolution (UDP Port 53):** Benchmarks system and public resolver query execution times using raw RFC 1035 wire packets.

---

## 2. Statistical Calculations

### 2.1 RFC 3550 Interarrival Jitter
NetPulse rejects the naive definition of jitter as $\text{Max Latency} - \text{Min Latency}$, which merely reflects outliers rather than latency variation.

Instead, NetPulse implements the standard statistical interarrival jitter defined in **RFC 3550 Section 6.4.1** (used globally in VoIP, RTP, and WebRTC streaming):

Given consecutive latency measurements $L_{i-1}$ and $L_i$:
$$D(i-1, i) = |L_i - L_{i-1}|$$

The smoothed jitter $J_i$ is updated recursively using an exponentially weighted moving average with smoothing factor $\alpha = \frac{1}{16}$:
$$J_i = J_{i-1} + \frac{D(i-1, i) - J_{i-1}}{16}$$

If $J_0$ is uninitialized, $J_0 = D(0, 1)$.

### 2.2 Rolling Median & Percentiles (P95)
Network latency distributions are typically skewed with long right tails. While rolling arithmetic means are displayed for reference, NetPulse prioritizes **Median (P50)** and **95th Percentile (P95)**:

For a sorted sample array $X$ of size $N$ and percentile $p \in [0, 1]$:
$$\text{Rank} = p \times (N - 1)$$
$$k = \lfloor \text{Rank} \rfloor, \quad d = \text{Rank} - k$$
$$P_p = X[k] + d \times (X[k+1] - X[k])$$

P95 represents the latency threshold below which 95% of all network probes fell, effectively identifying intermittent lag spikes without being distorted by a single timeout.

### 2.3 Packet Loss Percentage
$$\text{Loss } (\%) = \left( \frac{\text{Failed Probes}}{\text{Total Probes}} \right) \times 100$$

---

## 3. Hop-by-Hop Path & Route Diagnostics

### 3.1 Control-Plane vs Transit Packet Loss
When analyzing hop-by-hop traceroute / MTR output:
- **Case A (Control-Plane ICMP Rate-Limiting):**
  $$\text{Hop } 3: 45\text{ ms (80\% Loss)}, \quad \text{Hop } 4: 12\text{ ms (0\% Loss)}, \quad \text{Destination}: 14\text{ ms (0\% Loss)}$$
  *Interpretation:* Hop 3 is rate-limiting ICMP responses. Because subsequent hops and the destination exhibit 0% loss, transit traffic across Hop 3 is completely unaffected.
- **Case B (True Path Degradation):**
  $$\text{Hop } 3: 12\text{ ms (0\% Loss)}, \quad \text{Hop } 4: 85\text{ ms (15\% Loss)}, \quad \text{Destination}: 88\text{ ms (15\% Loss)}$$
  *Interpretation:* Packet loss and latency introduced at Hop 4 propagate through all downstream hops and directly affect the destination. NetPulse flags Hop 4 with `⚠️ Sustained Jump`.

### 3.2 Route Divergence Point
When comparing paths toward two destinations $A$ and $B$:
$$\text{Hops}_A = [h_{A,1}, h_{A,2}, \dots, h_{A,n}]$$
$$\text{Hops}_B = [h_{B,1}, h_{B,2}, \dots, h_{B,m}]$$

The divergence hop $k$ is the smallest index where:
$$\text{IP}(h_{A,k}) \neq \text{IP}(h_{B,k})$$

Common hops $[1 \dots k-1]$ represent shared local and ISP aggregation paths, while hops beyond $k$ indicate where routing diverges to transit providers or destination networks.

---

## 4. Bufferbloat & Loaded Latency Methodology

### 4.1 Test Phases
1. **Unloaded Baseline:** Probe target 8 times over 1 second in an idle state. Calculate baseline median latency $L_{\text{base}}$.
2. **Download Saturation:** Initiate 4 parallel HTTP GET chunk streams from high-capacity CDN edge servers, measuring latency concurrently every 150ms. Calculate loaded download latency $L_{\text{dl}}$ and delta $\Delta_{\text{dl}} = \max(0, L_{\text{dl}} - L_{\text{base}})$.
3. **Upload Saturation:** Initiate parallel HTTP POST uploads with random 256KB binary payloads, measuring latency concurrently. Calculate loaded upload latency $L_{\text{ul}}$ and delta $\Delta_{\text{ul}} = \max(0, L_{\text{ul}} - L_{\text{base}})$.

### 4.2 Grading Scale
$$\Delta_{\max} = \max(\Delta_{\text{dl}}, \Delta_{\text{ul}})$$

- **A+ ($\le 5\text{ ms}$):** Ideal. Perfect queue discipline.
- **A ($5 - 15\text{ ms}$):** Excellent. Minimal buffering.
- **B ($15 - 30\text{ ms}$):** Good. Minor transient queuing.
- **C ($30 - 60\text{ ms}$):** Noticeable. Interactive gaming/video calls degrade under load.
- **D ($60 - 150\text{ ms}$):** Poor. Heavy buffer bloat.
- **F ($> 150\text{ ms}$):** Critical. Router buffers hold hundreds of milliseconds of data.

---

## 5. Diagnostic Evidence Attribution Matrix

| Gateway Health | Internet Targets | DNS Resolver | Bufferbloat Delta | Diagnostic Classification |
|---|---|---|---|---|
| Latency $> 25\text{ms}$ or Loss $> 4\%$ | Degraded | Any | Any | **Local Network / Router / Wi-Fi Issue** |
| Low ($\le 5\text{ms}$), 0% Loss | High Latency / Loss on $\ge 2$ targets | Normal | Normal | **ISP / Upstream Internet Issue** |
| Low ($\le 5\text{ms}$), 0% Loss | Normal | Failing or $> 180\text{ms}$ | Normal | **DNS Resolver Issue** |
| Low ($\le 5\text{ms}$), 0% Loss | 1 Target Failing, Others Normal | Normal | Normal | **Remote Target Specific Issue** |
| Low ($\le 5\text{ms}$), 0% Loss | Normal Unloaded | Normal | Delta $> 60\text{ms}$ | **Bufferbloat / Queue Congestion** |
| Low ($\le 5\text{ms}$), 0% Loss | Normal ($\le 50\text{ms}$), 0% Loss | Normal | Delta $\le 15\text{ms}$ | **Optimal Connection** |
| Interface Down or 100% Loss | 100% Loss | 100% Loss | N/A | **Disconnected** |
| Minor fluctuations | Mild variation | Normal | Normal | **Inconclusive / Mild Variability** |
