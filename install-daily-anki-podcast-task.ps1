[CmdletBinding()]
param(
    [string]$TaskName = "Anki Podcast Generator Daily",
    [string]$At = "10:00",
    [string[]]$Decks = @(),
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

function Get-ConfiguredDeckNames {
    $appsettingsPath = Join-Path $ProjectRoot "AnkiPodcastGenerator\appsettings.json"
    if (-not (Test-Path -LiteralPath $appsettingsPath)) {
        throw "appsettings.json not found: $appsettingsPath"
    }

    $config = Get-Content -LiteralPath $appsettingsPath -Raw | ConvertFrom-Json
    return @($config.Decks | ForEach-Object { $_.DeckName })
}

function Assert-ConfiguredDecks {
    param(
        [string[]]$RequestedDecks,
        [string[]]$ConfiguredDecks
    )

    if ($RequestedDecks.Count -eq 0) {
        return
    }

    $configuredLookup = @{}
    foreach ($deck in $ConfiguredDecks) {
        $configuredLookup[$deck.ToLowerInvariant()] = $deck
    }

    $unknown = @()
    foreach ($requested in $RequestedDecks) {
        if (-not $configuredLookup.ContainsKey($requested.ToLowerInvariant())) {
            $unknown += $requested
        }
    }

    if ($unknown.Count -gt 0) {
        throw "Unknown deck name(s): $($unknown -join ', '). Use exact DeckName values from appsettings.json. Configured decks: $($ConfiguredDecks -join ', ')"
    }
}

$configuredDecks = Get-ConfiguredDeckNames
Assert-ConfiguredDecks -RequestedDecks $Decks -ConfiguredDecks $configuredDecks

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
$deckArgument = ($Decks | ForEach-Object {
    ConvertTo-PowerShellSingleQuotedLiteral -Value $_
}) -join ", "

$runnerCommandParts = @(
    "& $runnerLiteral"
    "-OutputFolder $outputFolderLiteral"
    "-AnkiConnectUrl $ankiConnectUrlLiteral"
    "-AnkiConnectTimeoutSeconds $AnkiConnectTimeoutSeconds"
)

if ($Decks.Count -gt 0) {
    $runnerCommandParts += "-Decks @($deckArgument)"
}

$runnerCommand = $runnerCommandParts -join " "

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
    -Description "Generates Anki due-card podcasts for every deck listed in appsettings.json." `
    -Force | Out-Null

Write-Host "Installed scheduled task '$TaskName'."
Write-Host "Schedule: daily at $At"
Write-Host "Runner: $RunnerPath"
Write-Host "Output: $OutputFolder"
Write-Host "AnkiConnect URL: $AnkiConnectUrl"
Write-Host "AnkiConnect timeout: $AnkiConnectTimeoutSeconds seconds"
if ($Decks.Count -gt 0) {
    Write-Host "Decks: $($Decks -join ', ')"
}
else {
    Write-Host "Decks: all configured in appsettings.json ($($configuredDecks -join ', '))"
}

if ($RunNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Started scheduled task '$TaskName'."
}
