# Anki Podcast Generator

Turn due Anki cards into daily podcast MP3s.

This project is a local Windows-first MVP for people who want to review Anki material while walking, commuting, doing chores, or preparing for focused study. It reads due cards through AnkiConnect, asks an LLM to turn them into a two-host educational conversation, generates multi-speaker TTS audio, and writes a tidy daily podcast folder.

## Why This Exists

Anki is excellent for active recall, but sometimes the best next review is not another screen session. This app creates a companion audio review from the cards that Anki is already about to ask you.

The goal is not to replace Anki reviews. The goal is to warm up your memory before review time:

- Hear the first due cards before opening Anki.
- Keep commands, warnings, and details in the script.
- Spend more audio time on complex cards.
- Reuse existing MP3s when the same due-card set has not changed.
- Keep everything local and automatable on a Windows PC.

## Current Stack

- .NET 10
- C#
- AnkiConnect
- AvalAI API
- Gemini TTS through AvalAI native `v1beta`
- Serilog
- Windows Task Scheduler
- JSON configuration
- ffmpeg for PCM-to-MP3 encoding

## Architecture

```text
AnkiConnect
  -> find due cards
  -> load card info
  -> sort near Anki review order
  -> hash selected cards
  -> reuse previous MP3 if unchanged
  -> generate podcast script with LLM
  -> generate multi-speaker audio with TTS
  -> save MP3 + metadata
```

The main implementation is intentionally split behind interfaces so providers can be swapped later:

- `IAnkiConnectClient`
- `IPodcastScriptGenerator`
- `ITextToSpeechProvider`
- `IMultiSpeakerTextToSpeechProvider`
- `IAudioCombiner`
- `IPcmAudioEncoder`
- `IMetadataStore`

AvalAI is the current provider because it supports the models used during MVP development. The design is not intended to be AvalAI-only. For example, script generation could later use OpenRouter, OpenAI, a local model server, or any OpenAI-compatible API by adding another `IPodcastScriptGenerator` implementation. TTS could also be swapped behind the TTS interfaces.

## Output Layout

The app writes one folder per day:

```text
C:\AnkiPodcasts
  2026-06-13
    devops.mp3
    apswe.mp3
    _metadata
      devops
        cards.json
        script.txt
        generated.json
      apswe
        cards.json
        script.txt
        generated.json
```

Only the MP3 files need to be consumed. The `_metadata` folder exists for debugging, traceability, and reuse caching.

For personal use, the daily runner script defaults to:

```text
C:\Users\fdavo\OneDrive\AnkiPodcasts
```

That makes the MP3s sync automatically to a phone through OneDrive. OneDrive is only one delivery option. Future delivery targets could be:

- Telegram bot upload
- private podcast RSS feed
- S3-compatible storage
- Syncthing folder
- Nextcloud folder
- local network share

The current storage layer writes files; a later delivery layer can publish those files elsewhere without changing the card/script/TTS pipeline.

## Prerequisites

1. Windows 11
2. .NET 10 SDK
3. Anki
4. AnkiConnect add-on
5. ffmpeg available on `PATH`
6. AvalAI API key

Check AnkiConnect:

```powershell
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- test-anki
```

## Configuration

Main config file:

```text
AnkiPodcastGenerator\appsettings.json
```

Important sections:

```json
{
  "Anki": {
    "BaseUrl": "http://127.0.0.1:8765"
  },
  "Podcast": {
    "OutputFolder": "C:\\AnkiPodcasts",
    "TargetMinutes": 30,
    "MaxCards": 40,
    "ReuseIfSameCards": true,
    "MultiSpeaker": true
  },
  "AvalAi": {
    "BaseUrl": "https://api.avalai.ir",
    "ApiKey": "",
    "ScriptModel": "claude-sonnet-4-5",
    "TtsModel": "gemini-2.5-flash-tts",
    "VoiceA": "Kore",
    "VoiceB": "Algenib"
  }
}
```

