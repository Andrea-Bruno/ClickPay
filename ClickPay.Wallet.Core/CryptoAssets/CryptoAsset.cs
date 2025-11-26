using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClickPay.Wallet.Core.CryptoAssets;

/// <summary>
/// Represents a fungible asset (native coin or token) that can be surfaced in the wallet.
/// </summary>
public sealed class CryptoAsset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("blockchain")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BlockchainNetwork Network { get; init; } = BlockchainNetwork.Unknown;

    [JsonPropertyName("contractAddress")]
    public string? ContractAddress { get; init; }

    [JsonPropertyName("devnetContractAddress")]
    public string? DevnetContractAddress { get; init; }

    [JsonPropertyName("nativeContractAddress")]
    public string? NativeContractAddress { get; init; }

    /// <summary>
    /// Restituisce l'indirizzo del contratto effettivo basato sulla modalità (devnet se DEBUG e disponibile, altrimenti mainnet).
    /// Per asset nativi, usa NativeContractAddress se ContractAddress è null.
    /// </summary>
    public string? EffectiveContractAddress
    {
        get
        {
            var baseAddress = ContractAddress;
#if DEBUG
            if (!string.IsNullOrWhiteSpace(DevnetContractAddress))
            {
                baseAddress = DevnetContractAddress;
            }
#endif
            return string.IsNullOrWhiteSpace(baseAddress) ? NativeContractAddress : baseAddress;
        }
    }

    [JsonPropertyName("decimals")]
    public int Decimals { get; init; } = 0;

    [JsonPropertyName("chainId")]
    public int? ChainId { get; init; }

    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("coingeckoId")]
    public string? CoingeckoId { get; init; }

    [JsonPropertyName("explorerUrl")]
    public string? ExplorerUrl { get; init; }

    [JsonPropertyName("menuCustomLabel")]
    public string? MenuCustomLabel { get; init; }

    [JsonPropertyName("routeSegment")]
    public string? RouteSegment { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string>? Aliases { get; init; }

    [JsonPropertyName("sortOrder")]
    public int? SortOrder { get; init; }

    /// <summary>
    /// Optional Unicode glyph to display for this asset in place of an icon.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }

    [JsonPropertyName("feeAssetCodes")]
    public IReadOnlyList<string>? FeeAssetCodes { get; init; }

    [JsonPropertyName("networkFeeMinimumBalance")]
    public decimal? NetworkFeeMinimumBalance { get; init; }

    [JsonPropertyName("visibilityLocked")]
    public bool VisibilityLocked { get; init; }

    [JsonIgnore]
    public string MenuLabel => string.IsNullOrWhiteSpace(MenuCustomLabel)
        ? Code
        : MenuCustomLabel!;
}
