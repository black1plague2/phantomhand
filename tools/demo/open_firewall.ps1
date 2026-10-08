<#
.SYNOPSIS
    Prints (does NOT run) the Windows Firewall commands needed to let a second device (a real
    Quest headset, a phone, or a second PC on the same LAN) reach this machine's OPUS hub.

.DESCRIPTION
    This script never calls New-NetFirewallRule itself. It only prints the exact commands, because
    adding firewall rules needs an elevated (Administrator) PowerShell session, and no coding-agent
    session should be creating firewall rules on your behalf. Copy the printed block into an
    elevated PowerShell window yourself and run it there.

    Matches contracts/LIVE_PROTOCOL.md (hub WebSocket/HTTP TCP 8787, UDP beacon 8788) and, if you
    pass -Haptics, contracts/HAPTIC_PROTOCOL.md (sleeve UDP 8790, discovery UDP 8791). This mirrors
    the exact commands already listed in docs/MANUAL_TODO.md's Track A firewall entry.

.PARAMETER Haptics
    Also print the two additional rules for the haptic sleeve simulator's UDP ports (8790/8791).
    Only relevant if you're also demoing the haptic sleeve on a second machine.

.EXAMPLE
    tools\demo\open_firewall.ps1
    Prints the hub-only rules.

.EXAMPLE
    tools\demo\open_firewall.ps1 -Haptics
    Prints the hub rules plus the haptic sleeve rules.
#>
[CmdletBinding()]
param(
    [switch]$Haptics
)

Write-Host "This script does NOT change anything on this machine." -ForegroundColor Yellow
Write-Host "Copy the block below into an elevated (Administrator) PowerShell window and run it there:" -ForegroundColor Yellow
Write-Host ""

$commands = @(
    '# OPUS hub: WebSocket/HTTP (contracts/LIVE_PROTOCOL.md)',
    'New-NetFirewallRule -DisplayName "OPUS Hub TCP 8787" -Direction Inbound -Protocol TCP -LocalPort 8787 -Action Allow',
    '# OPUS hub: UDP discovery beacon (contracts/LIVE_PROTOCOL.md)',
    'New-NetFirewallRule -DisplayName "OPUS Hub UDP 8788" -Direction Inbound -Protocol UDP -LocalPort 8788 -Action Allow'
)

if ($Haptics) {
    $commands += @(
        '# OPUS haptic sleeve simulator: UDP cue/status (contracts/HAPTIC_PROTOCOL.md)',
        'New-NetFirewallRule -DisplayName "OPUS Haptic UDP 8790" -Direction Inbound -Protocol UDP -LocalPort 8790 -Action Allow',
        '# OPUS haptic sleeve simulator: UDP discovery',
        'New-NetFirewallRule -DisplayName "OPUS Haptic Discovery UDP 8791" -Direction Inbound -Protocol UDP -LocalPort 8791 -Action Allow'
    )
}

$commands | ForEach-Object { Write-Host $_ }

Write-Host ""
Write-Host "After running them, verify with:" -ForegroundColor Yellow
Write-Host "  Get-NetFirewallRule -DisplayName 'OPUS *' | Format-Table DisplayName,Direction,Action,Enabled"
Write-Host ""
Write-Host "If you need to remove them later:" -ForegroundColor Yellow
Write-Host "  Get-NetFirewallRule -DisplayName 'OPUS *' | Remove-NetFirewallRule"
