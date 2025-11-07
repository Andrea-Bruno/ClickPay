using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Shared.CryptoAssets;

namespace ClickPay.Shared.Services;

/// <summary>
/// Centralizza la mappatura tra l'enum degli asset del wallet e le definizioni configurabili via JSON.
/// </summary>
public static class WalletAssetHelper
{
    private static readonly IReadOnlyDictionary<WalletAsset, string> AssetCodes = new Dictionary<WalletAsset, string>
    {
        [WalletAsset.Bitcoin] = "BTC",
        [WalletAsset.Eurc] = "EURC",
        [WalletAsset.Xaut] = "XAUT₀",
        [WalletAsset.Sol] = "SOL"
    };

    private static readonly IReadOnlyDictionary<WalletAsset, string> RouteSegments = new Dictionary<WalletAsset, string>
    {
        [WalletAsset.Bitcoin] = "btc",
        [WalletAsset.Eurc] = "eurc",
        [WalletAsset.Xaut] = "xaut",
        [WalletAsset.Sol] = "sol"
    };

    private static readonly IReadOnlyDictionary<string, WalletAsset> Aliases = new Dictionary<string, WalletAsset>(StringComparer.OrdinalIgnoreCase)
    {
        ["btc"] = WalletAsset.Bitcoin,
        ["bitcoin"] = WalletAsset.Bitcoin,
        ["eurc"] = WalletAsset.Eurc,
        ["euro"] = WalletAsset.Eurc,
        ["sol"] = WalletAsset.Sol,
        ["solana"] = WalletAsset.Sol,
        ["xaut"] = WalletAsset.Xaut,
        ["gold"] = WalletAsset.Xaut
    };

    private static readonly IReadOnlyDictionary<string, WalletAsset> CodeToAsset =
        AssetCodes.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    private static readonly WalletAsset[] AssetPreferenceOrder =
    {
        WalletAsset.Eurc,
        WalletAsset.Bitcoin,
        WalletAsset.Sol,
        WalletAsset.Xaut
    };

    public static string GetCode(WalletAsset asset) => AssetCodes.TryGetValue(asset, out var code)
        ? code
        : throw new NotSupportedException($"Wallet asset '{asset}' non ha un code associato.");

    public static string GetRouteSegment(WalletAsset asset) => RouteSegments.TryGetValue(asset, out var segment)
        ? segment
        : throw new NotSupportedException($"Wallet asset '{asset}' non ha una route associata.");

    public static CryptoAsset GetDefinition(WalletAsset asset) => CryptoAssetRegistry.GetRequired(GetCode(asset));

    public static bool TryGetDefinition(WalletAsset asset, out CryptoAsset definition)
    {
        if (!AssetCodes.ContainsKey(asset))
        {
            definition = default!;
            return false;
        }

        return CryptoAssetRegistry.TryGet(GetCode(asset), out definition);
    }

    public static IEnumerable<(WalletAsset Asset, CryptoAsset Definition)> GetRegisteredAssets()
    {
        foreach (var asset in AssetPreferenceOrder)
        {
            if (TryGetDefinition(asset, out var definition))
            {
                yield return (asset, definition);
            }
        }
    }

    public static IEnumerable<WalletAsset> GetVisibleAssets()
    {
        foreach (var (asset, definition) in GetRegisteredAssets())
        {
            if (!definition.Hidden)
            {
                yield return asset;
            }
        }
    }

    public static bool TryFromCode(string code, out WalletAsset asset)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            asset = default;
            return false;
        }

        return CodeToAsset.TryGetValue(code.Trim(), out asset);
    }

    public static Task SetHiddenAsync(WalletAsset asset, bool hidden, CancellationToken cancellationToken = default)
    {
        var code = GetCode(asset);
        return CryptoAssetRegistry.SetHiddenAsync(code, hidden, cancellationToken);
    }

    public static bool TryParse(string? value, out WalletAsset asset)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            asset = default;
            return false;
        }

        return Aliases.TryGetValue(value.Trim(), out asset);
    }
}
