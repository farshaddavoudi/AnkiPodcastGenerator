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
6. Generate a two-host script through the configured script provider, usually AvalAI or OpenRouter.
7. Generate MP3 audio through the configured TTS provider, usually AvalAI Gemini TTS or local Kokoro.
8. For multi-speaker mode, generate one MP3 per `[A]` / `[B]` segment, create silence for `[PAUSE:n]` markers, and merge with `ffmpeg`.
9. Reuse or extend an existing MP3 when the due-card set has not advanced beyond what was already generated.

## Layout

- `AnkiPodcastGenerator/Program.cs`: host, DI, config, logging, CLI entry point.
- `AnkiPodcastGenerator/appsettings.json`: Anki, podcast, AvalAI, OpenRouter, Kokoro, deck, and Serilog config.
- `AnkiPodcastGenerator/Core/Models`: domain records and metadata models.
- `AnkiPodcastGenerator/Core/Interfaces`: provider and service boundaries.
- `AnkiPodcastGenerator/Core/Services`: orchestration, CLI, deck lookup, script parsing.
- `AnkiPodcastGenerator/Infrastructure/Anki`: AnkiConnect HTTP client.
- `AnkiPodcastGenerator/Infrastructure/AvalAi`: AvalAI script and TTS clients.
- `AnkiPodcastGenerator/Infrastructure/OpenRouter`: OpenRouter script client.
- `AnkiPodcastGenerator/Infrastructure/Kokoro`: local Kokoro TTS client.
- `AnkiPodcastGenerator/Infrastructure/Storage`: file output, hashing, metadata, ffmpeg merge.

## Commands

Run from `C:\Workspace\scripts\AnkiPodcastGenerator`:

```powershell
dotnet build .\AnkiPodcastGenerator.slnx
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- test-anki
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- preview "Career::DevOps" 5
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- generate "Career::DevOps"
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- generate-all
```

For local smoke tests, keep runs small:

```powershell
$env:Podcast__OutputFolder = "C:\Temp\AnkiPodcasts"
$env:Decks__0__MaxCards = "2"
$env:Decks__0__TargetMinutes = "1"
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- generate "Career::DevOps"
```

## Secrets

Never commit or hard-code API keys. Prefer:

```powershell
$env:AVALAI_API_KEY = "my-key"
```

The app also accepts `AvalAi__ApiKey` or `AvalAi:ApiKey`, but environment variables are preferred for agent runs.

## Current Defaults

- Anki sync before due-card queries is enabled by default (`Anki:SyncBeforeQuery=true`).
- `Podcast:GenerationProfile` selects the model/cost profile; default is `Balanced`.
- `Quality` uses `claude-sonnet-4-6` script generation and AvalAI Gemini TTS.
- `QualityLocalTts` uses `claude-sonnet-4-6` script generation and local Kokoro TTS.
- `Balanced` uses `gemini-2.5-flash` for script generation and AvalAI `gemini-2.5-flash-tts` for audio.
- `LocalTts` uses `gemini-2.5-flash` for script generation and local Kokoro TTS.
- `BudgetLocalTts` uses `gemini-2.5-flash-lite-preview-09-2025` for script generation and local Kokoro TTS.
- `GemmaOpenRouterLocalTts` uses OpenRouter `google/gemma-4-31b-it:free` for script generation and local Kokoro TTS.
- Multi-speaker is enabled by default.
- Host A voice: `Kore`
- Host B voice: `Algenib`
- TTS speed: `1.0`
- TTS endpoint: `POST https://api.avalai.ir/v1/audio/speech`
- TTS uses `gemini-2.5-flash-tts`.
- OpenRouter script profiles require `OPENROUTER_API_KEY`.
- Local Kokoro TTS calls the configured `Kokoro:Command` CLI and requires `kokoro-v1.0.onnx` plus `voices-v1.0.bin` in `Kokoro:WorkingDirectory`.
- AvalAI rejected or stalled on MP3 output for `gemini-2.5-flash-preview-tts` during testing; use the non-preview model for normal generation.

## Decks

- Configured decks live under `Decks` in `AnkiPodcastGenerator/appsettings.json`.
- Each entry must use the exact Anki `DeckName`, including hierarchy separators such as `Career::DevOps` and `Language::English`.
- Each entry must also set a positive `MaxCards`.
- `DeckName` is the only deck identifier the app uses for Anki queries, CLI commands, and scheduled runs. Do not add `OutputSlug` unless output filenames need a custom slug.
- The app builds due-card queries as `deck:"<DeckName>" is:due`.
- After renaming a deck in Anki or in `appsettings.json`, reinstall the scheduled task if one is installed.

## Daily Automation

- Default scheduled install runs every deck in `appsettings.json` via `generate-all`:

```powershell
.\install-daily-anki-podcast-task.ps1 -At "10:00"
```

- Do not hard-code deck names into Task Scheduler unless the user explicitly wants a temporary subset.
- Use `-Decks` only for a temporary subset during testing:

```powershell
.\install-daily-anki-podcast-task.ps1 -At "10:00" -Decks "Career::DevOps","Career::ApSwe"
```

- Reinstall the task after changing the deck list, runner script, output folder, or repo location.
- `install-daily-anki-podcast-task.ps1` and `run-daily-anki-podcasts.ps1` validate `-Decks` values against `appsettings.json` and fail fast with the configured deck list when a name is wrong.
- Never use stale short names such as `DailyDevOps` or `DailyApSwe`; they must match `DeckName` in `appsettings.json`.
- Keep scheduled-task deck arguments array-safe. The installer uses `-EncodedCommand`; avoid switching it back to `powershell.exe -File ... -Decks "A" "B"` because Windows PowerShell can bind the second deck as another positional parameter.

## Caching And Reuse

- `Podcast:ReuseIfSameCards` is enabled by default.
- If the selected due cards are unchanged and generation settings match, reuse the previous MP3 with no new script or TTS calls.
- If none of the previously generated cards were studied and the current due-card list is the same prefix plus new cards at the end, generate only the new suffix cards and append that audio to the previous MP3.
- Regenerate fully when card text changes, review order changes, studied cards drop out of the prefix, or generation settings change.
- Card reuse hashes use card ID plus front/back text, not due dates.
- If output-affecting settings change, update the generation settings hash.

## Implementation Rules

- Keep provider boundaries behind interfaces in `Core/Interfaces`.
- Keep AnkiWeb sync behind `IAnkiConnectClient`; do not call AnkiConnect HTTP directly from orchestration code.
- When `Anki:SyncBeforeQuery` is true, sync before `findCards` in generation and preview flows.
- Do not put HTTP or filesystem details into domain models.
- Preserve UTF-8 JSON handling for AnkiConnect, AvalAI, OpenRouter, and local Kokoro input files.
- Keep logs useful: card count, token usage, duration, output file, and provider/model failures.
- Keep cost/profile logs useful: generation profile, script provider, and TTS provider should be visible in generation logs and metadata.
- Do not reuse an old single-speaker MP3 for multi-speaker output.
- Use `ffmpeg` only in `IAudioCombiner` implementations.
- Avoid broad refactors unless they are needed for the requested change.

## Validation

At minimum run:

```powershell
dotnet build .\AnkiPodcastGenerator.slnx
dotnet run --project .\AnkiPodcastGenerator\AnkiPodcastGenerator.csproj -- test-anki
```

When changing generation behavior, run a small deck smoke test with `MaxCards=1` or `2`, then rerun the same command to verify cache reuse or incremental extension.
