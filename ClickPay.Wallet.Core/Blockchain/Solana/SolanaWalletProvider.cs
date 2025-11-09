using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Wallet;

namespace ClickPay.Wallet.Core.Blockchain.Solana
{
    internal sealed class SolanaWalletProvider : IWalletProvider
    {
        private readonly SolanaWalletService _walletService;
        private readonly SolanaWalletOptions _options;

        public SolanaWalletProvider(SolanaWalletService walletService, Microsoft.Extensions.Options.IOptions<SolanaWalletOptions> optionsAccessor)
        {
            _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
            _options = optionsAccessor?.Value ?? SolanaWalletOptions.Default;
        }

        public BlockchainNetwork Network => BlockchainNetwork.Solana;

        public bool SupportsAsset(CryptoAsset asset)
        {
            return asset is not null && asset.Network == BlockchainNetwork.Solana;
        }

        public async Task<WalletOverview> GetOverviewAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            decimal balance;

            if (IsNative(asset))
            {
                balance = await _walletService.GetNativeSolBalanceAsync(account, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var tokenBalance = await _walletService.GetTokenBalanceAsync(account, asset, cancellationToken).ConfigureAwait(false);
                balance = tokenBalance.Amount;
            }

            var transactions = await _walletService.GetRecentTransactionsAsync(account, asset, _options.TransactionHistoryLimit, cancellationToken).ConfigureAwait(false);

            return new WalletOverview(
                asset.Code,
                GetDisplaySymbol(asset),
                balance,
                transactions,
                FiatEstimate: null,
                NativeBalanceDescriptor: null);
        }

        public Task<WalletReceiveInfo> GetReceiveInfoAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            var metadata = BuildMetadata(asset);
            var address = account.Account.PublicKey.Key;
            return Task.FromResult(new WalletReceiveInfo(asset.Code, GetDisplaySymbol(asset), address, metadata));
        }

        public Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            return _walletService.GetRecentTransactionsAsync(account, asset, _options.TransactionHistoryLimit, cancellationToken);
        }

        public Task<WalletSendResult> SendAsync(CryptoAsset asset, WalletVault vault, WalletSendRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.DestinationAddress))
            {
                throw WalletError.DestinationMissing();
            }

            if (request.Amount <= 0m)
            {
                throw WalletError.AmountInvalid();
            }

            var account = DeriveAccount(vault);
            return _walletService.SendAsync(account, asset, request.DestinationAddress, request.Amount, cancellationToken);
        }

        private SolanaWalletAccount DeriveAccount(WalletVault vault)
        {
            if (vault is null)
            {
                throw WalletError.VaultUnavailable();
            }

            if (string.IsNullOrWhiteSpace(vault.Mnemonic))
            {
                throw WalletError.MnemonicMissing();
            }

            return _walletService.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
        }

        private static bool IsNative(CryptoAsset asset)
        {
            return string.IsNullOrWhiteSpace(asset.ContractAddress);
        }

        private static IReadOnlyDictionary<string, string>? BuildMetadata(CryptoAsset asset)
        {
            if (IsNative(asset))
            {
                return null;
            }

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(asset.ContractAddress))
            {
                metadata["mint"] = asset.ContractAddress;
            }

            if (asset.Decimals > 0)
            {
                metadata["decimals"] = asset.Decimals.ToString(CultureInfo.InvariantCulture);
            }

            return metadata.Count == 0 ? null : metadata;
        }

        private static string GetDisplaySymbol(CryptoAsset asset)
        {
            return string.IsNullOrWhiteSpace(asset.Symbol) ? asset.Code : asset.Symbol;
        }
    }
}
