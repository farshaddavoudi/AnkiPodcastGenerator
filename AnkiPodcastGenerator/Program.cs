using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Interfaces;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
using AnkiPodcastGenerator.Infrastructure.Anki;
using AnkiPodcastGenerator.Infrastructure.AvalAi;
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

var decks = builder.Configuration.GetSection("Decks").Get<List<PodcastDeck>>() ?? [];
builder.Services.AddSingleton(new DecksOptions { Decks = decks });

builder.Services.PostConfigure<AvalAiOptions>(options =>
{
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        options.ApiKey = Environment.GetEnvironmentVariable("AVALAI_API_KEY") ?? string.Empty;
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
builder.Services.AddHttpClient<IPodcastScriptGenerator, AvalAiPodcastScriptGenerator>();
builder.Services.AddHttpClient<ITextToSpeechProvider, AvalAiTextToSpeechProvider>();
builder.Services.AddHttpClient<IMultiSpeakerTextToSpeechProvider, AvalAiGeminiMultiSpeakerTextToSpeechProvider>();

builder.Services.AddSingleton<IDeckProvider, DeckProvider>();
builder.Services.AddSingleton<IOutputPathService, OutputPathService>();
builder.Services.AddSingleton<ICardHashService, CardHashService>();
builder.Services.AddSingleton<ICardSnapshotStore, FileCardSnapshotStore>();
builder.Services.AddSingleton<IMetadataStore, FileMetadataStore>();
builder.Services.AddSingleton<IPodcastScriptParser, PodcastScriptParser>();
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
