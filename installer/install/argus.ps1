#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Argus EDR v2.1 Smart Bootstrap

.DESCRIPTION
    Single command that handles the complete Argus lifecycle:
    - Auto-detects installation state
    - Fresh install if not present
    - Repair mode if Safe Mode sentinel detected
    - Status check if healthy
    - Uninstall with -Uninstall flag

.EXAMPLE
    # One-liner install/repair (run from elevated PowerShell):
    irm h-hyperion.github.io/ArgusEDR/install | iex

.EXAMPLE
    # Uninstall:
    .\argus.ps1 -Uninstall
#>

param(
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

# === Configuration ===
$GITHUB_OWNER = "h-hyperion"
$GITHUB_REPO = "ArgusEDR"
$ARGUS_VERSION = "2.1.1"
$PUBLISHER = "Argus EDR"

# Paths (must match Argus.Core Constants.cs)
$DATA_ROOT = "C:\ProgramData\Argus"
$STATE_DIR = Join-Path $DATA_ROOT "State"
$LOGS_DIR = Join-Path $DATA_ROOT "Logs"
$CONFIG_DIR = Join-Path $DATA_ROOT "Config"
$BACKUPS_DIR = Join-Path $DATA_ROOT "Backups"
$QUARANTINE_DIR = Join-Path $DATA_ROOT "Quarantine"
$YARA_DIR = Join-Path $DATA_ROOT "YARA"
$CANARIES_DIR = Join-Path $DATA_ROOT "Canaries"
$INSTALL_DIR = Join-Path $env:ProgramFiles "Argus"
$SENTINEL_PATH = Join-Path $STATE_DIR "argus.safemode"
$GUARD_CONFIG_PATH = Join-Path $CONFIG_DIR "GuardConfig.json"

$SERVICE_NAME = "ArgusWatchdog"
$GUI_EXE_NAME = "Argus.GUI.exe"
$WATCHDOG_EXE_NAME = "Argus.Watchdog.exe"
$DEFENDER_EXE_NAME = "Argus.Defender.exe"

# Shortcut & Registry paths
$DESKTOP_SHORTCUT = Join-Path $env:PUBLIC "Desktop\Argus EDR.lnk"
$START_MENU_DIR = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\Argus EDR"
$START_MENU_SHORTCUT = Join-Path $START_MENU_DIR "Argus EDR.lnk"
$RUN_KEY = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$UNINSTALL_KEY = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ArgusEDR"
$SHELL_FILE_KEY = "Registry::HKEY_CLASSES_ROOT\*\shell\ArgusEDR"
$SHELL_DIR_KEY = "Registry::HKEY_CLASSES_ROOT\Directory\shell\ArgusEDR"

# === Helpers ===
function Write-Msg {
    param([string]$Message, [string]$Level = "INFO")
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "SUCCESS" { "Green" }
        "WARNING" { "Yellow" }
        "ERROR"   { "Red" }
        "HEADER"  { "Cyan" }
        default   { "White" }
    }
    Write-Host "[$ts] [$Level] $Message" -ForegroundColor $color
}

# === Manifest Generation ===
function New-Manifest {
    param(
        [string]$InstallDir,
        [string]$OutputPath
    )

    Write-Msg "Generating manifest.json..." "INFO"

    $files = @{}
    Get-ChildItem $InstallDir -Filter "*.exe" -ErrorAction SilentlyContinue | ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        $files[$_.Name] = "sha256:$hash"
    }

    @{
        version = $ARGUS_VERSION
        files   = $files
    } | ConvertTo-Json | Set-Content $OutputPath -Encoding UTF8

    Write-Msg "manifest.json written ($($files.Count) entries)" "SUCCESS"
}

# === Mode Detection ===
function Get-ArgusMode {
    $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if (-not $svc) { return "not_installed" }
    if (Test-Path $SENTINEL_PATH) { return "repair" }
    return "healthy"
}

# === Prerequisites ===
# Argus ships self-contained: the Watchdog and GUI exes bundle the .NET 8 runtime.
# No runtime probe is needed. Kept as a stub so callers don't have to change.
function Test-DotNetRuntime {
    return $true
}

