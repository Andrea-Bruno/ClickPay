using ClickPay.Wallet.Core.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Wallet;

namespace ClickPay.Wallet.Core.Blockchain.Stellar
{
    internal sealed class StellarWalletProvider : IWalletProvider
    {
        private readonly StellarWalletService _walletService;
        private readonly StellarWalletOptions _options;

        public StellarWalletProvider(StellarWalletService walletService, Microsoft.Extensions.Options.IOptions<StellarWalletOptions> optionsAccessor)
        {
            _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
            _options = optionsAccessor?.Value ?? StellarWalletOptions.Default;
        }

        public BlockchainNetwork Network => BlockchainNetwork.Stellar;

        public bool SupportsAsset(CryptoAsset asset)
        {
            return asset is not null && asset.Network == BlockchainNetwork.Stellar;
        }

        public async Task<WalletOverview> GetOverviewAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            decimal balance = 0m;

            try
            {
                if (IsNative(asset))
                {
                    balance = await _walletService.GetNativeXlmBalanceAsync(account, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var tokenBalance = await _walletService.GetTokenBalanceAsync(account, asset, cancellationToken).ConfigureAwait(false);
                    balance = tokenBalance.Amount;
                }
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or TimeoutException)
            {
                // Network error: return balance 0 and empty transactions
            }

            IReadOnlyList<WalletTransaction> transactions = Array.Empty<WalletTransaction>();
            try
            {
                transactions = await _walletService.GetRecentTransactionsAsync(account, asset, _options.TransactionHistoryLimit, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or TimeoutException)
            {
                // Network error: return empty transactions
            }

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
            var address = account.PublicKey;
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

        private StellarWalletAccount DeriveAccount(WalletVault vault)
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
                // For Stellar, contract address typically contains both asset code and issuer
                var parts = asset.ContractAddress.Split(':');
                if (parts.Length == 2)
                {
                    metadata["asset_code"] = parts[0].Trim();
                    metadata["issuer"] = parts[1].Trim();
                }
                else
                {
                    metadata["contract"] = asset.ContractAddress;
                }
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