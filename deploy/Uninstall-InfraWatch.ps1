#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Stops and removes the InfraWatch Windows service (leaves files + data in place).
#>
param(
    [string]$ServiceName = "InfraWatch",
    [switch]$RemoveFirewallRule
)

$ErrorActionPreference = 'Stop'

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Write-Host "Stopping '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
    }
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Removed service '$ServiceName'."
} else {
    Write-Host "Service '$ServiceName' not found - nothing to remove."
}

if ($RemoveFirewallRule) {
    Get-NetFirewallRule -DisplayName "InfraWatch Dashboard" -ErrorAction SilentlyContinue |
        Remove-NetFirewallRule
    Write-Host "Removed firewall rule 'InfraWatch Dashboard'."
}

Write-Host "Done. App files and the data/ + docs/ folders were left in place."
