# 📋 NetPulse - Code Review Project Inventory

This document catalogs every source file included in the **NetPulse** code review package (`NetPulse_Audit.zip`). All compiled binaries, intermediate build directories (`bin/`, `obj/`, `dist/`), and caches have been excluded.

---

## 1. Solution & Build Configuration
| File Path | Description |
|---|---|
| `NetPulse.slnx` | Master Visual Studio / .NET Solution manifest linking Core, App, Installer, and Test projects. |
| `build.ps1` | Master automated PowerShell script for running tests, publishing self-contained binaries, and packaging releases. |
| `README.md` | Primary documentation covering features, architecture, UX progressive disclosure, and installation. |
| `PROJECT_INVENTORY.md` | Comprehensive inventory of all source files included in this audit archive. |

---

## 2. Architecture & Methodology Documentation (`docs/`)
| File Path | Description |
|---|---|
| `docs/ARCHITECTURE.md` | In-depth architectural design, subsystem boundaries, threading model, and vector rendering pipeline. |
| `docs/METHODOLOGY.md` | Mathematical formulas for RFC 3550 jitter, percentiles, hop analysis, bufferbloat grading, and fault matrix. |
| `docs/TEST_RESULTS.md` | Automated test execution logs verifying unit calculations and live network integration passes. |

---

## 3. Core Diagnostic Engine (`src/NetPulse.Core/`)

### 3.1 Project File
| File Path | Description |
|---|---|
| `src/NetPulse.Core/NetPulse.Core.csproj` | .NET 10.0 class library project file defining framework targets and dependencies. |

### 3.2 Models (`src/NetPulse.Core/Models/`)
| File Path | Description |
|---|---|
| `src/NetPulse.Core/Models/NetworkProtocol.cs` | Enum defining probe protocols: ICMP Echo, TCP Connect, HTTPS Handshake, and DNS Query. |
| `src/NetPulse.Core/Models/Measurement.cs` | Structured measurement result model capturing timestamp, RTT, success/failure, and HTTPS timing breakdown. |
| `src/NetPulse.Core/Models/TargetConfig.cs` | Target configuration model and reactive target metrics model (min/max/avg/median/P95/jitter/loss). |
| `src/NetPulse.Core/Models/DiagnosticResult.cs` | Diagnostic result model with category, health status, plain-English headline, cause, and evidence bullets. |
| `src/NetPulse.Core/Models/ConnectionEvent.cs` | Model representing connection anomalies (latency spikes, loss bursts, gateway degradation, route changes). |
| `src/NetPulse.Core/Models/NetworkInterfaceInfo.cs` | Model capturing active network adapter hardware status, link speed, IP addressing, and Wi-Fi radio stats. |
| `src/NetPulse.Core/Models/RouteHop.cs` | Models for hop-by-hop route tracing, persistent latency anomalies, and route change comparison events. |
| `src/NetPulse.Core/Models/BufferbloatResult.cs` | Model capturing baseline vs loaded latency deltas, bufferbloat letter grade, and queue recommendations. |
| `src/NetPulse.Core/Models/AppSettings.cs` | Configuration settings model with default diverse anycast targets and notification thresholds. |

### 3.3 Services (`src/NetPulse.Core/Services/`)
| File Path | Description |
|---|---|
| `src/NetPulse.Core/Services/MeasurementEngine.cs` | Central orchestrator coordinating non-blocking asynchronous multi-target periodic probing loops. |
| `src/NetPulse.Core/Services/NetworkDiscoveryService.cs` | Discovers active network adapter, IPv4/IPv6 gateways, DNS servers, and Windows Wi-Fi radio metrics. |
| `src/NetPulse.Core/Services/PingService.cs` | High-precision ICMP echo probing service with Stopwatch QPC timing. |
| `src/NetPulse.Core/Services/TcpProbeService.cs` | Layer 4 TCP connection establishment latency measurement service. |
| `src/NetPulse.Core/Services/HttpProbeService.cs` | Granular Layer 7 prober isolating DNS lookup, TCP handshake, TLS 1.3 negotiation, and TTFB. |
| `src/NetPulse.Core/Services/DnsProbeService.cs` | Benchmarks system DNS resolver and generates RFC 1035 UDP DNS queries directly to port 53. |
| `src/NetPulse.Core/Services/TracerouteService.cs` | TTL-stepping ICMP hop discovery service with ASN mapping and persistent latency jump detection. |
| `src/NetPulse.Core/Services/MtrService.cs` | Continuous multi-hop MTR engine tracking loss %, min, max, avg RTT, and route change events. |
| `src/NetPulse.Core/Services/RouteAnalysisService.cs` | Compares multi-destination paths, computes divergence points, and detects routing anomalies. |
| `src/NetPulse.Core/Services/BufferbloatService.cs` | Controlled 3-phase loaded latency evaluator (Baseline $\rightarrow$ Download Load $\rightarrow$ Upload Load). |
| `src/NetPulse.Core/Services/SpeedTestService.cs` | Multi-stream throughput benchmark against high-speed CDN edge nodes with traffic warnings. |
| `src/NetPulse.Core/Services/DiagnosticEngine.cs` | Multi-factor evidence-based reasoning engine attributing faults to local, ISP, DNS, or remote origins. |
| `src/NetPulse.Core/Services/EventLogService.cs` | Thread-safe chronological event timeline logger supporting severity and target filtering. |
| `src/NetPulse.Core/Services/StorageService.cs` | Local data exporter for CSV measurements, JSON snapshots, and Markdown diagnostic reports. |
| `src/NetPulse.Core/Services/WindowsIntegrationService.cs` | Manages Windows startup registry (`Run` key) and system dark/light theme detection. |

