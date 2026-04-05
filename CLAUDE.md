# Argus EDR v2.1 — Claude Context

**Self-defending EDR for Windows** — real-time protection, local pattern recognition, 26-toggle Privacy Guard, self-healing architecture.

- **License:** Free, Open Source | **OS:** Windows 10/11 (x64) | **Runtime:** .NET 8 LTS
- **Root:** `C:\Users\Cayde\Documents\Project Argus\`
- **Red team:** 28 findings remediated. See `RED_TEAM_ASSESSMENT.md` and `RED_TEAM_REMEDIATION_PLAN.md`.

---

## PRIMARY DIRECTIVE — Consult the Brain First

**Before beginning ANY task, query the Argus EDR Brain in NotebookLM with the full UUID:**

```bash
notebooklm use 25b270c2-95d8-4e9b-93f3-5ba095848f28
notebooklm ask "What does Phase X require? List files, dependencies, architecture."
notebooklm ask "What errors hit this phase before and how were they fixed?"
```

1. **NEVER read the implementation plan or LOGS.md from disk.** The Brain is the single source of truth.
2. **ALWAYS start by querying the Brain** for phase specs, file structures, DI patterns, and past errors.
3. **Ask targeted questions.** Not "give me everything" — ask for file names, class signatures, IPC contracts, DI setup.
4. **If the Brain is unavailable,** inform the user immediately. Do not proceed with guesses.
5. **This applies to ALL agents.** Pass the full UUID (`25b270c2-95d8-4e9b-93f3-5ba095848f28`) and commands to every subagent.
6. **If the Brain's answer is unclear, incomplete, or contradictory — ask again.** Never proceed on a summary that leaves you guessing about file names, method signatures, DI setup, or data flow. The Brain always has the authoritative detail. Query again with a narrower question:
   - `notebooklm ask "Show me the exact DI registration for X"` instead of accepting a paragraph overview
   - `notebooklm ask "What is the full class signature of Y, including all methods?"` instead of inferring members
   - Never hallucinate types, interfaces, or paths based on a vague response
7. **The Brain is a reference — not a code generator.** It answers questions about what to build,
   dependencies, architecture, and past errors. The actual implementation — writing code, choosing
   algorithms, structuring classes, fixing bugs — is YOUR job. Use your own skills, plugins, and
   tools to do the work. Never ask the Brain to "write the code for me" or to generate implementations.

## Session Wrap-Up — Mandatory

**Brain Notebook ID:** `25b270c2-95d8-4e9b-93f3-5ba095848f28`

At the end of **every working session** on this project — and when any phase is completed — invoke:

```
/wrapup
```

The wrap-up saves a session summary to the Argus EDR Brain as a new source. It must cover:
- **Phase worked on** (e.g. "Phase 10: Integration")
- **Files created or modified** (with paths)
- **Errors encountered** and how they were fixed
- **Decisions made** (architectural, trade-offs)
- **Open threads** for next session

This applies to ALL agents — main sessions, subagents, and parallel workers. Before dispatching subagents, instruct them to run `/wrapup` when finished. Pass the full Brain UUID to every subagent.

**This is non-negotiable.** The entire Brain-first workflow collapses if the knowledge base goes stale.

---

## Build Status

| Phase | Status | Description |
|-------|--------|-------------|
| 1-8 | DONE | Foundation through Recovery |
| 9 | DONE | GUI (WPF shell, MVVM, Dashboard, Privacy Guard) |
| 10 | DONE | Integration (end-to-end pipeline wiring, installer) |
| 11 | DONE | Security Center & DNS Protection |
| 12 | DONE | Operational Completeness & Uninstaller |
| GUI | DONE | Redesign — black/gold/red theme, MVVM views |

---

## Solution Structure

```
C:\Users\Cayde\Documents\Project Argus\
├── Argus.slnx                                 <- SDK 10.0.201 XML format
├── CLAUDE.md                              <- You are here
├── RED_TEAM_ASSESSMENT.md
├── RED_TEAM_REMEDIATION_PLAN.md
├── src\
│   ├── Argus.Core\                        <- Shared contracts, models, IPC, crypto, constants
│   ├── Argus.Watchdog\                    <- Windows Service (SYSTEM) — root of trust, orchestrator
│   ├── Argus.Engine\                      <- Shared detection: YARA, AMSI, Behavior, Similarity, Prevalence
│   ├── Argus.Scanner\                     <- Manual deep scan + file upload analysis (current user)
│   ├── Argus.Defender\                    <- Real-time monitoring (SYSTEM), Privacy Guard, EventPipeline
│   ├── Argus.Recovery\                    <- Encrypted backups, canary/tamper detection, Safe Mode (SYSTEM)
│   └── Argus.GUI\                         <- WPF MVVM application (standard user, IPC client)
├── tests\
│   ├── Argus.Core.Tests\
│   ├── Argus.Engine.Tests\
│   ├── Argus.Scanner.Tests\
│   ├── Argus.Defender.Tests\
│   └── Argus.Recovery.Tests\
└── installer\
    └── install\
        └── argus.ps1                      <- PowerShell bootstrap: fresh install / safe-mode repair / status
