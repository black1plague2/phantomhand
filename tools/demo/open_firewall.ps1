<#
.SYNOPSIS
    Reports (default) what the Windows Firewall does to the OPUS demo's traffic on this PC and, ONLY with an explicit switch,
    in an elevated PowerShell, fixes it. A human step: coding agents must not run it with a change switch.

.DESCRIPTION
    Without any switch this script changes nothing. It reports
      1. the network profile (Public / Private) of every connection and the firewall profile settings,
      2. every firewall rule that belongs to the Unity editor (Unity.exe): enabled?, inbound/outbound, Allow/Block, profile,
         protocol and ports,
      3. whether the OPUS port rules exist (hub TCP 8787, twin UDP 8790 + 8792, ...),
    then explains what it found and prints the exact commands that the change switches would run.

    Why the Unity editor matters: node discovery beacons (UDP 8791), the hub beacon (UDP 8788) and the nodes' replies must reach
    Unity.exe. A per-program inbound BLOCK rule for Unity.exe on the Public profile (Windows creates one when the "allow access?"
    dialog is cancelled) beats every Allow rule, including port rules: Windows evaluates Block before Allow. While it exists, no
    port rule helps. Fix it with -RemoveUnityBlock, or move the network to Private.

    Changes (each needs an elevated PowerShell, asks for confirmation, supports -WhatIf, and skips rules that already exist):
      -RemoveUnityBlock  delete the enabled inbound Block rules of Unity.exe (their definitions are printed first)
      -Apply             add the program-scoped inbound UDP Allow rule for the Unity editor and the hub rule TCP 8787
      -Apply -Twin       also UDP 8790 + 8792 (the sleeve twin, or the real nodes' command ports, on THIS PC)
      -Apply -Haptics    also UDP 8790 + 8791 (the old haptic-sleeve simulator pair; kept for START_DEMO.md)
    New rules are limited to -RemoteAddress (default LocalSubnet) and apply to all profiles.

.PARAMETER Apply
    Add the Allow rules listed above. Needs Administrator.
.PARAMETER Twin
    With -Apply (or to see their status in the report): the twin's UDP ports 8790 and 8792.
.PARAMETER Haptics
    With -Apply: UDP 8790 and 8791 for the old haptic-sleeve simulator (fake_haptic.py).
.PARAMETER RemoveUnityBlock
    Delete the enabled inbound Block rules of Unity.exe. Needs Administrator.
.PARAMETER UnityExe
    Full path of the Unity editor (default: the Unity Hub editor of the version game\ProjectSettings\ProjectVersion.txt pins).
.PARAMETER RemoteAddress
    Remote addresses the new rules accept traffic from (default LocalSubnet).

.EXAMPLE
    tools\demo\open_firewall.ps1
    Report only. Safe in any PowerShell window.

.EXAMPLE
    tools\demo\open_firewall.ps1 -RemoveUnityBlock -Apply -Twin -WhatIf
    In an elevated window: shows what would be removed and added, changes nothing.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [switch]$Apply,
    [switch]$Twin,
    [switch]$Haptics,
    [switch]$RemoveUnityBlock,
    [string]$UnityExe,
    [string]$RemoteAddress = 'LocalSubnet'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Test-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-UnityExe {
    if ($UnityExe) { return $UnityExe }
    $pv = Join-Path $RepoRoot 'game\ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path $pv)) { return $null }
    $m = Select-String -Path $pv -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
    if (-not $m) { return $null }
    $pf = if ($env:ProgramFiles) { $env:ProgramFiles } else { 'C:\Program Files' }
    return Join-Path $pf ('Unity\Hub\Editor\' + $m.Matches[0].Groups[1].Value + '\Editor\Unity.exe')
}

# Every rule whose program is a Unity.exe, with the port filter folded in. Read-only.
function Get-UnityRules {
    $out = @()
    $filters = Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue | Where-Object { $_.Program -and $_.Program -like '*\Unity.exe' }
    foreach ($f in $filters) {
        $r = Get-NetFirewallRule -AssociatedNetFirewallApplicationFilter $f -ErrorAction SilentlyContinue
        if (-not $r) { continue }
        $p = Get-NetFirewallPortFilter -AssociatedNetFirewallRule $r -ErrorAction SilentlyContinue
        $out += [pscustomobject]@{
            Name = $r.Name; DisplayName = $r.DisplayName; Enabled = [string]$r.Enabled; Direction = [string]$r.Direction
            Action = [string]$r.Action; Profile = [string]$r.Profile; Protocol = [string]$p.Protocol
            LocalPort = (@($p.LocalPort) -join ','); Program = $f.Program
        }
    }
    return $out
}

# All enabled inbound Allow rules with their port and program filters (three bulk queries; a rule's filters share its Name).
$script:AllowIndex = $null
function Get-AllowIndex {
    if ($null -eq $script:AllowIndex) {
        $ports = @{}; Get-NetFirewallPortFilter -ErrorAction SilentlyContinue | ForEach-Object { $ports[$_.InstanceID] = $_ }
        $apps = @{}; Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue | ForEach-Object { $apps[$_.InstanceID] = $_ }
        $svcs = @{}; Get-NetFirewallServiceFilter -ErrorAction SilentlyContinue | ForEach-Object { $svcs[$_.InstanceID] = $_ }
        $script:AllowIndex = @(Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue | ForEach-Object {
            [pscustomobject]@{ DisplayName = $_.DisplayName; Profile = [string]$_.Profile; Port = $ports[$_.Name]; App = $apps[$_.Name]; Svc = $svcs[$_.Name] } })
    }
    return $script:AllowIndex
}

# The port is named by the spec: exactly, or inside a range. "Any" does not count (that is a program / service rule, not a port rule).
function Test-PortIn($Spec, [int]$Port) {
    foreach ($s in @($Spec)) {
        $t = [string]$s
        if ($t -eq [string]$Port) { return $true }
        if ($t -match '^(\d+)-(\d+)$' -and $Port -ge [int]$Matches[1] -and $Port -le [int]$Matches[2]) { return $true }
    }
    return $false
}

# An enabled inbound Allow rule that names this protocol and local port and is tied to no program or service: the rule, or $null.
function Test-PortRule([string]$Protocol, [int]$Port) {
    foreach ($e in (Get-AllowIndex)) {
        if (-not $e.Port) { continue }
        $proto = [string]$e.Port.Protocol
        if ($proto -ne $Protocol -and $proto -ne 'Any') { continue }
        if ($e.App -and [string]$e.App.Program -ne 'Any') { continue }
        if ($e.Svc -and [string]$e.Svc.Service -ne 'Any') { continue }
        if (Test-PortIn $e.Port.LocalPort $Port) { return $e }
    }
    return $null
}

function Add-OpusRule([string]$DisplayName, [hashtable]$Spec) {
    if (Get-NetFirewallRule -DisplayName $DisplayName -ErrorAction SilentlyContinue) {
        Write-Host "  already present: $DisplayName" -ForegroundColor DarkGray
        return
    }
    if ($PSCmdlet.ShouldProcess($DisplayName, 'New-NetFirewallRule')) {
        New-NetFirewallRule -DisplayName $DisplayName -Direction Inbound -Action Allow -Profile Any -RemoteAddress $RemoteAddress @Spec | Out-Null
        Write-Host "  added: $DisplayName" -ForegroundColor Green
    }
}

$unity = Resolve-UnityExe
$wantChange = $Apply -or $RemoveUnityBlock

# --- nothing may change without an elevated session ------------------------------------------------------------------
if ($wantChange -and -not (Test-Elevated)) {
    Write-Host "A change switch (-Apply / -RemoveUnityBlock) was given, but this PowerShell is not elevated." -ForegroundColor Red
    Write-Host "Nothing was changed. Open PowerShell as Administrator and run the same command again." -ForegroundColor Red
    exit 1
}

# --- 1. report ---------------------------------------------------------------------------------------------------------
Write-Host "OPUS demo firewall report (read-only unless a change switch was given)" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Network profiles of this PC" -ForegroundColor Yellow
Get-NetConnectionProfile | Select-Object Name, InterfaceAlias, NetworkCategory, IPv4Connectivity | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
Get-NetFirewallProfile | Select-Object Name, Enabled, DefaultInboundAction, DefaultOutboundAction | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

Write-Host "2. Firewall rules of the Unity editor (Unity.exe)" -ForegroundColor Yellow
Write-Host ("   editor used for new rules: " + $(if ($unity) { $unity + $(if (Test-Path $unity) { '' } else { '  (NOT FOUND on disk - pass -UnityExe)' }) } else { '(unknown - pass -UnityExe)' }))
$rules = @(Get-UnityRules)
if ($rules.Count -eq 0) {
    Write-Host "   no rule names Unity.exe: the first inbound packet to the editor on a Public network triggers the allow/cancel dialog." -ForegroundColor DarkGray
} else {
    $rules | Format-Table DisplayName, Enabled, Direction, Action, Profile, Protocol, LocalPort -AutoSize | Out-String -Width 220 | Write-Host
}
$blocks = @($rules | Where-Object { $_.Action -eq 'Block' -and $_.Direction -eq 'Inbound' -and $_.Enabled -eq 'True' })
$allows = @($rules | Where-Object { $_.Action -eq 'Allow' -and $_.Direction -eq 'Inbound' -and $_.Enabled -eq 'True' -and $_.Protocol -in @('UDP', 'Any') })
$public = @(Get-NetConnectionProfile | Where-Object { [string]$_.NetworkCategory -eq 'Public' })

Write-Host "3. OPUS port rules (inbound Allow, any program)" -ForegroundColor Yellow
$ports = @(@('TCP', 8787, 'hub (Quest / phone / second PC -> this PC)'), @('UDP', 8788, 'hub beacon (needed by Unity.exe: see rule 2)'),
           @('UDP', 8790, 'Node A / twin commands'), @('UDP', 8791, 'node discovery beacons (needed by Unity.exe: see rule 2)'),
           @('UDP', 8792, 'Node B / twin (bio) commands'))
foreach ($p in $ports) {
    $hit = Test-PortRule $p[0] $p[1]
    $state = 'missing'
    if ($hit) {
        $state = "present  ($($hit.DisplayName) [$($hit.Profile)])"
        if ($public.Count -gt 0 -and $hit.Profile -notmatch 'Public|Any') { $state += '  -- NOT for the Public network this PC is on' }
    }
    Write-Host ("   {0} {1,-5} {2,-52} {3}" -f $p[0], $p[1], $p[2], $state)
}
Write-Host ""

# --- 2. what it means ------------------------------------------------------------------------------------------------------
Write-Host "What this means" -ForegroundColor Yellow
if ($blocks.Count -gt 0) {
    Write-Host "  - Unity.exe has $($blocks.Count) enabled inbound BLOCK rule(s): $((($blocks | ForEach-Object { $_.DisplayName + ' [' + $_.Profile + ']' }) -join '; '))." -ForegroundColor Red
    Write-Host "    A program Block rule beats every Allow rule, port rules included. Node beacons (UDP 8791), the hub beacon (UDP 8788)"
    Write-Host "    and the nodes' replies cannot reach the editor on those profiles until the Block rule is gone."
    if ($public.Count -gt 0) { Write-Host "    This PC is on a Public network right now ($((($public | ForEach-Object { $_.InterfaceAlias }) -join ', ')))." -ForegroundColor Red }
    Write-Host "    Remove it (elevated):  tools\demo\open_firewall.ps1 -RemoveUnityBlock       (add -WhatIf to rehearse)"
    Write-Host "    or change the network to Private:  Set-NetConnectionProfile -InterfaceAlias '<name>' -NetworkCategory Private"
} else {
    Write-Host "  - No enabled inbound Block rule for Unity.exe." -ForegroundColor Green
}
if ($allows.Count -eq 0) {
    Write-Host "  - No enabled inbound UDP Allow rule for Unity.exe: add one with  tools\demo\open_firewall.ps1 -Apply" -ForegroundColor Yellow
}
Write-Host "  - The twin and the real nodes use UDP 8790 + 8792 on the PC they run on; their rules belong on THAT PC (-Apply -Twin there)."
Write-Host ""

Write-Host "Commands the change switches run (read them before you use -Apply / -RemoveUnityBlock):" -ForegroundColor Yellow
if ($blocks.Count -gt 0) {
    foreach ($b in $blocks) { Write-Host ("  Remove-NetFirewallRule -Name '" + $b.Name + "'    # " + $b.DisplayName) }
}
if ($unity) {
    Write-Host "  New-NetFirewallRule -DisplayName 'OPUS Unity Editor UDP in' -Direction Inbound -Action Allow -Profile Any -Protocol UDP -Program '$unity' -RemoteAddress $RemoteAddress"
}
Write-Host "  New-NetFirewallRule -DisplayName 'OPUS Hub TCP 8787' -Direction Inbound -Action Allow -Profile Any -Protocol TCP -LocalPort 8787 -RemoteAddress $RemoteAddress"
Write-Host "  (-Twin)     New-NetFirewallRule -DisplayName 'OPUS Twin UDP 8790' ... -Protocol UDP -LocalPort 8790   and   'OPUS Twin UDP 8792' ... -LocalPort 8792"
Write-Host "  (-Haptics)  New-NetFirewallRule -DisplayName 'OPUS Haptic UDP 8790' ... -Protocol UDP -LocalPort 8790   and   'OPUS Haptic Discovery UDP 8791' ... -LocalPort 8791"
Write-Host ""

if (-not $wantChange) {
    Write-Host "Nothing was changed (no change switch given)." -ForegroundColor Green
    Write-Host "Afterwards verify with:  Get-NetFirewallRule -DisplayName 'OPUS *' | Format-Table DisplayName,Direction,Action,Enabled,Profile"
    Write-Host "Undo the additions with: Get-NetFirewallRule -DisplayName 'OPUS *' | Remove-NetFirewallRule"
    exit 0
}

# --- 3. changes (elevated, explicit switch, confirmed) -------------------------------------------------------------------
if ($RemoveUnityBlock) {
    Write-Host "Removing the enabled inbound Block rules of Unity.exe" -ForegroundColor Cyan
    if ($blocks.Count -eq 0) { Write-Host "  nothing to remove." -ForegroundColor DarkGray }
    foreach ($b in $blocks) {
        Write-Host ("  rule: {0} | {1} | {2} {3} | program {4}   (to recreate: New-NetFirewallRule -DisplayName '{0}' -Direction Inbound -Action Block -Profile '{1}' -Protocol {2} -Program '{4}')" -f $b.DisplayName, $b.Profile, $b.Protocol, $b.LocalPort, $b.Program)
        if ($PSCmdlet.ShouldProcess($b.DisplayName, 'Remove-NetFirewallRule')) {
            Remove-NetFirewallRule -Name $b.Name
            Write-Host "  removed: $($b.DisplayName)" -ForegroundColor Green
        }
    }
}
if ($Apply) {
    Write-Host "Adding Allow rules (remote: $RemoteAddress, all profiles)" -ForegroundColor Cyan
    if ($unity) {
        Add-OpusRule 'OPUS Unity Editor UDP in' @{ Protocol = 'UDP'; Program = $unity }
    } else {
        Write-Warning "Unity editor not found; pass -UnityExe '<path>\Unity.exe'. Skipping the program rule."
    }
    Add-OpusRule 'OPUS Hub TCP 8787' @{ Protocol = 'TCP'; LocalPort = 8787 }
    if ($Twin) {
        Add-OpusRule 'OPUS Twin UDP 8790' @{ Protocol = 'UDP'; LocalPort = 8790 }
        Add-OpusRule 'OPUS Twin UDP 8792' @{ Protocol = 'UDP'; LocalPort = 8792 }
    }
    if ($Haptics) {
        Add-OpusRule 'OPUS Haptic UDP 8790' @{ Protocol = 'UDP'; LocalPort = 8790 }
        Add-OpusRule 'OPUS Haptic Discovery UDP 8791' @{ Protocol = 'UDP'; LocalPort = 8791 }
    }
}
Write-Host ""
Write-Host "Done. Verify with:  Get-NetFirewallRule -DisplayName 'OPUS *' | Format-Table DisplayName,Direction,Action,Enabled,Profile"
Write-Host "Undo the additions with: Get-NetFirewallRule -DisplayName 'OPUS *' | Remove-NetFirewallRule"