### 3.4 Statistics (`src/NetPulse.Core/Statistics/`)
| File Path | Description |
|---|---|
| `src/NetPulse.Core/Statistics/StatisticalCalculator.cs` | Mathematical implementations of RFC 3550 Interarrival Jitter, rolling mean, median, P95, and packet loss. |

---

## 4. Native Windows Desktop Application (`src/NetPulse.App/`)

### 4.1 Project & Entry Points
| File Path | Description |
|---|---|
| `src/NetPulse.App/NetPulse.App.csproj` | WPF application project file configured with Windows Desktop SDK, metadata, and app icon. |
| `src/NetPulse.App/App.xaml` | Application XAML definition merging the dark theme resource dictionary. |
| `src/NetPulse.App/App.xaml.cs` | Application startup code-behind. |
| `src/NetPulse.App/AssemblyInfo.cs` | Assembly theme info and resource dictionary location attributes. |
| `src/NetPulse.App/MainWindow.xaml` | Main window XAML with progressive disclosure navigation, overview cards, and diagnostic views. |
| `src/NetPulse.App/MainWindow.xaml.cs` | Main window code-behind managing window lifecycle, minimization, and system tray integration. |
| `src/NetPulse.App/Usings.cs` | Global using directives explicitly resolving ambiguous types between WPF and Windows Forms. |
| `src/NetPulse.App/app.ico` | High-resolution embedded application icon with network pulse waveform glyph. |

### 4.2 Controls & Themes
| File Path | Description |
|---|---|
| `src/NetPulse.App/Controls/SparklineChart.cs` | Direct2D/DirectWrite vector chart rendering real-time latency curves, area fills, and loss markers. |
| `src/NetPulse.App/Themes/DarkTheme.xaml` | Complete dark theme resource dictionary with slate backgrounds, vibrant accents, and control styles. |

### 4.3 System Tray
| File Path | Description |
|---|---|
| `src/NetPulse.App/Tray/SystemTrayManager.cs` | Taskbar tray notification icon with context menu (Open, Speed Test, Bufferbloat, Exit) and balloon tips. |

### 4.4 ViewModels (`src/NetPulse.App/ViewModels/`)
| File Path | Description |
|---|---|
| `src/NetPulse.App/ViewModels/ViewModelBase.cs` | MVVM base class implementing `INotifyPropertyChanged` and thread-safe property mutation. |
| `src/NetPulse.App/ViewModels/RelayCommand.cs` | Standard reusable `ICommand` implementation for UI user interaction bindings. |
| `src/NetPulse.App/ViewModels/MainViewModel.cs` | Central ViewModel orchestrating engine updates, MTR, test runners, navigation, and export commands. |
| `src/NetPulse.App/ViewModels/TargetMetricsViewModel.cs` | Reactive ViewModel representing individual probe targets, sparkline buffers, and status brushes. |

---

## 5. Standalone Installer & Packaging (`src/NetPulse.Installer/` & `installer/`)

### 5.1 GUI Setup Application (`src/NetPulse.Installer/`)
| File Path | Description |
|---|---|
| `src/NetPulse.Installer/NetPulse.Installer.csproj` | Project file for the standalone setup and uninstaller utility. |
| `src/NetPulse.Installer/App.xaml` | Installer application XAML entry point. |
| `src/NetPulse.Installer/App.xaml.cs` | Installer application startup code-behind. |
| `src/NetPulse.Installer/AssemblyInfo.cs` | Assembly theme attributes for the installer. |
| `src/NetPulse.Installer/MainWindow.xaml` | Dark-themed setup wizard interface with directory selection, shortcut options, and progress bar. |
| `src/NetPulse.Installer/MainWindow.xaml.cs` | Handles non-administrative installation, COM shortcut creation, registry registration, and uninstallation. |
| `src/NetPulse.Installer/app.ico` | Embedded icon for the setup executable. |

### 5.2 Scripts (`installer/`)
| File Path | Description |
|---|---|
| `installer/NetPulse.iss` | Inno Setup script for building traditional Windows installer packages. |
| `installer/Setup-NetPulse.ps1` | Standalone one-click PowerShell installation script for automated deployments. |

---

## 6. Automated Test Suite (`tests/NetPulse.Tests/`)

| File Path | Description |
|---|---|
| `tests/NetPulse.Tests/NetPulse.Tests.csproj` | xUnit test project file referencing the core engine library. |
| `tests/NetPulse.Tests/StatisticalCalculatorTests.cs` | Unit tests for RFC 3550 jitter calculation convergence, median, P95 percentiles, and loss %. |
| `tests/NetPulse.Tests/DiagnosticEngineTests.cs` | Unit tests validating the 8 diagnostic reasoning rules (Local, ISP, DNS, Bufferbloat, Target, Optimal). |
| `tests/NetPulse.Tests/RouteAnalysisTests.cs` | Unit tests for route divergence detection, persistent latency anomalies, and rate-limiting filtering. |
| `tests/NetPulse.Tests/LiveIntegrationTests.cs` | Live hardware integration tests verifying network discovery, ICMP, DNS, HTTPS breakdown, and traceroute. |
