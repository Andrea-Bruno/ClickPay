using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using ClickPay.Wallet.Core.CryptoAssets;

namespace ClickPay.Wallet.Core.Wallet;

public sealed record WalletVault
{
    public required string Mnemonic { get; init; }
    public string? Passphrase { get; init; }
    public int WordCount { get; init; }
    public int AccountIndex { get; init; } = 0;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public IDictionary<string, WalletVaultChainState> Chains { get; init; } = new Dictionary<string, WalletVaultChainState>(StringComparer.OrdinalIgnoreCase);
    public string Version { get; init; } = CurrentVersion;

    [JsonIgnore]
    public static string CurrentVersion => "1.0.0";

    public bool TryGetChainState(string chainId, out WalletVaultChainState state)
    {
        if (Chains is null)
        {
            state = WalletVaultChainState.Default;
            return false;
        }

        if (Chains.TryGetValue(chainId, out state!))
        {
            return true;
        }

        state = WalletVaultChainState.Default;
        return false;
    }

    public WalletVault WithChainState(string chainId, WalletVaultChainState state)
    {
        var updatedChains = Chains is null
            ? new Dictionary<string, WalletVaultChainState>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, WalletVaultChainState>(Chains, StringComparer.OrdinalIgnoreCase);

        updatedChains[chainId] = state;

        return this with { Chains = updatedChains };
    }
}

public sealed record WalletVaultChainState
{
    public static WalletVaultChainState Default => new();

    public int ExternalAddressIndex { get; init; }
    public int InternalAddressIndex { get; init; }
    public int AssociatedAccountIndex { get; init; }
}

public static class WalletChains
{
    public static string Bitcoin => ResolveChainKey(BlockchainNetwork.Bitcoin.ToString());
    public static string Solana => ResolveChainKey(BlockchainNetwork.Solana.ToString());
    public static string Ethereum => ResolveChainKey(BlockchainNetwork.Ethereum.ToString());

    public static string ResolveChainKey(string blockchainIdentifier)
    {
        if (string.IsNullOrWhiteSpace(blockchainIdentifier))
        {
            return string.Empty;
        }

        var normalized = Normalize(blockchainIdentifier);
        var lookup = BuildIdentifierMap();

        if (lookup.TryGetValue(normalized, out var chainKey))
        {
            return chainKey;
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, string> BuildIdentifierMap()
    {
        var assets = CryptoAssetRegistry.Assets.Values.ToList();

        if (assets.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var nativeByNetwork = assets
            .Where(asset => asset is not null && string.IsNullOrWhiteSpace(asset.ContractAddress))
            .GroupBy(asset => asset.Network)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(asset => asset.SortOrder ?? int.MaxValue)
                    .ThenBy(asset => asset.Code, StringComparer.OrdinalIgnoreCase)
                    .First(),
                EqualityComparer<BlockchainNetwork>.Default);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets)
        {
            if (asset is null)
            {
                continue;
            }

            var chainKey = ResolveNativeChainKey(asset, nativeByNetwork);
            if (string.IsNullOrEmpty(chainKey))
            {
                continue;
            }

            AddIdentifier(map, chainKey, chainKey);
            AddIdentifier(map, Normalize(asset.Code), chainKey);

            if (!string.IsNullOrWhiteSpace(asset.RouteSegment))
            {
                AddIdentifier(map, Normalize(asset.RouteSegment!), chainKey);
            }

            if (asset.Aliases is { Count: > 0 })
            {
                foreach (var alias in asset.Aliases)
                {
                    AddIdentifier(map, Normalize(alias), chainKey);
                }
            }

            AddIdentifier(map, Normalize(asset.Network.ToString()), chainKey);
        }

        return map;
    }

    private static string ResolveNativeChainKey(CryptoAsset asset, IReadOnlyDictionary<BlockchainNetwork, CryptoAsset> nativeByNetwork)
    {
        if (nativeByNetwork.TryGetValue(asset.Network, out var native) && native is not null)
        {
            return Normalize(native.Code);
        }

        return Normalize(asset.Code);
    }

    private static void AddIdentifier(IDictionary<string, string> map, string? identifier, string chainKey)
    {
        if (string.IsNullOrWhiteSpace(identifier) || map.ContainsKey(identifier))
        {
            return;
        }

        map[identifier] = chainKey;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