# === GitHub Release Download ===
function Get-LatestRelease {
    Write-Msg "Fetching latest release from GitHub..." "INFO"

    $apiUrl = "https://api.github.com/repos/$GITHUB_OWNER/$GITHUB_REPO/releases/latest"
    $headers = @{ "User-Agent" = "ArgusBootstrap/2.1" }

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers
    } catch {
        Write-Msg "Failed to fetch release info: $_" "ERROR"
        return $null
    }

    $asset = $release.assets | Where-Object { $_.name -match "win-x64\.zip" } | Select-Object -First 1
    if (-not $asset) {
        Write-Msg "No win-x64 release asset found" "ERROR"
        return $null
    }

    $zipPath = Join-Path $env:TEMP "Argus-$($release.tag_name).zip"
    Write-Msg "Downloading $($asset.name) ($([math]::Round($asset.size/1MB,1)) MB)..." "INFO"

    try {
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing
    } catch {
        Write-Msg "Download failed: $_" "ERROR"
        return $null
    }

    Write-Msg "Download complete" "SUCCESS"
    return $zipPath
}

# === Directory Setup ===
function Initialize-ArgusDirectories {
    Write-Msg "Initializing Argus directories..." "INFO"

    $dirs = @($DATA_ROOT, $STATE_DIR, $LOGS_DIR, $CONFIG_DIR, $BACKUPS_DIR, $QUARANTINE_DIR, $YARA_DIR, $CANARIES_DIR)
    foreach ($dir in $dirs) {
        if (-not (Test-Path $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            Write-Msg "Created: $dir"
        }
    }

    # Generate IPC HMAC key if missing
    $ipcKeyPath = Join-Path $CONFIG_DIR "ipc.key"
    if (-not (Test-Path $ipcKeyPath)) {
        Add-Type -AssemblyName System.Security
        $hmacKey = [byte[]]::new(32)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($hmacKey)
        $protectedKey = [System.Security.Cryptography.ProtectedData]::Protect(
            $hmacKey, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
        [System.IO.File]::WriteAllBytes($ipcKeyPath, $protectedKey)
        Write-Msg "Generated IPC HMAC key (DPAPI-protected)"
    }

    # Create default GuardConfig if missing
    if (-not (Test-Path $GUARD_CONFIG_PATH)) {
        $defaultConfig = @{
            Toggles = @(
                @{ Id = "telemetry_diagtrack"; Name = "Connected User Experiences"; Category = "Telemetry"; Enabled = $false },
                @{ Id = "ads_advertising_id"; Name = "Advertising ID"; Category = "Advertising"; Enabled = $false },
                @{ Id = "cloud_cortana"; Name = "Cortana"; Category = "Cloud"; Enabled = $false }
            )
        }
        $defaultConfig | ConvertTo-Json -Depth 10 | Set-Content $GUARD_CONFIG_PATH
        Write-Msg "Created default GuardConfig.json"
    }

    Write-Msg "Directory initialization complete" "SUCCESS"
}

# === Service Management ===
function Install-WindowsService {
    param([string]$BinaryPath)

    $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Msg "Service already exists" "WARNING"
        return
    }

    Write-Msg "Creating Windows service..." "INFO"
    sc.exe create $SERVICE_NAME binPath= "$BinaryPath" start= auto obj= LocalSystem DisplayName= "Argus EDR Watchdog" 2>$null | Out-Null
    sc.exe description $SERVICE_NAME "Argus EDR integrity monitor" 2>$null | Out-Null
    sc.exe failure $SERVICE_NAME reset= 0 actions= restart/1000/restart/1000/restart/1000 2>$null | Out-Null
    sc.exe failureflag $SERVICE_NAME 1 2>$null | Out-Null

    Write-Msg "Service installed successfully" "SUCCESS"
}

function Remove-WindowsService {
    $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        Stop-Service -Name $SERVICE_NAME -Force -ErrorAction SilentlyContinue
    }

    sc.exe delete $SERVICE_NAME 2>$null | Out-Null
    Write-Msg "Service removed" "SUCCESS"
}

