using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClickPay.Shared.CryptoAssets;

/// <summary>
/// Resolves PNG icon assets for configured crypto entries and exposes them as data URIs suitable for UI rendering.
/// </summary>
public static class CryptoAssetIconResolver
{
    private static readonly ConcurrentDictionary<string, string?> IconCache = new(StringComparer.Ordinal);
    private static readonly HashSet<char> InvalidFileNameCharacters = new(Path.GetInvalidFileNameChars());

    public static bool TryGetIconDataUri(string? code, out string dataUri)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            dataUri = string.Empty;
            return false;
        }

        var normalized = code.Trim();
        var cached = IconCache.GetOrAdd(GetCacheKey(normalized, symbol: null), _ => LoadIconDataUri(normalized));

        if (string.IsNullOrEmpty(cached))
        {
            dataUri = string.Empty;
            return false;
        }

        dataUri = cached;
        return true;
    }

    public static bool TryGetIconDataUri(CryptoAsset? asset, out string dataUri)
    {
        if (asset is null)
        {
            dataUri = string.Empty;
            return false;
        }

        var key = GetCacheKey(asset.Code, asset.Symbol);
        var cached = IconCache.GetOrAdd(key, _ => LoadIconDataUri(asset));

        if (string.IsNullOrEmpty(cached))
        {
            dataUri = string.Empty;
            return false;
        }

        dataUri = cached;
        return true;
    }

    public static void ClearCache() => IconCache.Clear();

    private static string? LoadIconDataUri(string code)
    {
        return LoadIconDataUri(new CryptoAsset { Code = code });
    }

    private static string? LoadIconDataUri(CryptoAsset asset)
    {
        if (!TryResolveIconPath(asset.Code, out var iconPath))
        {
            if (!string.IsNullOrWhiteSpace(asset.Symbol))
            {
                return CreateSymbolSvgDataUri(asset.Symbol!);
            }

            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(iconPath);
            if (bytes.Length == 0)
            {
                return null;
            }

            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }

    private static bool TryResolveIconPath(string code, out string path)
    {
        var candidates = new List<string>();
        var defaultDirectory = CryptoAssetRegistry.DefaultDirectoryPath;

        // Icon source hint: preferred artwork should be copied from
        // https://github.com/spothq/cryptocurrency-icons/tree/master/128/white
        // so the filename convention stays aligned with asset codes.

        if (!string.IsNullOrWhiteSpace(defaultDirectory))
        {
            candidates.Add(Path.Combine(defaultDirectory, $"{code}.png"));

            var upper = code.ToUpperInvariant();
            if (!string.Equals(upper, code, StringComparison.Ordinal))
            {
                candidates.Add(Path.Combine(defaultDirectory, $"{upper}.png"));
            }

            var lower = code.ToLowerInvariant();
            if (!string.Equals(lower, code, StringComparison.Ordinal))
            {
                candidates.Add(Path.Combine(defaultDirectory, $"{lower}.png"));
            }

            var sanitized = SanitizeFileName(code);
            if (!string.Equals(sanitized, code, StringComparison.Ordinal))
            {
                candidates.Add(Path.Combine(defaultDirectory, $"{sanitized}.png"));
            }
        }

        if (CryptoAssetRegistry.TryGetAssetFilePath(code, out var definitionPath) && !string.IsNullOrWhiteSpace(definitionPath))
        {
            var directory = Path.GetDirectoryName(definitionPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var jsonBaseName = Path.GetFileNameWithoutExtension(definitionPath);
                candidates.Add(Path.Combine(directory, $"{jsonBaseName}.png"));
            }
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }

    private static string CreateSymbolSvgDataUri(string symbol)
    {
        const int size = 128;
        const int fontSize = 96;
        var escapedSymbol = System.Security.SecurityElement.Escape(symbol);
        var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{size}' height='{size}' viewBox='0 0 {size} {size}'><text x='50%' y='50%' dominant-baseline='middle' text-anchor='middle' font-family='Segoe UI Symbol, Segoe UI, Arial, sans-serif' font-size='{fontSize}' fill='white'>{escapedSymbol}</text></svg>";
        var bytes = Encoding.UTF8.GetBytes(svg);
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
    }

    private static string SanitizeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(InvalidFileNameCharacters.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private static string GetCacheKey(string code, string? symbol)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? "" : code.Trim().ToUpperInvariant();
        var symbolPart = symbol?.Trim() ?? string.Empty;
        return $"{normalizedCode}|{symbolPart}";
    }
}
