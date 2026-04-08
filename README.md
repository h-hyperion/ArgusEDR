![Status](https://img.shields.io/badge/status-WORK_IN_PROGRESS-red?style=for-the-badge)
![Stability](https://img.shields.io/badge/stability-experimental-orange?style=for-the-badge)
![Production](https://img.shields.io/badge/production_use-NOT_RECOMMENDED-red?style=for-the-badge)

# Argus EDR v2.1

**A self-defending Endpoint Detection and Response system for Windows.**

> # ⚠️ WORK IN PROGRESS — DO NOT USE IN PRODUCTION
>
> **Argus EDR is an active personal project under heavy development.** It is **not** a finished, audited, or production-ready security product. Features may be incomplete, broken, or change without notice.
>
> **Installing Argus makes real, invasive changes to your system:**
> - Runs a Windows Service as `NT AUTHORITY\SYSTEM`
> - Registers with Windows Security Center as an antivirus provider
> - Modifies protected registry keys (Privacy Guard touches 26 of them)
> - Intercepts file system operations and quarantines files
> - Changes DNS configuration
>
> **Do not install Argus on any machine you rely on.** Use a disposable virtual machine or a dedicated test box. Do not use it as your primary endpoint protection. If something breaks your system, it's on you to recover.
>
> If you're here to read the code, learn from it, or contribute — welcome. If you're looking for a drop-in Defender replacement, **this is not that yet**.

Argus is a lightweight, open-source EDR built on .NET 8 that provides real-time threat detection, automated response, privacy hardening, and self-healing capabilities — all without requiring a kernel driver.

---

## Features

### Threat Detection
- **YARA Rule Engine** — scan files against custom YARA signatures for known malware patterns
- **AMSI Integration** — detect fileless and script-based attacks (PowerShell, VBScript, JScript) through the Windows Antimalware Scan Interface
- **Pattern Recognition** — heuristic analysis for suspicious file characteristics (entropy, packing, anomalous headers)
- **ETW Monitoring** — real-time process creation, termination, and registry change tracking via Event Tracing for Windows

### Real-Time Protection
- **File System Monitor** — continuous monitoring of critical directories with automatic threat scanning on file creation/modification
- **Event Pipeline** — high-throughput bounded event channel (50K capacity) with path-based deduplication
- **Quarantine Store** — AES-256-GCM encrypted quarantine with SHA-256 integrity verification and one-click restore
- **Windows Security Center** — registers as an antivirus provider in Windows Security for unified status reporting

### Privacy Guard
- **26 Privacy Toggles** across 5 categories (Telemetry, Advertising, Cloud, Location, Diagnostics)
- **Tamper Monitor** — continuously watches for unauthorized changes to privacy settings and re-enforces them immediately
- **DNS Protection** — configurable DNS-over-HTTPS with encrypted profile management

### Self-Defense & Recovery
- **Watchdog Service** — Windows Service running as SYSTEM that monitors all Argus components
- **Canary Files** — hidden tamper-detection files with HMAC-signed manifests to detect binary tampering
- **Safe Mode** — automatic lockdown with forensic report generation when tampering is detected
- **Encrypted Backups** — AES-256-GCM backup/restore of critical configuration with DPAPI-protected keys
- **Service Auto-Recovery** — Windows SCM failure actions configured for automatic restart

### Threat Intelligence
- **VirusTotal Integration** — hash-based file reputation lookups
- **AbuseIPDB Integration** — IP reputation scoring for network indicators
- **TTL Cache** — local caching layer to minimize API calls and improve response time

### GUI Dashboard
- **Modern Dark Theme** — professional black/gold/red interface built with WPF and MVVM
- **System Tray Integration** — color-coded shield icon (green/gold/red) with right-click quick actions
- **Toast Notifications** — native Windows 10/11 alerts for threats and status changes via Action Center
- **Explorer Context Menu** — right-click any file or folder to "Scan with Argus EDR"
- **Real-Time Dashboard** — live threat counts, system health, and module status
- **On-Demand Scanner** — manual file and directory scanning with progress tracking
- **Privacy Guard Panel** — visual toggle management for all 26 privacy settings
- **Quarantine Manager** — browse, restore, or permanently delete quarantined threats
- **Settings** — startup toggle, DNS protection, threat intel API keys, and scan preferences

---

## Architecture

```
┌──────────────────────────────────────────────────┐
│                   Argus.GUI                      │
│          WPF Dashboard (standard user)           │
└──────────────────┬───────────────────────────────┘
                   │ Named Pipe + HMAC-SHA256
┌──────────────────▼───────────────────────────────┐
│              Argus.Watchdog                       │
│        Windows Service (SYSTEM)                  │
│   ACL-secured pipe server, service orchestration │
└──┬──────────┬──────────┬──────────┬──────────────┘
   │          │          │          │
   ▼          ▼          ▼          ▼
Defender   Scanner   Recovery   Engine
 - ETW      - Deep     - Backup   - YARA
 - FSMon    - Scan     - Canary   - AMSI
 - Guard    - IScan    - Safe     - Pattern
 - Quarant.   Engine     Mode       Recog.
```

| Project | Description |
|---------|-------------|
| **Argus.Core** | Shared models, IPC protocol, constants |
| **Argus.Engine** | YARA, AMSI, and pattern recognition scan engines |
| **Argus.Scanner** | DeepScanner orchestrator with IScanEngine abstraction |
| **Argus.Defender** | Real-time protection: ETW, file monitoring, quarantine, Privacy Guard |
| **Argus.Watchdog** | Windows Service — pipe server, service health, safe mode activation |
| **Argus.Recovery** | Backup/restore, canary files, safe mode controller |
| **Argus.GUI** | WPF dashboard with MVVM ViewModels and IPC bridge |

---

## Installation

### Quick Install (PowerShell)

Run the following command in an **elevated PowerShell** terminal (Run as Administrator):

```powershell
irm h-hyperion.github.io/ArgusEDR/install | iex
```

> **Why admin?** Argus installs a Windows Service that runs as `SYSTEM`, registers with Windows Security Center as an antivirus provider, modifies protected registry keys for Privacy Guard, and writes to `C:\Program Files\Argus\`. All of these operations require administrator privileges — this is standard for any real endpoint security product.

This single command handles everything:
- Detects whether Argus is already installed
- **Fresh install** — downloads the latest release, creates directories, generates DPAPI-protected IPC keys, installs and starts the Windows Service, creates Desktop and Start Menu shortcuts, registers auto-start, and adds an Explorer context menu for "Scan with Argus EDR"
- **Repair mode** — if a safe mode sentinel is detected, reinstalls binaries while preserving configuration
- **Status check** — if Argus is healthy, reports current version and service status

After installation, Argus EDR launches minimized to the system tray. Look for the shield icon near your clock.

### Uninstall

```powershell
# Download and run with -Uninstall flag:
.\argus.ps1 -Uninstall
```

The uninstaller offers options to archive logs and revert Privacy Guard registry changes before removal.

### Prerequisites

- **Windows 10/11** (x64)
- **.NET 8 Runtime** — the installer will attempt to install this automatically via `winget` if not present
- **Administrator privileges** — required for service installation and Privacy Guard registry access

---

## IPC Security Model

Communication between the GUI (standard user) and the Watchdog service (SYSTEM) uses:
- **Named Pipes** with ACL-restricted access
- **HMAC-SHA256** message authentication (4-byte length prefix + JSON payload + 32-byte HMAC)
- **DPAPI LocalMachine** scope for key protection — the HMAC key is generated during installation and protected with Windows Data Protection API

---

## Configuration

| File | Location | Purpose |
|------|----------|---------|
| `GuardConfig.json` | `C:\ProgramData\Argus\Config\` | Privacy Guard toggle states |
| `ipc.key` | `C:\ProgramData\Argus\Config\` | DPAPI-protected HMAC key |
| YARA rules | `C:\ProgramData\Argus\YARA\` | Custom `.yar` rule files |
| Logs | `C:\ProgramData\Argus\Logs\` | Rolling daily log files (7-day retention) |
| Quarantine | `C:\ProgramData\Argus\Quarantine\` | AES-256-GCM encrypted threat files |

---

## Building from Source

```bash
git clone https://github.com/h-hyperion/ArgusEDR.git
cd ArgusEDR
dotnet build Argus.slnx
dotnet test Argus.slnx
```

> **Note:** YARA engine tests require `libyara.dll` (native x64) in the test output directory. All other tests pass without additional dependencies.

---

## Security

See [SECURITY.md](SECURITY.md) for the full threat model, known limitations, and responsible disclosure policy.

---

## License

This project is open source and available under the [MIT License](LICENSE).
