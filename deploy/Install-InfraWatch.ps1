#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs InfraWatch as a Windows service on this server.
.DESCRIPTION
  Run this on the TARGET server, from inside the unzipped InfraWatch folder, in an
  elevated PowerShell. It registers InfraWatch.Web.exe as an auto-start Windows service
  running under a domain account, grants that account write access to the data/docs
  folders, opens the dashboard port in the firewall, and starts the service.
.PARAMETER ServiceAccount
  The domain account the service runs as (needs rights to query AD / Hyper-V / DHCP / SMB /
  Print via integrated auth). If omitted, you'll be prompted.
.PARAMETER Port
  Dashboard TCP port (default 8080). Must match the port in appsettings.Local.json (Urls).
.EXAMPLE
  .\Install-InfraWatch.ps1
.EXAMPLE
  .\Install-InfraWatch.ps1 -Port 8080
#>
param(
    [string]$ServiceName = "InfraWatch",
    [string]$DisplayName = "InfraWatch Infrastructure Monitor",
    [int]$Port = 8080,
    [System.Management.Automation.PSCredential]$ServiceAccount
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $here "InfraWatch.Web.exe"

if (-not (Test-Path $exe)) {
    throw "InfraWatch.Web.exe was not found next to this script ($here). Run this from inside the unzipped InfraWatch folder."
}

if (-not $ServiceAccount) {
    Write-Host "Enter the DOMAIN service account InfraWatch will run as" -ForegroundColor Cyan
    Write-Host "  (e.g. COMPASS-TAMU\svc-infrawatch - needs rights to query AD/Hyper-V/DHCP/SMB/Print)." -ForegroundColor Cyan
    $ServiceAccount = Get-Credential -Message "InfraWatch service account (domain\user + password)"
}

# --- Remove any prior install ---
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Removing existing '$ServiceName' service..."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# --- Make the data + docs folders writable by the service account ---
foreach ($sub in @('data', 'docs')) {
    $dir = Join-Path $here $sub
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $acl  = Get-Acl $dir
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $ServiceAccount.UserName, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -Path $dir -AclObject $acl
}

# --- Create the service ---
Write-Host "Creating service '$ServiceName' -> $exe"
New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName $DisplayName `
    -Description "InfraWatch - infrastructure monitoring + self-documenting dashboard." `
    -StartupType Automatic -Credential $ServiceAccount | Out-Null

# Auto-restart on failure (reset count daily; restart after 60s, three times).
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# --- Firewall: allow the dashboard port on domain/private profiles ---
if (-not (Get-NetFirewallRule -DisplayName "InfraWatch Dashboard" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "InfraWatch Dashboard" -Direction Inbound -Action Allow `
        -Protocol TCP -LocalPort $Port -Profile Domain,Private | Out-Null
    Write-Host "Opened TCP $Port (Domain,Private)."
}

# --- Start ---
Write-Host "Starting '$ServiceName'..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3
$svc = Get-Service -Name $ServiceName

Write-Host ""
Write-Host "InfraWatch service status: $($svc.Status)" -ForegroundColor Green
Write-Host "Dashboard:  http://$($env:COMPUTERNAME):$Port/   (and http://<server-ip>:$Port/)"
Write-Host ""
if ($svc.Status -ne 'Running') {
    Write-Warning "Service is not Running. Check Event Viewer > Windows Logs > Application, and confirm the service account password and appsettings.Local.json are correct."
}
