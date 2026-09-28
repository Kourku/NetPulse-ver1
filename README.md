# ⚡ NetPulse - Real-Time Internet Connection & Path Diagnostics

> A native Windows desktop application designed to diagnose and visualize Internet connection health, identify the exact origin of network degradation, and analyze routing paths in real time.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-blue.svg)]()
[![Runtime](https://img.shields.io/badge/.NET-10.0%20(Self--Contained)-512BD4.svg)]()
[![Tests](https://img.shields.io/badge/tests-21%20passed-brightgreen.svg)]()
[![License](https://img.shields.io/badge/license-MIT-green.svg)]()
[![Privacy](https://img.shields.io/badge/telemetry-0%25%20(100%25%20Local)-success.svg)]()

---

## 📖 Table of Contents
- [Executive Overview](#-executive-overview)
- [The Core Diagnostic Problem](#-the-core-diagnostic-problem)
- [Key Features](#-key-features)
- [Progressive Disclosure UX](#-progressive-disclosure-ux)
- [Multi-Layer Measurement Architecture](#-multi-layer-measurement-architecture)
- [Hop-by-Hop Path Discovery & Continuous MTR](#-hop-by-hop-path-discovery--continuous-mtr)
- [Bufferbloat & Loaded Latency Engine](#-bufferbloat--loaded-latency-engine)
- [Download & Installation](#-download--installation)
- [Building from Source](#-building-from-source)
- [Documentation Links](#-documentation-links)

---

## 🎯 Executive Overview

Most network utilities merely send ICMP echo packets to a single remote address (like `8.8.8.8`) and show a simplistic ping number. If the ping is high, standard users have no idea where the problem actually lies:
* Is it their computer's Wi-Fi connection?
* Is it their home router?
* Is their ISP experiencing congestion?
* Is their DNS resolver slow or failing?
* Is a specific remote server overloaded?
* Is someone in the household downloading a large file (Bufferbloat)?

**NetPulse** was engineered from the ground up to solve this attribution problem. It treats the Internet connection as an end-to-end chain:

```
[ PC / Wi-Fi Adapter ] ──► [ Home Router / Gateway ] ──► [ ISP Access ] ──► [ Transit Backbone ] ──► [ DNS / Remote Server ]
```

By probing every layer independently using multiple protocols (ICMP, TCP, HTTPS, and DNS), NetPulse continuously correlates evidence to provide plain-English, evidence-based diagnoses for everyday users while equipping network engineers with granular MTR tables, TCP/TLS handshake breakdowns, and route change tracking.

---

## 🔍 The Core Diagnostic Problem

NetPulse clearly distinguishes between:

1. **Problems on the local network (PC ↔ Router / Wi-Fi):** High gateway RTT, local packet loss, low Wi-Fi signal.
2. **Problems on the ISP or Internet path:** Local gateway responds in <1ms with 0% loss, but multiple diverse Internet targets degrade.
3. **Problems specific to DNS:** IP packets travel quickly, but domain name lookups time out or take >150ms.
4. **Problems specific to a single remote server:** Diverse global endpoints respond normally, while only one specific destination fails.
5. **Problems caused by congestion / Bufferbloat:** Unloaded latency is low, but jumps dramatically (+50ms to +200ms) under download/upload load.
6. **Problems caused by routing changes:** Upstream hop divergence coincident with a persistent latency increase.
7. **Inconclusive scenarios:** Transparently acknowledged when measurements do not provide sufficient evidence to pinpoint causality.

---

## ✨ Key Features

- **Multi-Protocol Probing:** Combines ICMP, TCP handshake, HTTPS (DNS + TCP + TLS + TTFB), and direct RFC 1035 UDP DNS queries.
- **Continuous Hop-by-Hop MTR:** Discovers every hop along the route with packet loss %, Min/Max/Avg RTT, and Autonomous System (ASN) lookup.
- **Persistent Latency Jump Detection:** Intelligently distinguishes between intermediate routers rate-limiting ICMP responses vs true downstream transit degradation.
- **RFC 3550 Interarrival Jitter:** Implements standard statistical jitter:
  $$\text{Jitter}_{new} = \text{Jitter}_{old} + \frac{|\Delta\text{RTT}| - \text{Jitter}_{old}}{16}$$
- **Full Statistical Suite:** Real-time Minimum, Maximum, Mean, Median, and 95th Percentile (P95) latency over rolling sliding windows.
- **Bufferbloat / Loaded Latency Test:** Automated 3-phase test (Baseline $\rightarrow$ Download Load $\rightarrow$ Upload Load) with letter grades (A+ to F) and router SQM recommendations.
- **On-Demand Bandwidth Benchmark:** Multi-stream throughput test with traffic warning prompt and live Mbps gauge.
- **Active Adapter & Wi-Fi Telemetry:** Reports interface name, link speed, IPv4/IPv6, gateway, DNS, and native Windows Wi-Fi metrics (SSID, signal strength %, channel, 802.11 protocol).
- **Chronological Event Timeline:** Records latency spikes, packet loss bursts, gateway degradations, and route shifts.
- **Zero-Telemetry Local Privacy:** 100% of data is stored in memory and exported on demand. No accounts, no telemetry, no cloud dependencies.
- **Native Windows Desktop Integration:** Tray icon, minimize to tray, start with Windows, and balloon notifications for critical connection events.

---

## 💡 Progressive Disclosure UX

NetPulse serves both normal users and advanced network engineers without compromise:

### Level 1: Simple Dashboard (Beginner Mode)
- **Top Status Banner:** Instant color-coded verdict (`HEALTHY`, `LOCAL ROUTER ISSUE`, `ISP DEGRADED`, `DNS ISSUE`, `DISCONNECTED`).
- **Plain-English Answers:**
  - *Is my connection working?*
  - *Is it performing normally?*
  - *Where does the problem appear to be?*
- **Core Cards:** Latency, Jitter, Packet Loss, and Local Router latency with qualitative badges ("Good", "Fair", "Degraded").

### Level 2: Contextual Explanations
- Information buttons (`ℹ`) on every metric explaining latency, jitter, packet loss, and bufferbloat in accessible terms without technical jargon.

### Level 3: Evidence-Based Problem Reports
- When issues arise, NetPulse generates human-readable diagnostics:
  - **What we found:** Measured facts (e.g. "Your router is responding normally in 1.2ms; average Internet latency is fluctuating significantly").
  - **What this probably means:** Evidence-based inference ("The issue appears to originate upstream on your ISP's network, not in your home").
  - **What you can do:** Actionable next steps ("Provide the exported diagnostic report when contacting your ISP").

### Level 4: Technical View (Advanced Mode)
- Toggle switch right on the top navigation bar unlocks:
  - Multi-target data grid with rolling sparkline history.
  - Granular HTTPS timing breakdown (DNS, TCP Connect, TLS Handshake, TTFB).
  - Hop-by-hop continuous MTR table.
  - Route divergence path comparison.
  - Raw CSV and JSON data export.

---

## 🗺️ Hop-by-Hop Path Discovery & Continuous MTR

Under the **Route & MTR** view, NetPulse conducts active path analysis:

```
Hop 1:  192.168.100.1   (LAN Gateway)            0.8 ms    0% Loss   AS: Local Network
Hop 2:  41.108.96.1     (ISP Access Gateway)     2.4 ms    0% Loss   AS: Telecom Access
Hop 3:  10.104.15.13    (ISP Backbone)           2.9 ms    0% Loss   AS: Private Core
Hop 4:  *               (ICMP Filtered Router)     --    100% Loss   [No Transit Impact]
Hop 5:  1.1.1.1         (Cloudflare Edge)        4.1 ms    0% Loss   AS13335 (Cloudflare)
```

### Anomaly Rules:
1. **Control-Plane vs Forwarding-Plane:** If Hop 4 does not respond to ICMP, but Hop 5 responds in 4.1ms with 0% loss, NetPulse flags that Hop 4 is rate-limiting ICMP responses and that transit traffic is unaffected.
2. **Persistent Latency Jumps:** If a sudden +60ms latency jump occurs at Hop 3 and persists across all subsequent hops to the destination, NetPulse highlights that hop with `⚠️ Sustained Jump`.
3. **Route Divergence Analysis:** Compare two destinations (e.g., Cloudflare vs Google) to pinpoint the exact common hops and the hop where the paths diverge.

---

## ⚖️ Bufferbloat & Loaded Latency Engine

Bufferbloat is high latency caused by excessive buffering in consumer home routers during large downloads or uploads:

| Grade | Latency Increase | Assessment |
|:---:|:---:|:---|
| **A+** | $\le 5\text{ ms}$ | Pristine. Zero perceptible lag while downloading. |
| **A** | $5 - 15\text{ ms}$ | Excellent. Minimal buffering. |
| **B** | $15 - 30\text{ ms}$ | Good. Minor latency buildup. |
| **C** | $30 - 60\text{ ms}$ | Noticeable. Gaming or voice calls may stutter during downloads. |
| **D** | $60 - 150\text{ ms}$ | High bloat. Smart Queue Management (SQM) recommended. |
| **F** | $> 150\text{ ms}$ | Severe bufferbloat. Packets are delayed hundreds of milliseconds. |

---

## 📦 Download & Installation

The application is completely self-contained. **No .NET SDK or runtime installation is required.**

### 1. Portable Version (Zero Install)
1. Download `NetPulse-1.0.0-Portable-win-x64.zip` from `dist/`.
2. Extract the archive anywhere on your machine.
3. Launch `NetPulse.exe`.

### 2. Standalone Windows Installer (`NetPulse-Setup.exe`)
1. Run `dist/installer/NetPulse-Setup.exe`.
2. Click **Install**.
3. NetPulse installs to `%LocalAppData%\Programs\NetPulse` without requiring administrator privileges, creates Desktop and Start Menu shortcuts, and registers with Windows Add/Remove Programs.
4. To uninstall, run `NetPulse-Setup.exe --uninstall` or use Windows **Installed Apps**.

### 3. PowerShell One-Click Setup
Right-click `installer/Setup-NetPulse.ps1` and select **Run with PowerShell**, or execute:
```powershell
powershell.exe -ExecutionPolicy Bypass -File .\installer\Setup-NetPulse.ps1 -Launch
```

---

## 🛠️ Building from Source

NetPulse targets modern **.NET 10.0-windows** with high-performance native WPF rendering.

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11 (x64)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/)

### Build & Run Tests
```powershell
# Restore and build the solution
dotnet build NetPulse.sln

# Run all 21 automated unit and integration tests
dotnet test tests/NetPulse.Tests/NetPulse.Tests.csproj

# Run the master automated build & release packager
.\build.ps1
```

---

## 📚 Documentation Links

- [Architecture & Design Document](file:///e:/LAGG/docs/ARCHITECTURE.md)
- [Measurement Methodology & Statistical Definitions](file:///e:/LAGG/docs/METHODOLOGY.md)
- [Automated Test Execution Results](file:///e:/LAGG/docs/TEST_RESULTS.md)

---

*NetPulse Diagnostics • Built for precision, clarity, and reliability.*
