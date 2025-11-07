using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClickPay.Shared.Services
{
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
        public const string Bitcoin = "btc";
        public const string Solana = "sol";
    }
}