```

---

## Module Summaries

| Module | Role | Privilege | Detail source |
|--------|------|-----------|---------------|
| **Argus.Core** | Shared contracts, IPC models, crypto helpers, P/Invoke, constants, logging bootstrap | N/A (class lib) | Brain: `ask "What contracts and models does Core define?"` |
| **Argus.Watchdog** | Guard service: heartbeats, binary verification, IPC server, restart orchestration | SYSTEM | Brain: `ask "What is the Watchdog service implementation?"` |
| **Argus.Engine** | Detection primitives: YARA, AMSI, BehaviorExtractor, SimilarityEngine, SignatureDatabase, PrevalenceTracker | N/A (class lib) | Brain: `ask "How does the Engine implement detection?"` |
| **Argus.Scanner** | User-initiated deep scan + upload analysis, threat intel (VT, AbuseIPDB, OTX) | Current user | Brain: `ask "What does the Scanner implement?"` |
| **Argus.Defender** | Real-time monitoring (File/ETW/Registry), EventPipeline, Privacy Guard (GuardEnforcer, GuardMonitor) | SYSTEM | Brain: `ask "How does Defender monitor and enforce?"` |
| **Argus.Recovery** | Encrypted backups (AES-256-GCM), canary tamper detection, Safe Mode controller | SYSTEM | Brain: `ask "How does Recovery handle backups and Safe Mode?"` |
| **Argus.GUI** | WPF MVVM shell, dashboard, scanner UI, privacy toggles, quarantine viewer | Standard user | Brain: `ask "What is the GUI architecture and view models?"` |

---

## Quick Build Commands

```bash
dotnet build Argus.slnx                # Debug build
dotnet build Argus.slnx -c Release     # Release build
dotnet test "tests\Argus.Defender.Tests"  # Per-project for accurate counts
```

**Publish:** `dotnet publish src\Argus.<Module> -c Release -r win-x64 --self-contained false`

---

## Security Invariants (NEVER VIOLATE THESE)

1. **Fail closed:** If any scanner throws, result is `ThreatResult.Unknown` (never `Clean`). Unknown = Suspicious.
2. **IPC authentication:** Every pipe message carries a 32-byte HMAC-SHA256 signature. Invalid = rejected + logged.
3. **DPAPI for all keys:** All encryption keys use `DataProtectionScope.LocalMachine`. No plaintext keys.
4. **Bounded event processing:** All monitors feed a `Channel<MonitorEvent>` (capacity 50,000). Overflow drops oldest + logs.
5. **SQLite WAL mode:** All DB connections: `PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;`
6. **YARA rule integrity:** Rule files verified against DPAPI-protected SHA-256 manifest on load. Tampered = Safe Mode.

---

## Key File System Paths

All constants in `Argus.Core\Constants.cs`. Never hardcode.

| Path | ACL |
|------|-----|
| `C:\ProgramData\Argus\Backups\` | **SYSTEM only** |
| `C:\ProgramData\Argus\Quarantine\` | **SYSTEM only** |
| `C:\ProgramData\Argus\YARA\` | SYSTEM only |
| `C:\ProgramData\Argus\Canaries\` | SYSTEM only |
| `C:\ProgramData\Argus\State\argus.safemode` | SYSTEM only |
| `C:\ProgramData\Argus\Config\api_keys.json` | SYSTEM + Admins (keys DPAPI-encrypted) |

> **Never write to `Backups\` or `Quarantine\` from the GUI.** Route through Watchdog via IPC.

---

## Privilege Model

| Module | Privilege |
|--------|-----------|
| Watchdog, Defender, Recovery | `NT AUTHORITY\SYSTEM` |
| Scanner | Current user |
| GUI | Standard user (UAC-elevates for specific actions) |

---

## IPC Architecture

```
Argus.GUI (standard user)
        |  Named Pipe: \\.\pipe\ArgusEDR
        |  Protocol: 4-byte BE length + JSON + 32-byte HMAC
        v
Argus.Watchdog (SYSTEM) — manages Defender, invokes Recovery on demand
```

- Pipe ACL explicitly grants `BUILTIN\Users` read/write
- All message types in `Argus.Core` — never in individual modules

---

## Gotchas — Silent Failure Prevention

**1. FileSystemWatcher requires 64 KB buffer.** Default 4 KB quietly drops events under load.

**2. Named pipe ACLs require explicit user access.** SYSTEM pipes default to SYSTEM-only. Must set `PipeSecurity` for `BUILTIN\Users`.

**3. ETW is hybrid v2.1 — process + registry only.** File monitoring uses FileSystemWatcher. ETW delivers nothing if not running as SYSTEM.

**4. AMSI context is per-session.** Call `AmsiInitialize` once, `AmsiScanBuffer` per buffer, `AmsiUninitialize` at shutdown. Per-scan initialization leaks handles.

**5. Canary files must be invisible to Argus.** Add canary paths to Defender's exclusion list before starting FileSystemWatcher or get false positives every integrity check cycle.

**6. DPAPI scope must match the reading account.** All keys use `LocalMachine`. SYSTEM keys are not accessible to standard-user processes — route config through Watchdog IPC.

**7. PowerShell installer must run Administrator.** `argus.ps1` uses `#Requires -RunAsAdministrator`. Never remove this.

