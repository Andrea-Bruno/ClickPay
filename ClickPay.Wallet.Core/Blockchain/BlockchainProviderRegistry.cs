using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace ClickPay.Wallet.Core.Blockchain;

/// <summary>
/// Provides a configuration-driven mapping between blockchain identifiers and the
/// underlying client libraries used to interact with them.
/// The mapping is defined via JSON files located under <c>Blockchain/</c>.
/// </summary>
public sealed class BlockchainProviderRegistry
{
    private static readonly string[] EmbeddedResourcePrefixes =
    {
        "ClickPay.Wallet.Core.Blockchain.",
        "Blockchain/"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Lazy<BlockchainProviderRegistry> DefaultLazy = new(LoadDefault);

    private readonly IReadOnlyDictionary<string, BlockchainProviderDescriptor> _providers;

    private BlockchainProviderRegistry(IReadOnlyDictionary<string, BlockchainProviderDescriptor> providers)
    {
        _providers = providers;
    }

    /// <summary>
    /// Gets the registry loaded from the embedded configuration files shipped with the assembly.
    /// </summary>
    public static BlockchainProviderRegistry Default => DefaultLazy.Value;

    /// <summary>
    /// Attempts to resolve the provider descriptor for the specified blockchain identifier.
    /// </summary>
    public bool TryGetProvider(string? blockchain, out BlockchainProviderDescriptor? descriptor)
    {
        if (string.IsNullOrWhiteSpace(blockchain))
        {
            descriptor = default!;
            return false;
        }

    return _providers.TryGetValue(Normalize(blockchain), out descriptor);
    }

    /// <summary>
    /// Gets the provider descriptor for the specified blockchain identifier or throws if none exists.
    /// </summary>
    public BlockchainProviderDescriptor GetProviderOrThrow(string blockchain)
    {
    if (TryGetProvider(blockchain, out var descriptor) && descriptor is not null)
        {
            return descriptor;
        }

        throw new KeyNotFoundException($"No blockchain provider configured for '{blockchain}'.");
    }

    /// <summary>
    /// Exposes the configured provider descriptors keyed by normalized blockchain identifier.
    /// </summary>
    public IReadOnlyDictionary<string, BlockchainProviderDescriptor> GetAllProviders() => _providers;

    private static BlockchainProviderRegistry LoadDefault()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceNames = assembly
            .GetManifestResourceNames()
            .Where(name => IsBlockchainResource(name)
                           && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (resourceNames.Length == 0)
        {
            throw new InvalidOperationException("No blockchain provider configuration files were found in the assembly resources.");
        }

        var providerEntries = new List<BlockchainProviderConfiguration>();

        foreach (var resourceName in resourceNames)
        {
            using var stream = OpenResourceStream(assembly, resourceName);
            var document = JsonSerializer.Deserialize<BlockchainProviderConfigurationDocument>(stream, SerializerOptions);
            if (document?.Providers is null || document.Providers.Count == 0)
            {
                continue;
            }

            providerEntries.AddRange(document.Providers);
        }

        if (providerEntries.Count == 0)
        {
            throw new InvalidOperationException("No blockchain providers defined in configuration files.");
        }

        var map = new Dictionary<string, BlockchainProviderDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providerEntries)
        {
            if (string.IsNullOrWhiteSpace(provider.Library))
            {
                throw new InvalidOperationException("Encountered provider configuration with missing 'library'.");
            }

            if (provider.Blockchains is null || provider.Blockchains.Count == 0)
            {
                throw new InvalidOperationException($"Provider '{provider.Library}' does not specify any blockchains.");
            }

            var metadataSource = provider.Metadata is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(provider.Metadata, StringComparer.OrdinalIgnoreCase);

            var metadata = new ReadOnlyDictionary<string, string>(metadataSource);

            var descriptor = new BlockchainProviderDescriptor(
                provider.Library!,
                provider.Package,
                provider.Handler,
                metadata
            );

            foreach (var blockchain in provider.Blockchains.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                var normalized = Normalize(blockchain);

                if (map.ContainsKey(normalized))
                {
                    throw new InvalidOperationException($"Blockchain '{blockchain}' is already associated with provider '{map[normalized].Library}'.");
                }

                map.Add(normalized, descriptor);
            }
        }

        return new BlockchainProviderRegistry(new ReadOnlyDictionary<string, BlockchainProviderDescriptor>(map));
    }

    private static bool IsBlockchainResource(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return false;
        }

        foreach (var prefix in EmbeddedResourcePrefixes)
        {
            if (resourceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Stream OpenResourceStream(Assembly assembly, string resourceName)
    {
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new FileNotFoundException($"Embedded resource '{resourceName}' could not be found.");
        }

        return stream;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private sealed class BlockchainProviderConfigurationDocument
    {
        public List<BlockchainProviderConfiguration>? Providers { get; init; }
    }

    private sealed class BlockchainProviderConfiguration
    {
        public string? Library { get; init; }
        public string? Package { get; init; }
        public string? Handler { get; init; }
        public Dictionary<string, string>? Metadata { get; init; }
        public List<string>? Blockchains { get; init; }
    }
}

/// <summary>
/// Represents the library and related metadata assigned to a blockchain.
/// </summary>
public sealed record BlockchainProviderDescriptor(
    string Library,
    string? Package,
    string? Handler,
    IReadOnlyDictionary<string, string> Metadata
);
