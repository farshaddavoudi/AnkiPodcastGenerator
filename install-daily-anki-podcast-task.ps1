[CmdletBinding()]
param(
    [string]$TaskName = "Anki Podcast Generator Daily",
    [string]$At = "10:00",
    [string[]]$Decks = @(),
    [string]$OutputFolder = "C:\Users\fdavo\OneDrive\AnkiPodcasts",
    [string]$AnkiConnectUrl = "http://127.0.0.1:8765",
    [string]$KokoroWorkingDirectory = "C:\Tools\kokoro-tts",
    [int]$AnkiConnectTimeoutSeconds = 120,
    [switch]$DoNotStartAnki,
    [switch]$KeepWindowOpenOnError,
    [switch]$RunNow
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$RunnerPath = Join-Path $ProjectRoot "run-daily-anki-podcasts.ps1"

if (-not (Test-Path -LiteralPath $RunnerPath)) {
    throw "Runner script not found: $RunnerPath"
}

function Remove-JsonLineComments {
    param([string]$Json)

    $builder = [System.Text.StringBuilder]::new()
    $inString = $false
    $escaped = $false

    for ($index = 0; $index -lt $Json.Length; $index++) {
        $ch = $Json[$index]

        if ($inString) {
            [void]$builder.Append($ch)

            if ($escaped) {
                $escaped = $false
            }
            elseif ($ch -eq [char]'\') {
                $escaped = $true
            }
            elseif ($ch -eq [char]'"') {
                $inString = $false
            }

            continue
        }

        if ($ch -eq [char]'"') {
            $inString = $true
            [void]$builder.Append($ch)
            continue
        }

        if ($ch -eq [char]'/' -and
            $index + 1 -lt $Json.Length -and
            $Json[$index + 1] -eq [char]'/') {
            while ($index -lt $Json.Length -and
                $Json[$index] -ne [char]"`r" -and
                $Json[$index] -ne [char]"`n") {
                $index++
            }

            if ($index -lt $Json.Length) {
                [void]$builder.Append($Json[$index])
            }

            continue
        }

        [void]$builder.Append($ch)
    }

    return $builder.ToString()
}

function Read-AppSettings {
    $appsettingsPath = Join-Path $ProjectRoot "AnkiPodcastGenerator\appsettings.json"
    if (-not (Test-Path -LiteralPath $appsettingsPath)) {
        throw "appsettings.json not found: $appsettingsPath"
    }

    $json = Get-Content -LiteralPath $appsettingsPath -Raw
    return Remove-JsonLineComments -Json $json | ConvertFrom-Json
}

function Get-ConfiguredDeckNames {
    $config = Read-AppSettings
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
$kokoroWorkingDirectoryLiteral = ConvertTo-PowerShellSingleQuotedLiteral -Value $KokoroWorkingDirectory
$deckArgument = ($Decks | ForEach-Object {
    ConvertTo-PowerShellSingleQuotedLiteral -Value $_
}) -join ", "

$runnerCommandParts = @(
    "& $runnerLiteral"
    "-OutputFolder $outputFolderLiteral"
    "-AnkiConnectUrl $ankiConnectUrlLiteral"
    "-KokoroWorkingDirectory $kokoroWorkingDirectoryLiteral"
    "-AnkiConnectTimeoutSeconds $AnkiConnectTimeoutSeconds"
)

if ($Decks.Count -gt 0) {
    $runnerCommandParts += "-Decks @($deckArgument)"
}

$runnerCommand = $runnerCommandParts -join " "

if ($DoNotStartAnki) {
    $runnerCommand += " -DoNotStartAnki"
}

if ($KeepWindowOpenOnError) {
    $runnerCommand += " -KeepWindowOpenOnError"
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
Write-Host "Kokoro working directory: $KokoroWorkingDirectory"
Write-Host "AnkiConnect timeout: $AnkiConnectTimeoutSeconds seconds"
Write-Host "Latest run log: $(Join-Path $ProjectRoot 'logs\daily-run-latest.log')"
if ($KeepWindowOpenOnError) {
    Write-Host "Debug mode: scheduled task window will stay open on runner errors."
}
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