**8. CommunityToolkit.Mvvm requires partial classes.** Source generators need `partial class` on every ViewModel using `[ObservableProperty]` or `[RelayCommand]`. Missing `partial` causes cryptic compile errors.

**9. RegNotifyChangeKeyValue fires once.** If used (replaced by ETW in v2.1): must re-register in a persistent loop.

**10. Safe Mode is irreversible without user action.** No auto-exit logic. The sentinel is only removed by `argus.ps1 --repair`.

**11. Pattern Recognition cold start.** `SignatureDatabase` empty on first run. `PrevalenceTracker` needs ~7 days before `IsRare()` is meaningful.

**12. GuardConfig.json must ship in publish output.** `.csproj` needs `<Content Include="Guard\GuardConfig.json" CopyToOutputDirectory="PreserveNewest" />`.

**13. SQLite WAL mode is required.** Without WAL, concurrent access causes `SQLITE_LOCKED` errors appearing as silent cache misses.

**14. Sentinel file handshake between C# and PowerShell.** Both sides use `Constants.cs` — never hardcode the path.

**15. ALL projects target `net8.0-windows`, not `net8.0`.** Windows APIs (DPAPI, PipeSecurity, ETW) cascade through project references.

**16. dnYara requires native `libyara.dll`.** NuGet is C# wrapper only. Tests fail with `DllNotFoundException` without native library.

**17. `System.IO.Pipes.AccessControl` must be version 5.0.0.** Version 8.* was never published for net8.0+. `System.Security.Cryptography.ProtectedData` requires explicit 8.* NuGet (NOT built into .NET 8).

**18. Serilog is NOT transitive through project references.** Each project using `Log.*` needs its own `Serilog 3.*` package.

**19. AesGcm single-param constructor is obsolete in .NET 8.** Use `new AesGcm(key, AesGcm.TagByteSizes.MaxSize)`.

**20. `dotnet test Argus.slnx` undercounts — run per-project.** Total ~43 tests (41 pass, 2 YARA skip = native DLL). Phase 9 GUI has no tests.

**21. Windows blocks writes to `System`-attributed files.** Call `File.SetAttributes(path, FileAttributes.Normal)` before writing test tamper canaries.

**22. Implementation plan has 12 phases, not 10.** Always query the Brain for correct phase numbers.

**23. Parallel subagent dispatch works for independent tasks.** One agent handles csproj changes + template deletion; the other creates new files.

**24. Plan code samples may have wrong `using` statements.** Always verify plan imports against actual usage.

**25. GuardEnforcer.ApplyAll() takes GuardConfig, not IEnumerable.** Wrap selected toggles in `new GuardConfig { Toggles = list }`.

> **G26-G29 are in the Brain's LOGS.md.** Query: `"What additional gotchas are in the development logs?"`

---

## Performance Constraints

| Resource | Limit |
|----------|-------|
| CPU | Scanner pauses at 60% |
| Memory | 512 MB total, warn at 400 MB |
| Real-time latency | < 1 second per file |
| EventPipeline | 50,000 capacity, overflow drops oldest |

---

## Coding Conventions

- Standard C# naming (PascalCase classes/properties, camelCase locals/parameters)
- All I/O must be `async`/`await` — never `.Result` or `.Wait()`
- `CancellationToken` on all long-running operations
- P/Invoke centralized in `Argus.Core\NativeMethods.cs` only
- Serilog exclusively for logging — no `Console.WriteLine` or `Debug.WriteLine`
- MVVM strict: ViewModels manage UI state only, no business logic
- `using` for all `IDisposable` (handles, streams, ETW sessions)
- Constants in `Argus.Core\Constants.cs` — no magic strings
- **No em dashes (—) in UI text — use hyphen-minus (-) only**

---

## Recommended Skills

| When | Skill |
|------|-------|
| Starting any feature branch | `superpowers:using-git-worktrees` |
| Multi-step plan execution | `superpowers:executing-plans` |
| Independent tasks available | `superpowers:subagent-driven-development` |
| Before new features | `superpowers:brainstorming` |
| Before writing code | `superpowers:test-driven-development` |
| Bugs or test failures | `superpowers:systematic-debugging` |
| Before claiming complete | `superpowers:verification-before-completion` |
| After completing a phase | `superpowers:requesting-code-review` → then `/wrapup` |
| After writing code | `simplify` |
| After CLAUDE.md changes | `claude-md-management:revise-claude-md` |
| End of every session | **`/wrapup`** — MANDATORY. Saves session summary to the Argus EDR Brain. |
