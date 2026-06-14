[CmdletBinding()]
param(
    [string]$TaskName = "Anki Podcast Generator Daily",
    [string]$At = "10:00",
    [string[]]$Profiles = @("DailyDevOps", "DailyApSwe"),
    [string]$OutputFolder = "C:\Users\fdavo\OneDrive\AnkiPodcasts",
    [string]$AnkiConnectUrl = "http://127.0.0.1:8765",
    [int]$AnkiConnectTimeoutSeconds = 120,
    [switch]$DoNotStartAnki,
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

function ConvertTo-PowerShellSingleQuotedLiteral {
    param([string]$Value)

    return "'" + ($Value -replace "'", "''") + "'"
}

$runnerLiteral = ConvertTo-PowerShellSingleQuotedLiteral -Value $RunnerPath
$outputFolderLiteral = ConvertTo-PowerShellSingleQuotedLiteral -Value $OutputFolder
$ankiConnectUrlLiteral = ConvertTo-PowerShellSingleQuotedLiteral -Value $AnkiConnectUrl
$profileArgument = ($Profiles | ForEach-Object {
    ConvertTo-PowerShellSingleQuotedLiteral -Value $_
}) -join ", "

$runnerCommand = @(
    "& $runnerLiteral"
    "-OutputFolder $outputFolderLiteral"
    "-AnkiConnectUrl $ankiConnectUrlLiteral"
    "-AnkiConnectTimeoutSeconds $AnkiConnectTimeoutSeconds"
    "-Profiles @($profileArgument)"
) -join " "

if ($DoNotStartAnki) {
    $runnerCommand += " -DoNotStartAnki"
}

$encodedRunnerCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($runnerCommand))
$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-EncodedCommand", $encodedRunnerCommand
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
Write-Host "AnkiConnect URL: $AnkiConnectUrl"
Write-Host "AnkiConnect timeout: $AnkiConnectTimeoutSeconds seconds"
Write-Host "Profiles: $($Profiles -join ', ')"

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Started scheduled task '$TaskName'."
}
