# AGENTS.md

Guidance for AI coding agents working on this project.

## Project

`AnkiPodcastGenerator` is a .NET 10 console app that generates podcast MP3 files from Anki due cards.

Flow:

1. Call AnkiConnect.
2. If `Anki:SyncBeforeQuery` is enabled, call AnkiConnect `sync` before querying due cards.
3. Fetch due card IDs with `findCards`.
4. Fetch card details with `cardsInfo`.
5. Save a card snapshot JSON.
6. Generate a two-host script through AvalAI chat completions.
7. Generate MP3 audio through AvalAI `/v1/audio/speech`.
8. For multi-speaker mode, generate one MP3 per `[A]` / `[B]` segment and merge with `ffmpeg`.
9. Reuse an existing MP3 when card hash and generation settings are unchanged.

## Layout

- `AnkiPodcastGenerator/Program.cs`: host, DI, config, logging, CLI entry point.
- `AnkiPodcastGenerator/appsettings.json`: Anki, podcast, AvalAI, profile, and Serilog config.
- `AnkiPodcastGenerator/Core/Models`: domain records and metadata models.
- `AnkiPodcastGenerator/Core/Interfaces`: provider and service boundaries.
- `AnkiPodcastGenerator/Core/Services`: orchestration, CLI, profile lookup, script parsing.
- `AnkiPodcastGenerator/Infrastructure/Anki`: AnkiConnect HTTP client.
- `AnkiPodcastGenerator/Infrastructure/AvalAi`: AvalAI script and TTS clients.
- `AnkiPodcastGenerator/Infrastructure/Storage`: file output, hashing, metadata, ffmpeg merge.

## Commands

Run from `C:\Workspace\scripts\AnkiPodcastGenerator`:

```powershell
dotnet build .\AnkiPodcastGenerator.slnx
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- test-anki
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- preview DailyDevOps 5
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- generate DailyDevOps
```

For local smoke tests, keep runs small:

```powershell
$env:Podcast__OutputFolder = "C:\Temp\AnkiPodcasts"
$env:Profiles__0__MaxCards = "2"
$env:Profiles__0__TargetMinutes = "1"
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- generate DailyDevOps
```

## Secrets

Never commit or hard-code API keys. Prefer:

```powershell
$env:AVALAI_API_KEY = "my-key"
```

The app also accepts `AvalAi__ApiKey` or `AvalAi:ApiKey`, but environment variables are preferred for agent runs.

## Current Defaults

- `DailyDevOps` query: `deck:"Career::DevOps" is:due`
- Anki sync before due-card queries is enabled by default (`Anki:SyncBeforeQuery=true`).
- Multi-speaker is enabled by default.
- Host A voice: `Kore`
- Host B voice: `Umbriel`
- TTS speed: `1.0`
- TTS endpoint: `POST https://api.avalai.ir/v1/audio/speech`
- TTS uses `gemini-2.5-flash-tts`.
- AvalAI rejected or stalled on MP3 output for `gemini-2.5-flash-preview-tts` during testing; use the non-preview model for normal generation.

## Implementation Rules

- Keep provider boundaries behind interfaces in `Core/Interfaces`.
- Keep AnkiWeb sync behind `IAnkiConnectClient`; do not call AnkiConnect HTTP directly from orchestration code.
- When `Anki:SyncBeforeQuery` is true, sync before `findCards` in generation and preview flows.
- Do not put HTTP or filesystem details into domain models.
- Preserve UTF-8 JSON handling for AnkiConnect and AvalAI.
- Keep logs useful: card count, token usage, duration, output file, and provider/model failures.
- Keep scheduled-task profile arguments array-safe. The installer uses `-EncodedCommand`; avoid switching it back to `powershell.exe -File ... -Profiles "A" "B"` because Windows PowerShell can bind the second profile as another positional parameter.
- Preserve cache correctness. If output-affecting settings change, update the generation settings hash.
- Do not reuse an old single-speaker MP3 for multi-speaker output.
- Use `ffmpeg` only in `IAudioCombiner` implementations.
- Avoid broad refactors unless they are needed for the requested change.

## Validation

At minimum run:

```powershell
dotnet build .\AnkiPodcastGenerator.slnx
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- test-anki
```

When changing generation behavior, run a small profile smoke test with `MaxCards=1` or `2`, then rerun the same command to verify cache reuse.
