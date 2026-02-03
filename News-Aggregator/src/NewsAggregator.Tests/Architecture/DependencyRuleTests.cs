using NewsAggregator.Core.Domain;
using Xunit;

namespace NewsAggregator.Tests.Architecture;

/// <summary>
/// Enforces the Clean Architecture dependency rule: the Core assembly must stay
/// framework-free (BCL only). This is the CI guard referenced in docs §7.5.
/// </summary>
public sealed class DependencyRuleTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.Agents",
        "Microsoft.Extensions.AI",
        "Aspire",
        "OllamaSharp",
        "OpenAI",
        "Microsoft.AspNetCore",
    ];

    [Fact]
    public void Core_assembly_references_no_framework_or_provider_packages()
    {
        var coreAssembly = typeof(NewsItem).Assembly;

        var leaks = coreAssembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .Where(name => ForbiddenPrefixes.Any(
                prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            leaks.Length == 0,
            $"Core must be framework-free but references: {string.Join(", ", leaks)}");
    }
}
