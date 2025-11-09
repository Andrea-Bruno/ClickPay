using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ClickPay.Wallet.Core.CryptoAssets;

/// <summary>
/// Provides global access to the configured crypto assets loaded from JSON definitions.
/// </summary>
public static class CryptoAssetRegistry
{
    private static readonly object SyncRoot = new();
    private static CryptoAssetCatalog? _catalog;

    private static CryptoAssetCatalog Catalog
    {
        get
        {
            if (_catalog is not null)
            {
                return _catalog;
            }

            lock (SyncRoot)
            {
                _catalog ??= CryptoAssetCatalog.LoadDefault();
                return _catalog;
            }
        }
    }

    public static IReadOnlyDictionary<string, CryptoAsset> Assets => Catalog.Assets;

    public static IEnumerable<CryptoAsset> VisibleAssets => Catalog.VisibleAssets;

    public static bool TryGet(string code, out CryptoAsset asset) => Catalog.TryGetAsset(code, out asset);

    public static CryptoAsset GetRequired(string code) => Catalog.GetRequiredAsset(code);

    public static string DefaultDirectoryPath => CryptoAssetCatalog.GetDefaultDirectoryPath();

    public static bool TryGetAssetFilePath(string code, out string path)
    {
        try
        {
            path = Catalog.TryGetAssetFilePath(code, out var resolved) && !string.IsNullOrWhiteSpace(resolved)
                ? resolved
                : string.Empty;
            return !string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            path = string.Empty;
            return false;
        }
    }

    public static void Reload(string? directoryPath = null, JsonSerializerOptions? options = null)
    {
        lock (SyncRoot)
        {
            _catalog = directoryPath is null
                ? CryptoAssetCatalog.LoadDefault(options)
                : CryptoAssetCatalog.LoadFromDirectory(directoryPath, options);
        }

        CryptoAssetIconResolver.ClearCache();
    }

    public static async Task SetHiddenAsync(string code, bool hidden, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Asset code must be provided.", nameof(code));
        }

        var filePath = Catalog.GetRequiredAssetFilePath(code);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Could not locate the configuration file for asset '{code}'.", filePath);
        }

        await UpdateHiddenFlagAsync(filePath, hidden, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(filePath);
        Reload(directory);
    }

    private static async Task UpdateHiddenFlagAsync(string filePath, bool hidden, CancellationToken cancellationToken)
    {
        JsonNode? node;

        await using (var stream = File.OpenRead(filePath))
        {
            node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (node is not JsonObject jsonObject)
        {
            throw new InvalidDataException($"The crypto asset configuration '{filePath}' does not contain a JSON object.");
        }

        jsonObject["hidden"] = hidden;

        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        var json = jsonObject.ToJsonString(serializerOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }
}
