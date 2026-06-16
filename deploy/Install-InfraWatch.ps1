#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs InfraWatch as a Windows service on this server.
.DESCRIPTION
  Run on the TARGET server, from inside the unzipped InfraWatch folder, in an elevated
  PowerShell. Validates the service account up front (re-prompts on a bad password),
  registers the auto-start service, grants it write access to data/docs, opens the firewall,
  starts it, confirms the dashboard responds, and sets up a watchdog that emails if it stops.
.PARAMETER AlertEmail
  Recipient for the down/recover watchdog email. If omitted, the recipients from
  appsettings.Local.json are used; if none, you're prompted (blank = skip the watchdog).
.EXAMPLE
  .\Install-InfraWatch.ps1
.EXAMPLE
  .\Install-InfraWatch.ps1 -Port 8080 -AlertEmail you@sscserv.com
#>
param(
    [string]$ServiceName = "InfraWatch",
    [string]$DisplayName = "InfraWatch Infrastructure Monitor",
    [int]$Port = 8080,
    [System.Management.Automation.PSCredential]$ServiceAccount,
    [string]$AlertEmail
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $here "InfraWatch.Web.exe"

if (-not (Test-Path $exe)) {
    throw "InfraWatch.Web.exe was not found next to this script ($here). Run this from inside the unzipped InfraWatch folder."
}

function Test-DomainCredential {
    param([System.Management.Automation.PSCredential]$Cred)
    Add-Type -AssemblyName System.DirectoryServices.AccountManagement
    $u = $Cred.UserName; $domain = $env:USERDOMAIN; $userOnly = $u
    if     ($u -match '^(?<d>[^\\]+)\\(?<u>.+)$') { $domain = $Matches.d; $userOnly = $Matches.u }
    elseif ($u -match '@')                        { $parts = $u -split '@', 2; $userOnly = $parts[0]; $domain = $parts[1] }
    try {
        $ctx = New-Object System.DirectoryServices.AccountManagement.PrincipalContext('Domain', $domain)
        return [pscustomobject]@{ Checked = $true; Valid = $ctx.ValidateCredentials($userOnly, $Cred.GetNetworkCredential().Password) }
    } catch {
        return [pscustomobject]@{ Checked = $false; Valid = $false; Error = $_.Exception.Message }
    }
}

function Grant-LogonAsServiceRight {
    # Adds SeServiceLogonRight ("Log on as a service") to the account SID via secedit.
    # New-Service/SCM require this right and won't grant it themselves (error 1057 otherwise).
    param([string]$Sid)
    $inf = Join-Path $env:TEMP "iw_secpol.inf"
    $sdb = Join-Path $env:TEMP "iw_secpol.sdb"
    secedit /export /cfg $inf /areas USER_RIGHTS | Out-Null
    $lines = Get-Content $inf
    $found = $false
    $out = foreach ($l in $lines) {
        if ($l -match '^\s*SeServiceLogonRight\s*=') {
            $found = $true
            if ($l -match [regex]::Escape($Sid)) { $l } else { ($l.TrimEnd()) + ",*$Sid" }
        } else { $l }
    }
    if (-not $found) {
        $out = foreach ($l in $out) {
            $l
            if ($l -match '^\[Privilege Rights\]') { "SeServiceLogonRight = *$Sid" }
        }
    }
    Set-Content -Path $inf -Value $out -Encoding Unicode
    secedit /configure /db $sdb /cfg $inf /areas USER_RIGHTS | Out-Null
    Remove-Item $inf, $sdb -Force -ErrorAction SilentlyContinue
}

# --- Prompt for + validate the service account; re-prompt immediately on a bad password ---
while ($true) {
    if (-not $ServiceAccount) {
        Write-Host "Enter the DOMAIN service account InfraWatch will run as" -ForegroundColor Cyan
        Write-Host "  (e.g. COMPASS-TAMU\svc-infrawatch - needs rights to query AD/Hyper-V/DHCP/SMB/Print)." -ForegroundColor Cyan
        $ServiceAccount = Get-Credential -Message "InfraWatch service account (domain\user + password)"
    }
    $check = Test-DomainCredential -Cred $ServiceAccount
    if ($check.Valid) {
        Write-Host "Credentials verified against the domain." -ForegroundColor Green
        break
    } elseif (-not $check.Checked) {
        Write-Warning "Could not reach a domain controller to validate the password ($($check.Error))."
        $ans = Read-Host "Proceed anyway? (the service will fail to start if the password is wrong) [y/N]"
        if ($ans -match '^(y|yes)$') { break } else { $ServiceAccount = $null }
    } else {
        Write-Warning "Logon failed: the username or password is incorrect. Please re-enter."
        $ServiceAccount = $null
    }
}

# --- Normalize the account to canonical DOMAIN\user and grant 'Log on as a service' ---
try {
    $nt  = New-Object System.Security.Principal.NTAccount($ServiceAccount.UserName)
    $sid = $nt.Translate([System.Security.Principal.SecurityIdentifier])
    $canonical = $sid.Translate([System.Security.Principal.NTAccount]).Value
    if ($canonical -ne $ServiceAccount.UserName) {
        Write-Host "Using account name '$canonical'."
        $ServiceAccount = New-Object System.Management.Automation.PSCredential($canonical, $ServiceAccount.Password)
    }
} catch {
    throw "Could not resolve the account '$($ServiceAccount.UserName)': $($_.Exception.Message)"
}

Write-Host "Granting 'Log on as a service' to $($ServiceAccount.UserName)..."
Grant-LogonAsServiceRight -Sid $sid.Value

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
try {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName $DisplayName `
        -Description "InfraWatch - infrastructure monitoring + self-documenting dashboard." `
        -StartupType Automatic -Credential $ServiceAccount | Out-Null
} catch {
    throw "Failed to create the service: $($_.Exception.Message)`n" +
          "If this says the account/password is invalid even though the password was verified, " +
          "the account likely still lacks 'Log on as a service'. Confirm via secpol.msc > Local " +
          "Policies > User Rights Assignment > 'Log on as a service', then re-run."
}

# Auto-restart on crash (reset count daily; restart after 60s, three times).
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# --- Firewall: allow the dashboard port on domain/private profiles ---
if (-not (Get-NetFirewallRule -DisplayName "InfraWatch Dashboard" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "InfraWatch Dashboard" -Direction Inbound -Action Allow `
        -Protocol TCP -LocalPort $Port -Profile Domain,Private | Out-Null
    Write-Host "Opened TCP $Port (Domain,Private)."
}

# --- Start + confirm the dashboard responds ---
Write-Host "Starting '$ServiceName'..."
Start-Service -Name $ServiceName

Write-Host "Waiting for the dashboard on http://localhost:$Port/ ..."
$dashOk = $false
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { $dashOk = $true; break }
    } catch { }
}
if ($dashOk) {
    Write-Host "Dashboard is responding on port $Port." -ForegroundColor Green
} else {
    Write-Warning "Service installed but the dashboard did not respond on port $Port within ~30s."
    Write-Warning "Check Event Viewer > Windows Logs > Application, and confirm appsettings.Local.json + the service account."
}

# --- Watchdog: scheduled task that emails if InfraWatch goes down ---
$watchdog = Join-Path $here "InfraWatch-Watchdog.ps1"
if (Test-Path $watchdog) {
    $smtp = "relay.tamu.edu"; $from = "infrawatch@sscserv.com"; $to = @()
    $localCfg = Join-Path $here "appsettings.Local.json"
    if (Test-Path $localCfg) {
        $j = Get-Content $localCfg -Raw | ConvertFrom-Json
        if ($j.'Alerting:Email:Host') { $smtp = $j.'Alerting:Email:Host' }
        if ($j.'Alerting:Email:From') { $from = $j.'Alerting:Email:From' }
        $j.PSObject.Properties | Where-Object { $_.Name -like 'Alerting:Email:To:*' } | ForEach-Object { $to += $_.Value }
    }
    if ($AlertEmail) { $to = @($AlertEmail) }
    if (-not $to -or $to.Count -eq 0) {
        $entered = Read-Host "Email address to alert if InfraWatch stops (blank to skip the watchdog)"
        if ($entered) { $to = @($entered) }
    }

    if ($to.Count -gt 0) {
        $taskName = "InfraWatch Watchdog"
        Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false
        $toArg  = ($to | ForEach-Object { "'$_'" }) -join ','
        $psArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$watchdog`" -Port $Port -ServiceName $ServiceName -SmtpHost '$smtp' -From '$from' -To $toArg"
        $action    = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $psArgs
        $trigger   = New-ScheduledTaskTrigger -Once -At (Get-Date) `
                        -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
        $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
        $settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
            -Settings $settings -Description "Emails if the InfraWatch service or dashboard goes down." | Out-Null
        Write-Host "Watchdog created (checks every 5 min; alerts: $($to -join ', '))." -ForegroundColor Green
    } else {
        Write-Host "No alert recipient given - skipping the watchdog. Re-run with -AlertEmail to add it later."
    }
}

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "InfraWatch service status: $($svc.Status)" -ForegroundColor Green
Write-Host "Dashboard:  http://$($env:COMPUTERNAME):$Port/   (and http://<server-ip>:$Port/)"
