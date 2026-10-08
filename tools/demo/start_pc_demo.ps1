<#
.SYNOPSIS
    Opens the three processes needed for the OPUS PC demo, each in its own window: the Flutter
    hub, the haptic sleeve simulator, and this repo's session watcher/analyser.

.DESCRIPTION
    Mirrors docs/TESTING_RUNBOOK.md section 6 ("The whole pipeline together"), but as one script
    with three separate visible windows instead of three terminals you drive by hand. The actual
    headset (real Quest, or the Unity PlayMode `FullPipelineIntegrationTests`, or
    sim/live/fake_headset.py as a fallback) is started separately -- this script only brings up
    the two peers a headset talks to, plus the watcher that turns a finished session into a
    metrics summary automatically.

    Ports match contracts/LIVE_PROTOCOL.md / contracts/HAPTIC_PROTOCOL.md exactly (hub 8787, hub
    beacon 8788, haptic sleeve 8790, haptic discovery 8791) because that is what a real headset
    (Unity or Quest) is hardcoded to look for. This script is meant to be run by a human at the
    machine for an actual demo -- do not run it from an automated/sandboxed agent session where
    those ports may already be in use by someone else's live session.

.PARAMETER AutoDrive
    Pass this to start the hub with --auto-drive (it sends assign_program + start automatically as
    soon as a headset connects -- convenient for an unattended demo rehearsal, but the real app
    normally waits for a clinician to press Start). Off by default.

.PARAMETER WithHaptics
    Pass this to also open the haptic sleeve simulator window. Off by default, since the haptic
    sleeve is optional/additive per CONTEXT.md ("Haptics are ADDITIVE ... Default off until
    hardware exists").

.PARAMETER SkipWatcher
    Pass this to skip opening the watcher window (e.g. if you'd rather run it by hand to see its
    output inline instead of in a separate window).

.EXAMPLE
    tools\demo\start_pc_demo.ps1
    Opens the hub (no auto-drive) and the watcher. Start a headset (Unity PlayMode test, real
    Quest, or fake_headset.py) separately once the hub window says it's listening.

.EXAMPLE
    tools\demo\start_pc_demo.ps1 -AutoDrive -WithHaptics
    Opens hub (auto-drive on), haptic sleeve simulator, and the watcher -- everything except the
    headset itself.
#>
[CmdletBinding()]
param(
    [switch]$AutoDrive,
    [switch]$WithHaptics,
    [switch]$SkipWatcher
)

$ErrorActionPreference = 'Stop'

# Repo root = two levels up from tools\demo\
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$AppDir = Join-Path $RepoRoot 'app'
$SimDir = Join-Path $RepoRoot 'sim'
$AnalyticsPython = Join-Path $RepoRoot 'analytics\.venv\Scripts\python.exe'
$HapticPython = Join-Path $RepoRoot 'sim\haptic\.venv\Scripts\python.exe'

# dart (Flutter), same order as tools\demo\tool_paths.py: $env:OPUS_DART, $env:FLUTTER_ROOT, PATH, H:\flutter\bin, and
# only then the old C:\flutter\bin. Flutter may still be installing, so the file is not required to exist here.
function Resolve-Dart {
    if ($env:OPUS_DART) { return $env:OPUS_DART }
    if ($env:FLUTTER_ROOT -and (Test-Path (Join-Path $env:FLUTTER_ROOT 'bin\dart.bat'))) { return (Join-Path $env:FLUTTER_ROOT 'bin\dart.bat') }
    $onPath = Get-Command dart -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { return $onPath.Source }
    if (Test-Path 'H:\flutter\bin\dart.bat') { return 'H:\flutter\bin\dart.bat' }
    return 'C:\flutter\bin\dart.bat'
}
$DartExe = Resolve-Dart

Write-Host "OPUS PC demo launcher" -ForegroundColor Cyan
Write-Host "Repo root: $RepoRoot"

if (-not (Test-Path $AppDir)) {
    throw "app/ not found under $RepoRoot -- is this script still at tools\demo\ inside the OPUS repo?"
}
if (-not (Test-Path $AnalyticsPython)) {
    throw "Analytics venv python not found at $AnalyticsPython -- see docs/TESTING_RUNBOOK.md section 0."
}

# --- 1. The clinician's hub -------------------------------------------------
$hubArgs = @('run', 'tool/hub_cli.dart', '--port', '8787')
if ($AutoDrive) {
    $hubArgs += '--auto-drive'
    Write-Host "Hub: auto-drive ON (sends assign_program + start automatically on connect)" -ForegroundColor Yellow
}
Write-Host "Starting hub window (port 8787, beacon 8788)..."
Start-Process -FilePath 'powershell.exe' -ArgumentList @(
    '-NoExit', '-Command',
    "Set-Location -LiteralPath '$AppDir'; & '$DartExe' $($hubArgs -join ' ')"
) -WindowStyle Normal

# --- 2. The haptic sleeve simulator (optional) ------------------------------
if ($WithHaptics) {
    if (-not (Test-Path $HapticPython)) {
        Write-Warning "Haptic venv python not found at $HapticPython -- skipping the haptic sleeve window."
    } else {
        Write-Host "Starting haptic sleeve simulator window (UDP 8790, discovery 8791)..."
        Start-Process -FilePath 'powershell.exe' -ArgumentList @(
            '-NoExit', '-Command',
            "Set-Location -LiteralPath '$SimDir'; & '$HapticPython' -m haptic.fake_haptic"
        ) -WindowStyle Normal
    }
}

# --- 3. The session watcher/analyser ----------------------------------------
if (-not $SkipWatcher) {
    $watcherScript = Join-Path $PSScriptRoot 'watch_and_analyse.py'
    Write-Host "Starting watcher window (watches app\.hub_data\ every 2s)..."
    Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoExit', '-Command',
        "Set-Location -LiteralPath '$RepoRoot'; & '$AnalyticsPython' '$watcherScript'"
    ) -WindowStyle Normal
}

Write-Host ""
Write-Host "All requested windows opened. Next: start a headset against ws://<this machine>:8787/opus/v1/live" -ForegroundColor Green
Write-Host "  - Unity PlayMode: see docs/TESTING_RUNBOOK.md section 6, terminal 3"
Write-Host "  - Fallback mock headset: sim\live\.venv\Scripts\python.exe sim\live\fake_headset.py --session <dir> --host 127.0.0.1 --port 8787 --no-scenario"
Write-Host "If a second machine (or the Quest) can't reach the hub, run tools\demo\open_firewall.ps1 as admin (it only PRINTS the commands -- read them before running)."
