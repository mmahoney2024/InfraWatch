<#
.SYNOPSIS
  Builds a self-contained, deployable InfraWatch package (run on the DEV machine).
.DESCRIPTION
  Publishes InfraWatch.Web self-contained for win-x64 (no .NET needed on the target),
  pre-fills appsettings.Local.json from this machine's user-secrets, drops in the
  install/uninstall scripts + README, and zips it. Output goes to artifacts\ (gitignored),
  so the bundled secrets never get committed.
.PARAMETER Port
  Dashboard port to bake into the published appsettings.Local.json (Urls). Default 8080.
#>
param(
    [int]$Port = 8080,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'
$deployDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root      = Split-Path -Parent $deployDir
$proj      = Join-Path $root "src\InfraWatch.Web\InfraWatch.Web.csproj"
$pkg       = Join-Path $root "artifacts\InfraWatch"
$zip       = Join-Path $root "artifacts\InfraWatch-deploy.zip"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

Write-Host "Publishing self-contained win-x64 ($Configuration)..." -ForegroundColor Cyan
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
& $dotnet publish $proj -c $Configuration -r win-x64 --self-contained true -o $pkg --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# --- Pre-fill appsettings.Local.json from user-secrets + set the listen URL ---
$secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\infrawatch-web\secrets.json"
$local = [ordered]@{}
if (Test-Path $secretsPath) {
    Write-Host "Embedding credentials from user-secrets (values not shown)." -ForegroundColor Cyan
    $secretCount = 0
    (Get-Content $secretsPath -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object {
        $local[$_.Name] = $_.Value
        $secretCount++
    }
    Write-Host "  embedded $secretCount secret key(s)."
} else {
    Write-Warning "No user-secrets found at $secretsPath - writing placeholders. Fill them on the server."
    $local["Jira:Email"]                      = "REPLACE_ME"
    $local["Jira:ApiToken"]                   = "REPLACE_ME"
    $local["DocsExport:Confluence:Email"]     = "REPLACE_ME"
    $local["DocsExport:Confluence:ApiToken"]  = "REPLACE_ME"
    $local["Veeam:Username"]                  = "REPLACE_ME"
    $local["Veeam:Password"]                  = "REPLACE_ME"
    $local["Alerting:Teams:WebhookUrl"]       = "REPLACE_ME"
}
# Listen on all interfaces on the target server.
$local["Urls"] = "http://*:$Port"

$localPath = Join-Path $pkg "appsettings.Local.json"
($local | ConvertTo-Json -Depth 8) | Set-Content -Path $localPath -Encoding UTF8
Write-Host "Wrote $localPath (Urls = http://*:$Port)."

# --- Bundle the install scripts + README ---
Copy-Item (Join-Path $deployDir "Install-InfraWatch.ps1")   $pkg
Copy-Item (Join-Path $deployDir "Uninstall-InfraWatch.ps1") $pkg
Copy-Item (Join-Path $deployDir "InfraWatch-Watchdog.ps1")  $pkg
Copy-Item (Join-Path $deployDir "README-DEPLOY.md")         (Join-Path $pkg "README.md")

# --- Zip ---
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pkg "*") -DestinationPath $zip
$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)

Write-Host ""
Write-Host "Package ready:" -ForegroundColor Green
Write-Host "  folder: $pkg"
Write-Host ("  zip:    {0}  ({1} MB)" -f $zip, $sizeMb)
Write-Host ""
Write-Host "Copy the zip to the target server, unzip, then (elevated): .\Install-InfraWatch.ps1"
Write-Host "NOTE: this package contains live credentials in appsettings.Local.json - move it securely."
