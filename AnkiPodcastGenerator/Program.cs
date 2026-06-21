using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
using AnkiPodcastGenerator.Infrastructure.Anki;
using AnkiPodcastGenerator.Infrastructure.AvalAi;
using AnkiPodcastGenerator.Infrastructure.Kokoro;
using AnkiPodcastGenerator.Infrastructure.OpenRouter;
using AnkiPodcastGenerator.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<AnkiOptions>(builder.Configuration.GetSection("Anki"));
builder.Services.Configure<PodcastOptions>(builder.Configuration.GetSection("Podcast"));
builder.Services.Configure<AvalAiOptions>(builder.Configuration.GetSection("AvalAi"));
builder.Services.Configure<KokoroOptions>(builder.Configuration.GetSection("Kokoro"));
builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection("OpenRouter"));

ActiveGenerationProfile activeGenerationProfile;
try
{
    activeGenerationProfile = ResolveActiveGenerationProfile(builder.Configuration);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var textToSpeechProviderName = GetEffectiveTextToSpeechProvider(builder.Configuration, activeGenerationProfile);
var scriptProviderName = GetEffectiveScriptProvider(builder.Configuration, activeGenerationProfile);

builder.Services.AddSingleton(activeGenerationProfile);

var decks = builder.Configuration.GetSection("Decks").Get<List<PodcastDeck>>() ?? [];
builder.Services.AddSingleton(new DecksOptions { Decks = decks });

builder.Services.PostConfigure<PodcastOptions>(options =>
{
    if (activeGenerationProfile.IsConfigured)
    {
        options.GenerationProfile = activeGenerationProfile.Name;
    }

    ApplyPodcastProfile(options, activeGenerationProfile.Profile);
});

builder.Services.PostConfigure<AvalAiOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        options.ApiKey = Environment.GetEnvironmentVariable("AVALAI_API_KEY") ?? string.Empty;
    }

    ApplyAvalAiProfile(options, activeGenerationProfile.Profile);
});

builder.Services.PostConfigure<KokoroOptions>(options =>
{
    if (IsKokoroProvider(textToSpeechProviderName) &&
        !string.IsNullOrWhiteSpace(activeGenerationProfile.Profile?.TtsModel))
    {
        options.ModelName = activeGenerationProfile.Profile.TtsModel;
    }
});

builder.Services.PostConfigure<OpenRouterOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        options.ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? string.Empty;
    }
});

builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddHttpClient<IAnkiConnectClient, AnkiConnectClient>();

if (IsOpenRouterProvider(scriptProviderName))
{
    builder.Services.AddHttpClient<IPodcastScriptGenerator, OpenRouterPodcastScriptGenerator>();
}
else if (IsAvalAiProvider(scriptProviderName))
{
    builder.Services.AddHttpClient<IPodcastScriptGenerator, AvalAiPodcastScriptGenerator>();
}
else
{
    throw new InvalidOperationException(
        $"Unknown script provider '{scriptProviderName}'. Supported providers: AvalAi, OpenRouter.");
}

if (IsKokoroProvider(textToSpeechProviderName))
{
    builder.Services.AddSingleton<KokoroTextToSpeechProvider>();
    builder.Services.AddSingleton<ITextToSpeechProvider>(services => services.GetRequiredService<KokoroTextToSpeechProvider>());
    builder.Services.AddSingleton<IMultiSpeakerTextToSpeechProvider>(services => services.GetRequiredService<KokoroTextToSpeechProvider>());
}
else if (IsAvalAiProvider(textToSpeechProviderName))
{
    builder.Services.AddHttpClient<ITextToSpeechProvider, AvalAiTextToSpeechProvider>();
    builder.Services.AddHttpClient<IMultiSpeakerTextToSpeechProvider, AvalAiGeminiMultiSpeakerTextToSpeechProvider>();
}
else
{
    throw new InvalidOperationException(
        $"Unknown TTS provider '{textToSpeechProviderName}'. Supported providers: AvalAi, Kokoro.");
}

builder.Services.AddSingleton<IDeckProvider, DeckProvider>();
builder.Services.AddSingleton<IOutputPathService, OutputPathService>();
builder.Services.AddSingleton<ICardHashService, CardHashService>();
builder.Services.AddSingleton<ICardSnapshotStore, FileCardSnapshotStore>();
builder.Services.AddSingleton<IMetadataStore, FileMetadataStore>();
builder.Services.AddSingleton<IPodcastScriptParser, PodcastScriptParser>();
builder.Services.AddSingleton<IPodcastTtsTextNormalizer, PodcastTtsTextNormalizer>();
builder.Services.AddSingleton<IAudioCombiner, FfmpegAudioCombiner>();
builder.Services.AddSingleton<IPcmAudioEncoder, FfmpegPcmAudioEncoder>();
builder.Services.AddTransient<IPodcastGeneratorService, PodcastGeneratorService>();
builder.Services.AddTransient<CommandLineApp>();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateBootstrapLogger();

