[CmdletBinding()]
param(
    [string]$TaskName = "Anki Podcast Generator Daily",
    [string]$At = "10:00",
    [string[]]$Profiles = @("DailyDevOps", "DailyApSwe"),
    [string]$OutputFolder = "C:\Users\fdavo\OneDrive\AnkiPodcasts",
    [switch]$RunNow
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$RunnerPath = Join-Path $ProjectRoot "run-daily-anki-podcasts.ps1"

if (-not (Test-Path -LiteralPath $RunnerPath)) {
    throw "Runner script not found: $RunnerPath"
}

if ([string]::IsNullOrWhiteSpace($env:AVALAI_API_KEY) -and
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable("AVALAI_API_KEY", "User"))) {
    Write-Warning "AVALAI_API_KEY is not visible in this PowerShell session. Set it as a User environment variable before relying on the scheduled task."
}

$profileArgument = ($Profiles | ForEach-Object { "`"$($_.Replace('"', '\"'))`"" }) -join " "
$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", "`"$RunnerPath`"",
    "-OutputFolder", "`"$OutputFolder`"",
    "-Profiles", $profileArgument
) -join " "

$action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument $arguments `
    -WorkingDirectory $ProjectRoot

$trigger = New-ScheduledTaskTrigger -Daily -At ([DateTime]::Parse($At))
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 6)

$principal = New-ScheduledTaskPrincipal `
    -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive `
    -RunLevel Limited

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Principal $principal `
    -Description "Generates Anki due-card podcasts into OneDrive." `
    -Force | Out-Null

Write-Host "Installed scheduled task '$TaskName'."
Write-Host "Schedule: daily at $At"
Write-Host "Runner: $RunnerPath"
Write-Host "Output: $OutputFolder"
Write-Host "Profiles: $($Profiles -join ', ')"

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Started scheduled task '$TaskName'."
}
