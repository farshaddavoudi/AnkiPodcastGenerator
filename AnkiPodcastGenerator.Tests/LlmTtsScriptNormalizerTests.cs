using System.Net;
using System.Text;
using System.Text.Json;
using AnkiPodcastGenerator.Configuration;
using AnkiPodcastGenerator.Core.Models;
using AnkiPodcastGenerator.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnkiPodcastGenerator.Tests;

public sealed class LlmTtsScriptNormalizerTests
{
    [Fact]
    public async Task NormalizeWithLlmAsync_UsesConfiguredNormalizerModel()
    {
        var handler = new CapturingHandler(
            """
            {"choices":[{"message":{"content":"{\"ttsScript\":\"Hello A P I\",\"pronunciationMap\":[]}"}}]}
            """);
        var normalizer = CreateNormalizer(handler, "normalizer-chat-model");

        var result = await normalizer.NormalizeWithLlmAsync(
            "Hello API",
            [],
            CancellationToken.None);

        using var requestJson = JsonDocument.Parse(handler.RequestBody ?? throw new InvalidOperationException("No request captured."));
        Assert.Equal("normalizer-chat-model", requestJson.RootElement.GetProperty("model").GetString());
        Assert.Equal("Hello A P I", result.TtsScript);
    }

    [Fact]
    public async Task NormalizeWithLlmAsync_FallsBackWhenResponseIsInvalid()
    {
        var handler = new CapturingHandler(
            """
            {"choices":[{"message":{"content":"{}"}}]}
            """);
        var normalizer = CreateNormalizer(handler, "normalizer-chat-model");
        var staticMap = new[]
        {
            new PronunciationMapItem("API", "A P I", "test")
        };

        var result = await normalizer.NormalizeWithLlmAsync(
            "Hello A P I",
            staticMap,
            CancellationToken.None);

        Assert.Equal("Hello A P I", result.TtsScript);
        Assert.Equal(staticMap, result.PronunciationMap);
    }

    private static LlmTtsScriptNormalizer CreateNormalizer(
        CapturingHandler handler,
        string llmModel) =>
        new(
            new HttpClient(handler),
            Options.Create(new AvalAiOptions
            {
                BaseUrl = "https://avalai.test",
                ApiKey = "test-key",
                ScriptModel = "script-model",
                TtsModel = "kokoro-v1.0"
            }),
            Options.Create(new TtsNormalizerOptions { LlmModel = llmModel }),
            NullLogger<LlmTtsScriptNormalizer>.Instance);

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
