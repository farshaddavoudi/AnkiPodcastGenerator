[CmdletBinding()]
param(
    [string[]]$Profiles = @("DailyDevOps", "DailyApSwe"),
    [string]$OutputFolder = "C:\Users\fdavo\OneDrive\AnkiPodcasts",
    [string]$AnkiConnectUrl = "http://127.0.0.1:8765",
    [int]$AnkiConnectTimeoutSeconds = 120,
    [switch]$DoNotStartAnki
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "AnkiPodcastGenerator\AnkiPodcastGenerator.csproj"
$LogDirectory = Join-Path $ProjectRoot "logs"
$RunLogPath = Join-Path $LogDirectory ("daily-run-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))

New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $OutputFolder | Out-Null

function Write-RunLog {
    param([string]$Message)

    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message
    Write-Host $line
    Add-Content -Path $RunLogPath -Value $line -Encoding UTF8
}

function Test-AnkiConnect {
    param([string]$Url)

    $body = @{
        action = "version"
        version = 6
    } | ConvertTo-Json -Compress

    try {
        $response = Invoke-RestMethod `
            -Uri $Url `
            -Method Post `
            -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType "application/json; charset=utf-8" `
            -TimeoutSec 5

        return $null -ne $response.result
    }
    catch {
        return $false
    }
}

function Find-AnkiExe {
    if (-not [string]::IsNullOrWhiteSpace($env:ANKI_EXE) -and (Test-Path -LiteralPath $env:ANKI_EXE)) {
        return $env:ANKI_EXE
    }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Anki\anki.exe"),
        (Join-Path $env:ProgramFiles "Anki\anki.exe")
    )

    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} "Anki\anki.exe")
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    $command = Get-Command "anki.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

function Ensure-AnkiConnect {
    $parsedUri = $null
    if (-not [Uri]::TryCreate($AnkiConnectUrl, [UriKind]::Absolute, [ref]$parsedUri) -or
        $parsedUri.Scheme -notin @("http", "https")) {
        throw "Invalid AnkiConnect URL '$AnkiConnectUrl'. If this value is a profile name, reinstall the scheduled task with the current install-daily-anki-podcast-task.ps1 script."
    }

    if (Test-AnkiConnect -Url $AnkiConnectUrl) {
        Write-RunLog "AnkiConnect is already available at $AnkiConnectUrl."
        return
    }

    if ($DoNotStartAnki) {
        throw "AnkiConnect is not available and -DoNotStartAnki was specified."
    }

    $ankiExe = Find-AnkiExe
    if ([string]::IsNullOrWhiteSpace($ankiExe)) {
        throw "Anki is not running and anki.exe was not found. Set ANKI_EXE to the full path of anki.exe, or start Anki before this task runs."
    }

    Write-RunLog "Starting Anki: $ankiExe"
    Start-Process -FilePath $ankiExe -WindowStyle Minimized | Out-Null

    $deadline = (Get-Date).AddSeconds($AnkiConnectTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 3
        if (Test-AnkiConnect -Url $AnkiConnectUrl) {
            Write-RunLog "AnkiConnect became available."
            return
        }
    }

    throw "Timed out after $AnkiConnectTimeoutSeconds seconds waiting for AnkiConnect at $AnkiConnectUrl. Confirm AnkiConnect is installed and enabled in Anki."
}

function Invoke-Generator {
    param([string]$Profile)

    Write-RunLog "Generating profile '$Profile'."
    $env:Podcast__OutputFolder = $OutputFolder

    $output = & dotnet run --project $ProjectFile -- generate $Profile 2>&1
    $exitCode = $LASTEXITCODE

    foreach ($line in $output) {
        Write-RunLog ($line.ToString())
    }

    if ($exitCode -ne 0) {
        throw "Generator failed for profile '$Profile' with exit code $exitCode."
    }
}

Write-RunLog "Starting daily Anki podcast run."
Write-RunLog "Project root: $ProjectRoot"
Write-RunLog "Output folder: $OutputFolder"
Write-RunLog "AnkiConnect URL: $AnkiConnectUrl"
Write-RunLog "Profiles: $($Profiles -join ', ')"

if (-not (Test-Path -LiteralPath $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}

if ([string]::IsNullOrWhiteSpace($env:AVALAI_API_KEY)) {
    $env:AVALAI_API_KEY = [Environment]::GetEnvironmentVariable("AVALAI_API_KEY", "User")
}

if ([string]::IsNullOrWhiteSpace($env:AVALAI_API_KEY)) {
    throw "AVALAI_API_KEY is not set. Set it as a User environment variable before using Task Scheduler."
}

Ensure-AnkiConnect

$failures = @()
foreach ($profileName in $Profiles) {
    try {
        Invoke-Generator -Profile $profileName
    }
    catch {
        $failures += "${profileName}: $($_.Exception.Message)"
        Write-RunLog "ERROR for '$profileName': $($_.Exception.Message)"
    }
}

if ($failures.Count -gt 0) {
    Write-RunLog "Completed with failures: $($failures -join '; ')"
    exit 1
}

Write-RunLog "Daily Anki podcast run completed successfully."
exit 0