Do not commit API keys. Keep `AvalAi:ApiKey` empty and set an environment variable instead:

```powershell
$env:AVALAI_API_KEY = "my-key"
```

For scheduled runs, set it as a User environment variable:

```powershell
[Environment]::SetEnvironmentVariable("AVALAI_API_KEY", "my-key", "User")
```

## Profiles

Profiles define what Anki query to use and how many cards to include.

Current examples:

```json
{
  "Name": "DailyDevOps",
  "AnkiQuery": "deck:\"Career::DevOps\" is:due",
  "TargetMinutes": 30,
  "MaxCards": 10,
  "MultiSpeaker": true,
  "OutputSlug": "devops"
}
```

```json
{
  "Name": "DailyApSwe",
  "AnkiQuery": "deck:\"Career::ApSwe\" is:due",
  "TargetMinutes": 30,
  "MaxCards": 10,
  "MultiSpeaker": true,
  "OutputSlug": "apswe"
}
```

Anki queries can be changed to match your decks, tags, or scheduling needs:

```text
deck:"Career::DevOps" is:due
tag:linux is:due
prop:due<=0
```

## Commands

Connectivity check:

```powershell
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- test-anki
```

Preview the first cards selected for a profile:

```powershell
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- preview DailyDevOps 10
```

Generate one profile:

```powershell
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- generate DailyDevOps
```

Generate AP SWE:

```powershell
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- generate DailyApSwe
```

## Daily Automation

Install a Windows scheduled task:

```powershell
.\install-daily-anki-podcast-task.ps1 -At "10:00"
```

Run the same workflow manually:

```powershell
.\run-daily-anki-podcasts.ps1
```

The runner currently generates:

- `DailyDevOps`
- `DailyApSwe`

By default it writes to:

```text
C:\Users\fdavo\OneDrive\AnkiPodcasts
```

Override it:

```powershell
.\run-daily-anki-podcasts.ps1 -OutputFolder "C:\AnkiPodcasts"
```

If Anki is not already running, the runner tries to start `anki.exe` and waits for AnkiConnect. If Anki is installed in a non-standard location:

```powershell
[Environment]::SetEnvironmentVariable("ANKI_EXE", "C:\Path\To\anki.exe", "User")
```

Windows Task Scheduler stores script paths as absolute paths. If you move or clone this repo somewhere else, reinstall the task from the new folder:

```powershell
.\install-daily-anki-podcast-task.ps1 -At "10:00"
```

## Caching

The app computes a deterministic hash from:

- selected card IDs
- card front text
- card back text
- relevant generation settings

If the hash is unchanged and the previous MP3 still exists, the app reuses the old MP3 and avoids calling the LLM/TTS APIs again.

This matters for daily automation. If you skip Anki reviews and the same cards are still due tomorrow, the app can reuse yesterday's generated audio.

## TTS Notes

Single-speaker TTS can use AvalAI's OpenAI-compatible `/v1/audio/speech` endpoint.

Multi-speaker audio uses AvalAI's native Gemini route instead:

```text
POST /v1beta/models/{model}:generateContent
```

The native route supports explicit `multiSpeakerVoiceConfig`, which maps:

- Host A -> `Kore`
- Host B -> `Algenib`

Gemini returns PCM audio (`audio/L16;codec=pcm;rate=24000`), so this app uses ffmpeg to encode it to MP3.

## Repository Notes

Generated files are intentionally ignored:

- `bin/`
- `obj/`
- `logs/`
- `.vs/`
- `output*/`
- `voice-diagnostics/`
- `*.mp3`

This keeps the repo focused on source code, configuration, and automation scripts.

## Roadmap Ideas

- Add OpenRouter script-generation provider.
- Add Telegram delivery.
- Add private podcast RSS generation.
- Add per-profile delivery settings.
- Add retention policy for old daily folders.
- Add tests around Anki card ordering and cache hash behavior.
- Add a small UI for profile management.