function Start-WindowsService {
    $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne 'Running') {
        Start-Service -Name $SERVICE_NAME
        Write-Msg "Service started" "SUCCESS"
    }
}

# === Shortcut Creation ===
function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$Arguments = "",
        [string]$Description = "Argus EDR",
        [string]$IconPath = ""
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    $shortcut.Description = $Description
    $shortcut.WorkingDirectory = (Split-Path $TargetPath)
    if ($IconPath) { $shortcut.IconLocation = $IconPath }
    $shortcut.Save()

    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

# === Desktop & Start Menu Integration ===
function Install-Shortcuts {
    param([string]$GuiExePath)

    Write-Msg "Creating shortcuts..." "INFO"

    # Desktop shortcut
    New-Shortcut -ShortcutPath $DESKTOP_SHORTCUT -TargetPath $GuiExePath -Description "Argus EDR — Endpoint Protection" -IconPath "$GuiExePath,0"
    Write-Msg "Desktop shortcut created" "SUCCESS"

    # Start Menu
    if (-not (Test-Path $START_MENU_DIR)) {
        New-Item -ItemType Directory -Path $START_MENU_DIR -Force | Out-Null
    }
    New-Shortcut -ShortcutPath $START_MENU_SHORTCUT -TargetPath $GuiExePath -Description "Argus EDR — Endpoint Protection" -IconPath "$GuiExePath,0"
    Write-Msg "Start Menu entry created" "SUCCESS"
}

