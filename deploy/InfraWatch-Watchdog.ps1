<#
.SYNOPSIS
  Watchdog for the InfraWatch service. Run by a scheduled task every few minutes; emails when
  the service (or its dashboard) goes down, and once when it recovers. Alerts only on a state
  CHANGE (tracked in a small state file) so you don't get a mail every interval while it's down.
.NOTES
  The service itself auto-restarts on crash (configured at install). This watchdog covers the
  "stays down" case and notifies a human.
#>
param(
    [string]$ServiceName = "InfraWatch",
    [int]$Port = 8080,
    [string]$SmtpHost = "relay.tamu.edu",
    [string]$From = "infrawatch@sscserv.com",
    [string[]]$To = @(),
    [string]$StateFile = "$env:ProgramData\InfraWatch\watchdog.state"
)

if (-not $To -or $To.Count -eq 0) { return }  # no recipient => nothing to do

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$running = $svc -and $svc.Status -eq 'Running'

$httpOk = $false
if ($running) {
    try {
        $r = Invoke-WebRequest "http://localhost:$Port/" -UseBasicParsing -TimeoutSec 10
        $httpOk = ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500)
    } catch { $httpOk = $false }
}

$healthy = $running -and $httpOk
$current = if ($healthy) { 'up' } else { 'down' }

$prev = 'up'
if (Test-Path $StateFile) { $prev = (Get-Content $StateFile -Raw).Trim() }

if ($current -ne $prev) {
    $hostName = $env:COMPUTERNAME
    $now = Get-Date
    if ($current -eq 'down') {
        if (-not $svc)        { $reason = "the service is not installed" }
        elseif (-not $running) { $reason = "service status is '$($svc.Status)'" }
        else                   { $reason = "service is running but the dashboard did not respond on port $Port" }
        $subject = "[InfraWatch] DOWN on $hostName"
        $body = "InfraWatch is DOWN on $hostName at $now.`r`n`r`nReason: $reason.`r`n`r`nDashboard: http://${hostName}:$Port/"
    } else {
        $subject = "[InfraWatch] recovered on $hostName"
        $body = "InfraWatch is back UP on $hostName at $now."
    }
    try { Send-MailMessage -SmtpServer $SmtpHost -From $From -To $To -Subject $subject -Body $body -ErrorAction Stop }
    catch { } # best effort; don't crash the task
}

New-Item -ItemType Directory -Path (Split-Path $StateFile) -Force | Out-Null
Set-Content -Path $StateFile -Value $current
