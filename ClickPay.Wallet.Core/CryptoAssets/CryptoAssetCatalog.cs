using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClickPay.Wallet.Core.CryptoAssets;

/// <summary>
/// Loads <see cref="CryptoAsset"/> definitions from JSON configuration files and exposes
/// them via a symbol-indexed catalog.
/// </summary>
public sealed class CryptoAssetCatalog
{
    private readonly Dictionary<string, CryptoAsset> _assets;
    private readonly Dictionary<string, string> _assetFiles;

    private CryptoAssetCatalog(Dictionary<string, CryptoAsset> assets, Dictionary<string, string> assetFiles)
    {
        _assets = assets;
        _assetFiles = assetFiles;
    }

    public IReadOnlyDictionary<string, CryptoAsset> Assets => _assets;

    public IEnumerable<CryptoAsset> VisibleAssets => _assets.Values.Where(asset => !asset.Hidden);

    public bool TryGetAssetFilePath(string code, out string path)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            path = string.Empty;
            return false;
        }

        if (_assetFiles.TryGetValue(NormalizeCode(code), out var storedPath) && !string.IsNullOrWhiteSpace(storedPath))
        {
            path = storedPath;
            return true;
        }

        path = string.Empty;
        return false;
    }

    public string GetRequiredAssetFilePath(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Asset code must be provided.", nameof(code));
        }

        var key = NormalizeCode(code);
        if (!_assetFiles.TryGetValue(key, out var path))
        {
            throw new KeyNotFoundException($"Crypto asset '{key}' does not have an associated configuration file.");
        }

        return path;
    }

    public bool TryGetAsset(string code, out CryptoAsset asset)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            asset = default!;
            return false;
        }

        var key = NormalizeCode(code);

        if (_assets.TryGetValue(key, out var found))
        {
            asset = found;
            return true;
        }

        asset = default!;
        return false;
    }

    public CryptoAsset GetRequiredAsset(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Asset code must be provided.", nameof(code));
        }

        var key = NormalizeCode(code);

        if (!_assets.TryGetValue(key, out var asset))
        {
            throw new KeyNotFoundException($"Crypto asset '{key}' was not found in the catalog.");
        }

        return asset;
    }

    public static CryptoAssetCatalog LoadDefault(JsonSerializerOptions? options = null)
    {
        var directory = GetDefaultDirectoryPath();
        EnsureDefaultAssetsAvailable(directory);
        return LoadFromDirectory(directory, options);
    }

    public static CryptoAssetCatalog LoadFromDirectory(string directoryPath, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path must be provided.", nameof(directoryPath));
        }

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"The crypto asset configuration directory '{directoryPath}' does not exist.");
        }

        var serializerOptions = options ?? CreateDefaultJsonOptions();
        var assets = new Dictionary<string, CryptoAsset>(StringComparer.OrdinalIgnoreCase);
        var assetFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            using var stream = File.OpenRead(file);
            var asset = JsonSerializer.Deserialize<CryptoAsset>(stream, serializerOptions);

            if (asset is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(asset.Code))
            {
                var inferredSymbol = Path.GetFileNameWithoutExtension(file);
                throw new InvalidDataException($"Crypto asset definition in '{file}' does not declare a code. Suggested code: '{inferredSymbol}'.");
            }

            var normalized = Normalize(asset);
            var code = normalized.Code;

            if (!assets.TryAdd(code, normalized))
            {
                throw new InvalidOperationException($"Duplicate crypto asset code '{code}' found while loading '{file}'. Codes must be unique.");
            }

            assetFiles[code] = file;
        }

        return new CryptoAssetCatalog(assets, assetFiles);
    }

    private static CryptoAsset Normalize(CryptoAsset asset) => new()
    {
        Name = asset.Name?.Trim() ?? string.Empty,
        Code = asset.Code.Trim().ToUpperInvariant(),
        Network = asset.Network,
        ContractAddress = string.IsNullOrWhiteSpace(asset.ContractAddress) ? null : asset.ContractAddress.Trim(),
        Decimals = asset.Decimals,
        ChainId = asset.ChainId,
        LogoUrl = string.IsNullOrWhiteSpace(asset.LogoUrl) ? null : asset.LogoUrl.Trim(),
        MenuCustomLabel = string.IsNullOrWhiteSpace(asset.MenuCustomLabel) ? null : asset.MenuCustomLabel.Trim(),
        RouteSegment = string.IsNullOrWhiteSpace(asset.RouteSegment) ? null : asset.RouteSegment.Trim().ToLowerInvariant(),
        Aliases = asset.Aliases is null
            ? Array.Empty<string>()
            : asset.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        SortOrder = asset.SortOrder,
        Symbol = string.IsNullOrWhiteSpace(asset.Symbol) ? null : asset.Symbol.Trim(),
        Hidden = asset.Hidden,
    VisibilityLocked = asset.VisibilityLocked,
        CoingeckoId = string.IsNullOrWhiteSpace(asset.CoingeckoId) ? null : asset.CoingeckoId.Trim(),
        ExplorerUrl = string.IsNullOrWhiteSpace(asset.ExplorerUrl) ? null : asset.ExplorerUrl.Trim(),
        FeeAssetCodes = asset.FeeAssetCodes is null
            ? Array.Empty<string>()
            : asset.FeeAssetCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        NetworkFeeMinimumBalance = asset.NetworkFeeMinimumBalance is { } minimum && minimum >= 0m ? minimum : null
    };

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static string GetDefaultDirectoryPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var directory = Path.Combine(baseDirectory, "CryptoAssets");
        return directory;
    }

    private static JsonSerializerOptions CreateDefaultJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static readonly object InitializationLock = new();

    private static void EnsureDefaultAssetsAvailable(string directoryPath)
    {
        lock (InitializationLock)
        {
            if (Directory.Exists(directoryPath))
            {
                var hasAssets = Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly).Any();
                if (hasAssets)
                {
                    return;
                }
            }

            Directory.CreateDirectory(directoryPath);

            var assembly = typeof(CryptoAssetCatalog).GetTypeInfo().Assembly;

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                var relativePath = TryGetEmbeddedAssetRelativePath(resourceName);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream is null)
                {
                    continue;
                }

                var normalizedPath = relativePath
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar);

                if (string.IsNullOrWhiteSpace(normalizedPath))
                {
                    continue;
                }

                var destinationPath = Path.Combine(directoryPath, normalizedPath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                using var fileStream = File.Create(destinationPath);
                resourceStream.CopyTo(fileStream);
            }
        }
    }

    private static string? TryGetEmbeddedAssetRelativePath(string resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return null;
        }

        const string dotMarker = ".CryptoAssets.";
        const string slashMarker = "CryptoAssets/";

        var dotIndex = resourceName.IndexOf(dotMarker, StringComparison.Ordinal);
        if (dotIndex >= 0)
        {
            return resourceName[(dotIndex + dotMarker.Length)..];
        }

        var slashIndex = resourceName.IndexOf(slashMarker, StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            return resourceName[(slashIndex + slashMarker.Length)..];
        }

        return null;
    }
}
