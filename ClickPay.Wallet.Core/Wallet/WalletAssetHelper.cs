using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Blockchain;

namespace ClickPay.Wallet.Core.Wallet;

/// <summary>
/// Utility methods for resolving and working with wallet assets defined via JSON metadata.
/// </summary>
public static class WalletAssetHelper
{
    private static IEnumerable<CryptoAsset> AllAssets => CryptoAssetRegistry.Assets.Values;

    public static IEnumerable<CryptoAsset> GetRegisteredAssets() => AllAssets
        .OrderBy(asset => asset.SortOrder ?? int.MaxValue)
        .ThenBy(asset => asset.Code, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<CryptoAsset> GetVisibleAssets() => GetRegisteredAssets()
        .Where(asset => !asset.Hidden);

    private static BlockchainProviderRegistry ProviderRegistry => BlockchainProviderRegistry.Default;

    public static bool IsSupportedNetwork(BlockchainNetwork network)
    {
        var blockchainId = ResolveBlockchainIdentifier(network);
        if (string.IsNullOrWhiteSpace(blockchainId))
        {
            return false;
        }

        return ProviderRegistry.TryGetProvider(blockchainId, out _);
    }

    private static string? ResolveBlockchainIdentifier(BlockchainNetwork network) => network switch
    {
        BlockchainNetwork.Bitcoin => "bitcoin",
        BlockchainNetwork.Solana => "solana",
        BlockchainNetwork.Ethereum => "ethereum",
        BlockchainNetwork.Polygon => "polygon",
        _ => null
    };

    public static bool IsSupportedAsset(CryptoAsset asset)
        => asset is not null && IsSupportedNetwork(asset.Network);

    public static IReadOnlyList<string> GetFeeAssetCodes(CryptoAsset asset)
    {
        if (asset is null)
        {
            throw new ArgumentNullException(nameof(asset));
        }

        if (asset.FeeAssetCodes is { Count: > 0 } specified)
        {
            return specified;
        }

        if (!string.IsNullOrWhiteSpace(asset.ContractAddress))
        {
            if (TryGetNativeAsset(asset.Network, out var native) && !string.Equals(native.Code, asset.Code, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { native.Code };
            }
        }

        return Array.Empty<string>();
    }

    public static bool TryGetNativeAsset(BlockchainNetwork network, out CryptoAsset asset)
    {
        var native = AllAssets
            .Where(candidate => candidate.Network == network && string.IsNullOrWhiteSpace(candidate.ContractAddress))
            .OrderBy(candidate => candidate.SortOrder ?? int.MaxValue)
            .ThenBy(candidate => candidate.Code, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (native is null)
        {
            asset = default!;
            return false;
        }

        asset = native;
        return true;
    }

    public static CryptoAsset? GetNativeAsset(BlockchainNetwork network)
        => TryGetNativeAsset(network, out var asset) ? asset : null;

    public static bool TryParse(string? value, out CryptoAsset asset)
    {
        asset = default!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var input = value.Trim();

        if (CryptoAssetRegistry.TryGet(input, out asset))
        {
            return true;
        }

        foreach (var candidate in AllAssets)
        {
            if (!string.IsNullOrWhiteSpace(candidate.RouteSegment) &&
                string.Equals(candidate.RouteSegment, input, StringComparison.OrdinalIgnoreCase))
            {
                asset = candidate;
                return true;
            }

            if (candidate.Aliases is null)
            {
                continue;
            }

            foreach (var alias in candidate.Aliases)
            {
                if (string.Equals(alias, input, StringComparison.OrdinalIgnoreCase))
                {
                    asset = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryFromCode(string? code, out CryptoAsset asset)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            asset = default!;
            return false;
        }

        return CryptoAssetRegistry.TryGet(code, out asset);
    }

    public static CryptoAsset GetDefinition(string assetCode) => CryptoAssetRegistry.GetRequired(assetCode);

    public static bool TryGetDefinition(string assetCode, out CryptoAsset definition) =>
        CryptoAssetRegistry.TryGet(assetCode, out definition);

    public static string GetRouteSegment(CryptoAsset asset) =>
        string.IsNullOrWhiteSpace(asset.RouteSegment)
            ? asset.Code.ToLowerInvariant()
            : asset.RouteSegment;

    public static string GetRouteSegment(string assetCode) => GetRouteSegment(GetDefinition(assetCode));

    public static string NormalizeCode(string assetCode)
    {
        if (string.IsNullOrWhiteSpace(assetCode))
        {
            throw new ArgumentException("Asset code must be provided.", nameof(assetCode));
        }

        return assetCode.Trim().ToUpperInvariant();
    }

    public static Task SetHiddenAsync(string assetCode, bool hidden, CancellationToken cancellationToken = default) =>
        SetHiddenInternalAsync(assetCode, hidden, cancellationToken);

    public static bool TryResolveCode(string? value, out string code)
    {
        if (TryParse(value, out var asset))
        {
            code = asset.Code;
            return true;
        }

        code = string.Empty;
        return false;
    }

    private static async Task SetHiddenInternalAsync(string assetCode, bool hidden, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetCode))
        {
            throw new ArgumentException("Asset code must be provided.", nameof(assetCode));
        }

        var definition = CryptoAssetRegistry.GetRequired(assetCode);

        if (definition.VisibilityLocked && hidden != definition.Hidden)
        {
            throw new InvalidOperationException($"Visibility for asset '{assetCode}' is locked and cannot be modified.");
        }

        await CryptoAssetRegistry.SetHiddenAsync(assetCode, hidden, cancellationToken).ConfigureAwait(false);
    }

    public static bool IsSupportedForQrPayment(CryptoAsset asset)
    {
        return asset is not null && asset.QrPaymentSupported;
    }
}
