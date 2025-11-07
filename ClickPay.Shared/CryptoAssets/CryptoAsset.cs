using System;
using System.Text.Json.Serialization;

namespace ClickPay.Shared.CryptoAssets;

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

    /// <summary>
    /// Optional Unicode glyph to display for this asset in place of an icon.
    /// </summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; init; }

    [JsonPropertyName("hidden")]
    public bool Hidden { get; init; }

    [JsonIgnore]
    public string MenuLabel => string.IsNullOrWhiteSpace(MenuCustomLabel)
        ? Code
        : MenuCustomLabel!;
}