try
{
    using var host = builder.Build();
    return await host.Services.GetRequiredService<CommandLineApp>().RunAsync(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "AnkiPodcastGenerator failed");
    Console.Error.WriteLine(ex.Message);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static ActiveGenerationProfile ResolveActiveGenerationProfile(IConfiguration configuration)
{
    var selectedProfileName = configuration["Podcast:GenerationProfile"]?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(selectedProfileName))
    {
        return new ActiveGenerationProfile(string.Empty, null);
    }

    var profiles = configuration
        .GetSection("GenerationProfiles")
        .Get<Dictionary<string, GenerationProfile>>() ?? [];
    var profileMatch = profiles.FirstOrDefault(profile =>
        string.Equals(profile.Key, selectedProfileName, StringComparison.OrdinalIgnoreCase));

    if (profileMatch.Value is null)
    {
        var availableProfiles = profiles.Count == 0
            ? "(none configured)"
            : string.Join(", ", profiles.Keys.Order(StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException(
            $"Generation profile '{selectedProfileName}' was not found. Available profiles: {availableProfiles}.");
    }

    return new ActiveGenerationProfile(profileMatch.Key, profileMatch.Value);
}

static string GetEffectiveTextToSpeechProvider(
    IConfiguration configuration,
    ActiveGenerationProfile activeGenerationProfile)
{
    var profileProvider = activeGenerationProfile.Profile?.TextToSpeechProvider;
    if (!string.IsNullOrWhiteSpace(profileProvider))
    {
        return profileProvider;
    }

    var configuredProvider = configuration["Podcast:TextToSpeechProvider"];
    return string.IsNullOrWhiteSpace(configuredProvider) ? "AvalAi" : configuredProvider;
}

static string GetEffectiveScriptProvider(
    IConfiguration configuration,
    ActiveGenerationProfile activeGenerationProfile)
{
    var profileProvider = activeGenerationProfile.Profile?.ScriptProvider;
    if (!string.IsNullOrWhiteSpace(profileProvider))
    {
        return profileProvider;
    }

    var configuredProvider = configuration["Podcast:ScriptProvider"];
    return string.IsNullOrWhiteSpace(configuredProvider) ? "AvalAi" : configuredProvider;
}

static void ApplyPodcastProfile(PodcastOptions options, GenerationProfile? profile)
{
    if (profile is null)
    {
        return;
    }

    if (!string.IsNullOrWhiteSpace(profile.TextToSpeechProvider))
    {
        options.TextToSpeechProvider = profile.TextToSpeechProvider;
    }

    if (!string.IsNullOrWhiteSpace(profile.ScriptProvider))
    {
        options.ScriptProvider = profile.ScriptProvider;
    }

    if (profile.TargetMinutes is > 0)
    {
        options.TargetMinutes = profile.TargetMinutes.Value;
    }

    if (profile.MultiSpeaker.HasValue)
    {
        options.MultiSpeaker = profile.MultiSpeaker.Value;
    }
}

static void ApplyAvalAiProfile(AvalAiOptions options, GenerationProfile? profile)
{
    if (profile is null)
    {
        return;
    }

    if (!string.IsNullOrWhiteSpace(profile.ScriptModel))
    {
        options.ScriptModel = profile.ScriptModel;
    }

    if (!string.IsNullOrWhiteSpace(profile.TtsModel))
    {
        options.TtsModel = profile.TtsModel;
    }

    if (profile.TtsFallbackModel is not null)
    {
        options.TtsFallbackModel = profile.TtsFallbackModel;
    }

    if (!string.IsNullOrWhiteSpace(profile.VoiceA))
    {
        options.VoiceA = profile.VoiceA;
    }

    if (!string.IsNullOrWhiteSpace(profile.VoiceB))
    {
        options.VoiceB = profile.VoiceB;
    }

    if (profile.TtsSpeed is > 0)
    {
        options.TtsSpeed = profile.TtsSpeed.Value;
    }
}

static bool IsAvalAiProvider(string provider) =>
    string.Equals(provider, "AvalAi", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(provider, "AvalAI", StringComparison.OrdinalIgnoreCase);

static bool IsKokoroProvider(string provider) =>
    string.Equals(provider, "Kokoro", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(provider, "LocalKokoro", StringComparison.OrdinalIgnoreCase);

static bool IsOpenRouterProvider(string provider) =>
    string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase);
