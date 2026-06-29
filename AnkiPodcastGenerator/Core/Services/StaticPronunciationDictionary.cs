using System.Text.RegularExpressions;
using AnkiPodcastGenerator.Core.Models;

namespace AnkiPodcastGenerator.Core.Services;

public static class StaticPronunciationDictionary
{
    private static readonly IReadOnlyList<(string Pattern, string Replacement, string Reason)> Entries =
    [
        // CI/CD & DevOps
        (@"\bCI/CD\b", "see eye see dee", "Common DevOps acronym"),
        (@"\bCI / CD\b", "see eye see dee", "Common DevOps acronym"),
        (@"\bGitLab CI\b", "GitLab C I", "CI/CD platform acronym"),
        (@"\bGitLab-CI\b", "GitLab C I", "CI/CD platform acronym"),

        // Cloud & Infrastructure
        (@"\bKubernetes\b", "koo-ber-net-eez", "Container orchestration platform"),
        (@"\bkubectl\b", "kube control", "Kubernetes CLI tool"),
        (@"\bKube-Proxy\b", "kube proxy", "Kubernetes network component"),
        (@"\bKubeadm\b", "kube admin", "Kubernetes cluster bootstrap"),
        (@"\bMinikube\b", "mini kube", "Local Kubernetes cluster tool"),
        (@"\bNGINX\b", "engine x", "Web server and reverse proxy"),
        (@"\bHAProxy\b", "H A Proxy", "Load balancer and proxy"),
        (@"\bHA-Proxy\b", "H A Proxy", "Load balancer and proxy"),
        (@"\bRedis\b", "red-iss", "In-memory data store"),
        (@"\bMinIO\b", "min eye oh", "S3-compatible object storage"),
        (@"\bS3\b", "S three", "AWS Simple Storage Service"),
        (@"\betcd\b", "et C D", "Distributed key-value store"),
        (@"\bContainer-d\b", "container D", "Container runtime daemon"),
        (@"\bContainerd\b", "container D", "Container runtime daemon"),
        (@"\bCRI-O\b", "C R I O", "Container runtime interface"),

        // .NET Ecosystem
        (@"\bASP\.NET Core\b", "A S P dot net Core", ".NET web framework"),
        (@"\.NET\b", "dot net", ".NET framework name"),
        (@"\bEF Core\b", "E F Core", "Entity Framework Core"),
        (@"\bNuGet\b", "new get", ".NET package manager"),
        (@"\bMediatR\b", "mediator", "Mediator pattern library for .NET"),
        (@"\bYARP\b", "yarp", "Yet Another Reverse Proxy for .NET"),
        (@"\bSignalR\b", "signal R", "Real-time communication library for .NET"),

        // Databases
        (@"\bSQL Server\b", "sequel server", "Microsoft relational database"),
        (@"\bSqlServer\b", "sequel server", "Microsoft relational database"),
        (@"\bPostgreSQL\b", "post-gres Q L", "Open source relational database"),
        (@"\bPostgres\b", "post-gres", "Open source relational database"),
        (@"\bMongoDB\b", "Mongo D B", "NoSQL document database"),
        (@"\bMongo DB\b", "Mongo D B", "NoSQL document database"),

        // Authentication & Security
        (@"\bOIDC\b", "OpenID Connect", "OpenID Connect protocol"),
        (@"\bOAuth2\b", "oh auth two", "OAuth 2.0 authorization framework"),
        (@"\bOAuth 2\b", "oh auth two", "OAuth 2.0 authorization framework"),
        (@"\bJWT\b", "J W T", "JSON Web Token"),
        (@"\bKeycloak\b", "key cloak", "Identity and access management"),
        (@"\bCORS\b", "C O R S", "Cross-Origin Resource Sharing"),

        // Architecture & Patterns
        (@"\bCQRS\b", "C Q R S", "Command Query Responsibility Segregation"),
        (@"\bgRPC\b", "G R P C", "gRPC remote procedure call framework"),

        // Data Formats
        (@"\bJSON\b", "jay son", "JavaScript Object Notation"),
        (@"\bYAML\b", "yam-ul", "YAML data serialization format"),
        (@"\bDTO\b", "D T O", "Data Transfer Object"),
        (@"\bGUID\b", "goo-id", "Globally Unique Identifier"),
        (@"\bUUID\b", "U U I D", "Universally Unique Identifier"),
        (@"\bREST API\b", "rest A P I", "RESTful API"),
        (@"\bRESTApi\b", "rest A P I", "RESTful API"),

        // Acronyms (spelled out)
        (@"\bAPI\b", "A P I", "Application Programming Interface"),
        (@"\bCLI\b", "C L I", "Command Line Interface"),
        (@"\bCLI tool\b", "C L I tool", "Command Line Interface tool"),
        (@"\bCPU\b", "C P U", "Central Processing Unit"),
        (@"\bRAM\b", "ram", "Random Access Memory"),
        (@"\bDNS\b", "D N S", "Domain Name System"),
        (@"\bHTTP\b", "H T T P", "Hypertext Transfer Protocol"),
        (@"\bHTTPS\b", "H T T P S", "Hypertext Transfer Protocol Secure"),
        (@"\bSSH\b", "S S H", "Secure Shell"),
        (@"\bSSL\b", "S S L", "Secure Sockets Layer"),
        (@"\bTLS\b", "T L S", "Transport Layer Security"),
        (@"\bTCP\b", "T C P", "Transmission Control Protocol"),
        (@"\bUDP\b", "U D P", "User Datagram Protocol"),
        (@"\bURI\b", "U R I", "Uniform Resource Identifier"),
        (@"\bURL\b", "U R L", "Uniform Resource Locator"),
        (@"\bHTML\b", "H T M L", "Hypertext Markup Language"),
        (@"\bCSS\b", "C S S", "Cascading Style Sheets"),
        (@"\bXML\b", "X M L", "Extensible Markup Language"),
        (@"\bAJAX\b", "A J A X", "Asynchronous JavaScript and XML"),
        (@"\bSQL\b", "sequel", "Structured Query Language"),
        (@"\bORM\b", "O R M", "Object-Relational Mapping"),
        (@"\bIoC\b", "IoC", "Inversion of Control"),
        (@"\bDI\b", "D I", "Dependency Injection"),

        // Messaging & Queues
        (@"\bRabbitMQ\b", "Rabbit M Q", "Message queue system"),
        (@"\bRabbit MQ\b", "Rabbit M Q", "Message queue system"),

        // Cloud Platforms
        (@"\bAWS\b", "A W S", "Amazon Web Services"),
        (@"\bEC2\b", "E C two", "Amazon Elastic Compute Cloud"),
        (@"\bIAM\b", "I A M", "Identity and Access Management"),
        (@"\bVPC\b", "V P C", "Virtual Private Cloud"),
        (@"\bELB\b", "E L B", "Elastic Load Balancer"),

        // Additional .NET & Windows
        (@"\bMSBuild\b", "M S build", "Microsoft build engine"),
        (@"\bMSIL\b", "M S I L", "Microsoft Intermediate Language"),
        (@"\bCLR\b", "C L R", "Common Language Runtime"),
        (@"\bIL\b", "I L", "Intermediate Language"),

        // Docker / Containers
        (@"\bDockerfile\b", "Docker file", "Docker build configuration file"),
        (@"\bdocker-compose\b", "docker compose", "Docker multi-container tool"),

        // Git
        (@"\bGitLab\b", "Git Lab", "Git-based DevOps platform"),
        (@"\bGitHub\b", "Git Hub", "Git hosting and collaboration platform"),
        (@"\bGitOps\b", "Git Ops", "Git-based operations methodology"),
    ];

    private static readonly IReadOnlyList<(Regex Regex, string Replacement, string Reason)> Compiled;

    static StaticPronunciationDictionary()
    {
        Compiled = Entries
            .Select(e => (new Regex(e.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled), e.Replacement, e.Reason))
            .ToArray();
    }

    public static IReadOnlyList<PronunciationMapItem> Apply(ref string text)
    {
        var map = new List<PronunciationMapItem>();

        foreach (var (regex, replacement, reason) in Compiled)
        {
            var matches = regex.Matches(text);
            if (matches.Count == 0)
            {
                continue;
            }

            // Apply replacements in reverse order to preserve positions
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                map.Add(new PronunciationMapItem(
                    match.Value,
                    replacement,
                    reason));
                text = text[..match.Index] + replacement + text[(match.Index + match.Length)..];
            }
        }

        return map;
    }
}