# === Auto-Start on Login ===
function Install-AutoStart {
    param([string]$GuiExePath)

    Write-Msg "Registering auto-start..." "INFO"
    Set-ItemProperty -Path $RUN_KEY -Name "ArgusEDR" -Value "`"$GuiExePath`" --minimized" -Type String
    Write-Msg "Auto-start registered (launches minimized to tray)" "SUCCESS"
}

# === Add/Remove Programs Registration ===
function Install-AddRemovePrograms {
    param([string]$GuiExePath)

    Write-Msg "Registering in Add/Remove Programs..." "INFO"

    if (-not (Test-Path $UNINSTALL_KEY)) {
        New-Item -Path $UNINSTALL_KEY -Force | Out-Null
    }

    $installerPath = Join-Path $INSTALL_DIR "installer\argus.ps1"

    Set-ItemProperty -Path $UNINSTALL_KEY -Name "DisplayName" -Value "Argus EDR"
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "DisplayVersion" -Value $ARGUS_VERSION
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "Publisher" -Value $PUBLISHER
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "DisplayIcon" -Value "$GuiExePath,0"
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "InstallLocation" -Value $INSTALL_DIR
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$installerPath`" -Uninstall"
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "NoModify" -Value 1 -Type DWord
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "NoRepair" -Value 1 -Type DWord

    # Estimate installed size in KB
    $size = (Get-ChildItem $INSTALL_DIR -Recurse -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum / 1KB
    Set-ItemProperty -Path $UNINSTALL_KEY -Name "EstimatedSize" -Value ([math]::Round($size)) -Type DWord

    Write-Msg "Add/Remove Programs entry created" "SUCCESS"
}

# === Explorer Context Menu ===
function Install-ContextMenu {
    param([string]$GuiExePath)

    Write-Msg "Adding Explorer context menu..." "INFO"

    # Right-click on files: "Scan with Argus EDR"
    New-Item -Path $SHELL_FILE_KEY -Force | Out-Null
    Set-ItemProperty -Path $SHELL_FILE_KEY -Name "(Default)" -Value "Scan with Argus EDR"
    Set-ItemProperty -Path $SHELL_FILE_KEY -Name "Icon" -Value "$GuiExePath,0"
    New-Item -Path "$SHELL_FILE_KEY\command" -Force | Out-Null
    Set-ItemProperty -Path "$SHELL_FILE_KEY\command" -Name "(Default)" -Value "`"$GuiExePath`" --scan `"%1`""

    # Right-click on directories: "Scan with Argus EDR"
    New-Item -Path $SHELL_DIR_KEY -Force | Out-Null
    Set-ItemProperty -Path $SHELL_DIR_KEY -Name "(Default)" -Value "Scan with Argus EDR"
    Set-ItemProperty -Path $SHELL_DIR_KEY -Name "Icon" -Value "$GuiExePath,0"
    New-Item -Path "$SHELL_DIR_KEY\command" -Force | Out-Null
    Set-ItemProperty -Path "$SHELL_DIR_KEY\command" -Name "(Default)" -Value "`"$GuiExePath`" --scan `"%1`""

    Write-Msg "Explorer context menu registered" "SUCCESS"
}

# === Install Flow ===
function Install-Fresh {
    Write-Msg "=== Starting Fresh Install ===" "HEADER"

    if (-not (Test-DotNetRuntime)) {
        Write-Msg "Please install .NET 8 Runtime and try again" "ERROR"
        return
    }

    $zipPath = Get-LatestRelease
    if (-not $zipPath) {
        Write-Msg "Could not download release. Please check GitHub owner/repo settings." "ERROR"
        return
    }

    Initialize-ArgusDirectories

    # Extract
    if (Test-Path $INSTALL_DIR) {
        Remove-Item $INSTALL_DIR -Recurse -Force
    }
    Expand-Archive -Path $zipPath -DestinationPath $INSTALL_DIR -Force
    Remove-Item $zipPath -Force

    # Copy installer script into install directory for uninstall support
    $installerDest = Join-Path $INSTALL_DIR "installer"
    if (-not (Test-Path $installerDest)) {
        New-Item -ItemType Directory -Path $installerDest -Force | Out-Null
    }
    Copy-Item $MyInvocation.ScriptName -Destination (Join-Path $installerDest "argus.ps1") -Force -ErrorAction SilentlyContinue

    $watchdogPath = Join-Path $INSTALL_DIR $WATCHDOG_EXE_NAME
    $guiPath     = Join-Path $INSTALL_DIR $GUI_EXE_NAME
    $manifestPath = Join-Path $INSTALL_DIR "manifest.json"

    # Generate manifest covering all installed exes (Watchdog, Defender, GUI)
    New-Manifest -InstallDir $INSTALL_DIR -OutputPath $manifestPath

    # Install Windows Service (Watchdog only; Defender is spawned by Watchdog)
    Install-WindowsService -BinaryPath $watchdogPath
    Start-WindowsService

    # Install GUI integration
    Install-Shortcuts -GuiExePath $guiPath
    Install-AutoStart -GuiExePath $guiPath
    Install-AddRemovePrograms -GuiExePath $guiPath
    Install-ContextMenu -GuiExePath $guiPath

    # Launch GUI minimized to tray
    Write-Msg "Launching Argus EDR..." "INFO"
    Start-Process -FilePath $guiPath -ArgumentList "--minimized" -WindowStyle Hidden

    Write-Msg "=== INSTALL COMPLETE ===" "SUCCESS"
    Write-Msg "Argus EDR is now running. Look for the shield icon in your system tray." "INFO"
}

# === Repair Flow ===
function Repair-Argus {
    Write-Msg "=== Starting Repair ===" "HEADER"
    Write-Msg "Safe mode sentinel detected. Reinstalling..." "WARNING"

    # Stop GUI if running
    Get-Process -Name "Argus.GUI" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    Remove-WindowsService

    $zipPath = Get-LatestRelease
    if (-not $zipPath) {
        Write-Msg "Could not download release" "ERROR"
        return
    }

    # Backup config
    $configBackup = "$env:TEMP\ArgusConfig"
    if (Test-Path $CONFIG_DIR) {
        Copy-Item $CONFIG_DIR $configBackup -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Reinstall
    if (Test-Path $INSTALL_DIR) {
        Remove-Item $INSTALL_DIR -Recurse -Force
    }
    Expand-Archive -Path $zipPath -DestinationPath $INSTALL_DIR -Force
    Remove-Item $zipPath -Force

    # Restore config
    if (Test-Path $configBackup) {
        Copy-Item "$configBackup\*" $CONFIG_DIR -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $configBackup -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Remove sentinel
    if (Test-Path $SENTINEL_PATH) {
        Remove-Item $SENTINEL_PATH -Force
    }

    $watchdogPath = Join-Path $INSTALL_DIR $WATCHDOG_EXE_NAME
    $guiPath     = Join-Path $INSTALL_DIR $GUI_EXE_NAME
    $manifestPath = Join-Path $INSTALL_DIR "manifest.json"

    # Regenerate manifest after repair (binaries replaced from fresh ZIP)
    New-Manifest -InstallDir $INSTALL_DIR -OutputPath $manifestPath

    Install-WindowsService -BinaryPath $watchdogPath
    Start-WindowsService

    # Re-register GUI integration
    Install-Shortcuts -GuiExePath $guiPath
    Install-AutoStart -GuiExePath $guiPath
    Install-AddRemovePrograms -GuiExePath $guiPath
    Install-ContextMenu -GuiExePath $guiPath

    # Launch GUI
    Start-Process -FilePath $guiPath -ArgumentList "--minimized" -WindowStyle Hidden

    Write-Msg "=== REPAIR COMPLETE ===" "SUCCESS"
    Write-Msg "All files replaced. Sentinel cleared." "INFO"
}

# === Status ===
function Get-Status {
    Write-Msg "=== Argus EDR Status ===" "HEADER"

    $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if ($svc) {
        Write-Msg "Service: $($svc.Status)" "INFO"
    }

    if (Test-Path $INSTALL_DIR) {
        $version = (Get-Item (Join-Path $INSTALL_DIR $WATCHDOG_EXE_NAME) -ErrorAction SilentlyContinue).VersionInfo.ProductVersion
        Write-Msg "Version: $version" "INFO"
    }

    $guiRunning = Get-Process -Name "Argus.GUI" -ErrorAction SilentlyContinue
    Write-Msg "GUI: $(if ($guiRunning) { 'Running' } else { 'Not running' })" "INFO"

    Write-Msg "Argus EDR is healthy." "SUCCESS"
}

# === Uninstall Flow ===
function Uninstall-Argus {
    Write-Msg "=== Argus EDR - UNINSTALL ===" "HEADER"
    Write-Msg "This will remove Argus completely. Continue? (y/N)" "WARNING"
    $confirm = Read-Host
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Msg "Uninstall cancelled" "INFO"
        return
    }

    # Stop GUI process
    Write-Msg "Stopping Argus EDR GUI..." "INFO"
    Get-Process -Name "Argus.GUI" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    # Stop services
    $svc = Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -eq 'Running') {
        Write-Msg "Stopping $SERVICE_NAME service..." "INFO"
        Stop-Service -Name $SERVICE_NAME -Force -ErrorAction SilentlyContinue
    }

    # Remove Desktop shortcut
    if (Test-Path $DESKTOP_SHORTCUT) {
        Remove-Item $DESKTOP_SHORTCUT -Force
        Write-Msg "Removed Desktop shortcut" "SUCCESS"
    }

    # Remove Start Menu entry
    if (Test-Path $START_MENU_DIR) {
        Remove-Item $START_MENU_DIR -Recurse -Force
        Write-Msg "Removed Start Menu entry" "SUCCESS"
    }

    # Remove auto-start
    $runVal = Get-ItemProperty -Path $RUN_KEY -Name "ArgusEDR" -ErrorAction SilentlyContinue
    if ($runVal) {
        Remove-ItemProperty -Path $RUN_KEY -Name "ArgusEDR" -ErrorAction SilentlyContinue
        Write-Msg "Removed auto-start entry" "SUCCESS"
    }

    # Remove Add/Remove Programs entry
    if (Test-Path $UNINSTALL_KEY) {
        Remove-Item $UNINSTALL_KEY -Recurse -Force
        Write-Msg "Removed Add/Remove Programs entry" "SUCCESS"
    }

    # Remove Explorer context menu
    if (Test-Path $SHELL_FILE_KEY) {
        Remove-Item $SHELL_FILE_KEY -Recurse -Force
        Write-Msg "Removed file context menu" "SUCCESS"
    }
    if (Test-Path $SHELL_DIR_KEY) {
        Remove-Item $SHELL_DIR_KEY -Recurse -Force
        Write-Msg "Removed directory context menu" "SUCCESS"
    }

    # Deregister from Windows Security Center
    Write-Msg "Deregistering from Windows Security Center..." "INFO"
    try {
        $wscPath = "HKLM:\SOFTWARE\Microsoft\Security Center\Provider\Av\{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
        if (Test-Path $wscPath) {
            Remove-Item -Path $wscPath -Recurse -Force -ErrorAction SilentlyContinue
            Write-Msg "Removed WSC registration" "SUCCESS"
        }
    } catch {
        Write-Msg "Could not remove WSC registration (may require elevation)" "WARNING"
    }

    # Option to archive logs
    Write-Msg "Archive logs before removal? (Y/n)" "INFO"
    $archiveLogs = Read-Host
    if ($archiveLogs -ne "n" -and $archiveLogs -ne "N") {
        if (Test-Path $LOGS_DIR) {
            $archivePath = Join-Path $env:USERPROFILE "Desktop\ArgusLogs_$(Get-Date -Format 'yyyyMMdd_HHmmss').zip"
            try {
                Compress-Archive -Path "$LOGS_DIR\*" -DestinationPath $archivePath -Force -ErrorAction Stop
                Write-Msg "Logs archived to: $archivePath" "SUCCESS"
            } catch {
                Write-Msg "Could not archive logs: $_" "WARNING"
            }
        }
    }

    # Option to revert Privacy Guard
    Write-Msg "Revert Privacy Guard registry changes? (y/N)" "INFO"
    $revertGuard = Read-Host
    if ($revertGuard -eq "y" -or $revertGuard -eq "Y") {
        Write-Msg "Reverting Privacy Guard toggles..." "INFO"
        Write-Msg "Note: This resets telemetry, cloud, and advertising settings to Windows defaults" "WARNING"
        $revertKeys = @(
            "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
            "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Windows Search",
            "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
        )
        foreach ($key in $revertKeys) {
            if (Test-Path $key) {
                try { Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue } catch { }
            }
        }
        Write-Msg "Privacy Guard settings reverted" "SUCCESS"
    }

    # Remove Windows service
    if (Get-Service -Name $SERVICE_NAME -ErrorAction SilentlyContinue) {
        Write-Msg "Removing Windows service..." "INFO"
        sc.exe delete $SERVICE_NAME 2>$null | Out-Null
        Start-Sleep -Seconds 1
    }

    # Remove directories
    if (Test-Path $INSTALL_DIR) {
        Remove-Item $INSTALL_DIR -Recurse -Force
        Write-Msg "Removed: $INSTALL_DIR" "SUCCESS"
    }
    if (Test-Path $DATA_ROOT) {
        Remove-Item $DATA_ROOT -Recurse -Force
        Write-Msg "Removed: $DATA_ROOT" "SUCCESS"
    }

    Write-Msg "=== UNINSTALL COMPLETE ===" "SUCCESS"
    Write-Msg "Thank you for using Argus EDR." "INFO"
}

# === Main Entry ===
if ($Uninstall) {
    Uninstall-Argus
    exit 0
}

Write-Msg "" "INFO"
Write-Msg "  ========================================" "HEADER"
Write-Msg "         Argus EDR v$ARGUS_VERSION Bootstrap" "HEADER"
Write-Msg "  ========================================" "HEADER"
Write-Msg "" "INFO"

$mode = Get-ArgusMode

switch ($mode) {
    "not_installed" {
        Install-Fresh
    }
    "repair" {
        Repair-Argus
    }
    "healthy" {
        Get-Status
    }
}

Write-Msg "" "INFO"
