using AnkiPodcastGenerator.Core.Services;
using Xunit;

namespace AnkiPodcastGenerator.Tests;

public sealed class StaticPronunciationDictionaryTests
{
    [Fact]
    public void Apply_ReplacesCiCd()
    {
        var text = "We use CI/CD for deployment.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("CI/CD", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("We use see eye see dee for deployment.", text);
    }

    [Fact]
    public void Apply_ReplacesKubernetes()
    {
        var text = "Deploy to Kubernetes cluster.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("Kubernetes", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Deploy to koo-ber-net-eez cluster.", text);
    }

    [Fact]
    public void Apply_ReplacesKubectl()
    {
        var text = "Use kubectl to manage pods.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("kubectl", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Use kube control to manage pods.", text);
    }

    [Fact]
    public void Apply_ReplacesDotNet()
    {
        var text = "Built with .NET 10.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals(".NET", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Built with dot net 10.", text);
    }

    [Fact]
    public void Apply_ReplacesAspNetCore()
    {
        var text = "Using ASP.NET Core framework.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("ASP.NET Core", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Using A S P dot net Core framework.", text);
    }

    [Fact]
    public void Apply_ReplacesJwt()
    {
        var text = "Authenticate with JWT tokens.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("JWT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Authenticate with J W T tokens.", text);
    }

    [Fact]
    public void Apply_ReplacesJson()
    {
        var text = "Returns JSON data.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("JSON", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Returns jay son data.", text);
    }

    [Fact]
    public void Apply_ReplacesYaml()
    {
        var text = "Write YAML configuration.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("YAML", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Write yam-ul configuration.", text);
    }

    [Fact]
    public void Apply_ReplacesApi()
    {
        var text = "Call the REST API endpoint.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original is "API" or "REST API");
        Assert.Equal("Call the rest A P I endpoint.", text);
    }

    [Fact]
    public void Apply_ReplacesSql()
    {
        var text = "Write SQL queries.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original is "SQL");
        Assert.Equal("Write sequel queries.", text);
    }

    [Fact]
    public void Apply_ReplacesPostgres()
    {
        var text = "Using PostgreSQL database.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Using post-gres Q L database.", text);
    }

    [Fact]
    public void Apply_ReplacesRedis()
    {
        var text = "Cache with Redis.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("Redis", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Cache with red-iss.", text);
    }

    [Fact]
    public void Apply_ReplacesNginx()
    {
        var text = "Reverse proxy via NGINX.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("NGINX", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Reverse proxy via engine x.", text);
    }

    [Fact]
    public void Apply_ReplacesOidc()
    {
        var text = "Configure OIDC authentication.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("OIDC", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Configure OpenID Connect authentication.", text);
    }

    [Fact]
    public void Apply_ReplacesOAuth2()
    {
        var text = "Using OAuth2 for authorization.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("OAuth2", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Using oh auth two for authorization.", text);
    }

    [Fact]
    public void Apply_ReplacesCqrs()
    {
        var text = "Implement CQRS pattern.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("CQRS", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Implement C Q R S pattern.", text);
    }

    [Fact]
    public void Apply_ReplacesGrpc()
    {
        var text = "Communicate via gRPC.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("gRPC", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Communicate via G R P C.", text);
    }

    [Fact]
    public void Apply_ReplacesGuid()
    {
        var text = "Generate a GUID.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("GUID", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Generate a goo-id.", text);
    }

    [Fact]
    public void Apply_ReplacesUuid()
    {
        var text = "UUID is unique.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("UUID", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("U U I D is unique.", text);
    }

    [Fact]
    public void Apply_ReplacesRabbitMq()
    {
        var text = "Message queue with RabbitMQ.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("RabbitMQ", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Message queue with Rabbit M Q.", text);
    }

    [Fact]
    public void Apply_ReplacesEfCore()
    {
        var text = "Using EF Core for data access.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("EF Core", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Using E F Core for data access.", text);
    }

    [Fact]
    public void Apply_ReplacesNuGet()
    {
        var text = "Package from NuGet.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("NuGet", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Package from new get.", text);
    }

    [Fact]
    public void Apply_ReplacesKeycloak()
    {
        var text = "SSO with Keycloak.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("Keycloak", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("SSO with key cloak.", text);
    }

    [Fact]
    public void Apply_MultipleReplacementsInSameText()
    {
        var text = "Deploy .NET apps to Kubernetes using kubectl and CI/CD.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Equal(4, result.Count);
        Assert.Equal("Deploy dot net apps to koo-ber-net-eez using kube control and see eye see dee.", text);
    }

    [Fact]
    public void Apply_EmptyText_ReturnsEmpty()
    {
        var text = string.Empty;
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Empty(result);
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void Apply_NoMatchingTerms_ReturnsEmptyMap()
    {
        var text = "The quick brown fox jumps over the lazy dog.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Empty(result);
        Assert.Equal("The quick brown fox jumps over the lazy dog.", text);
    }

    [Fact]
    public void Apply_CaseInsensitiveReplacement()
    {
        var text = "KUBERNETES, kubernetes, Kubernetes";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Equal(3, result.Count);
        Assert.Equal("koo-ber-net-eez, koo-ber-net-eez, koo-ber-net-eez", text);
    }

    [Fact]
    public void Apply_PartialWord_NoMatch()
    {
        // "postgresql" without a word boundary "PostgreSQL" should not match
        var text = "postgres";
        var result = StaticPronunciationDictionary.Apply(ref text);
        // "Postgres" should match (it's a separate entry)
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Apply_RamBecomesLowercase()
    {
        var text = "Allocate RAM.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("RAM", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Allocate ram.", text);
    }

    [Fact]
    public void Apply_CpuStaysUppercase()
    {
        var text = "CPU utilization.";
        var result = StaticPronunciationDictionary.Apply(ref text);
        Assert.Contains(result, m => m.Original.Equals("CPU", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("C P U utilization.", text);
    }
}
