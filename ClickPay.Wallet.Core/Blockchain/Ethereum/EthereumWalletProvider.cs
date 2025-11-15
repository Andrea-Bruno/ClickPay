using ClickPay.Wallet.Core.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Wallet;

namespace ClickPay.Wallet.Core.Blockchain.Ethereum
{
    internal sealed class EthereumWalletProvider : IWalletProvider
    {
        private readonly EthereumWalletService _walletService;
        private readonly EthereumWalletOptions _options;

        public EthereumWalletProvider(EthereumWalletService walletService, Microsoft.Extensions.Options.IOptions<EthereumWalletOptions> optionsAccessor)
        {
            _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
            _options = optionsAccessor?.Value ?? EthereumWalletOptions.Default;
        }

        public BlockchainNetwork Network => BlockchainNetwork.Ethereum;

        public bool SupportsAsset(CryptoAsset asset)
        {
            return asset is not null && asset.Network == BlockchainNetwork.Ethereum;
        }

        public async Task<WalletOverview> GetOverviewAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            decimal balance;

            if (IsNative(asset))
            {
                balance = await _walletService.GetNativeBalanceAsync(account, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                balance = await _walletService.GetTokenBalanceAsync(account, asset, cancellationToken).ConfigureAwait(false);
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
            return Task.FromResult(new WalletReceiveInfo(asset.Code, GetDisplaySymbol(asset), account.Address, metadata));
        }

        public Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            return _walletService.GetRecentTransactionsAsync(account, asset, _options.TransactionHistoryLimit, cancellationToken);
        }

        public async Task<WalletSendResult> SendAsync(CryptoAsset asset, WalletVault vault, WalletSendRequest request, CancellationToken cancellationToken)
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
            string transactionId;

            if (IsNative(asset))
            {
                transactionId = await _walletService.SendNativeAsync(account, request.DestinationAddress, request.Amount, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                transactionId = await _walletService.SendTokenAsync(account, asset, request.DestinationAddress, request.Amount, cancellationToken).ConfigureAwait(false);
            }

            return new WalletSendResult(asset.Code, GetDisplaySymbol(asset), transactionId, DateTimeOffset.UtcNow);
        }

        private EthereumWalletAccount DeriveAccount(WalletVault vault)
        {
            if (vault is null)
            {
                throw WalletError.VaultUnavailable();
            }

            if (string.IsNullOrWhiteSpace(vault.Mnemonic))
            {
                throw WalletError.MnemonicMissing();
            }

            var chainState = GetChainState(vault);
            var addressIndex = Math.Max(0, chainState.AssociatedAccountIndex);
            return _walletService.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex, addressIndex);
        }

        private static WalletVaultChainState GetChainState(WalletVault vault)
        {
            if (vault.TryGetChainState(WalletChains.Ethereum, out var state))
            {
                return state ?? WalletVaultChainState.Default;
            }

            return WalletVaultChainState.Default;
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
                metadata["contract"] = asset.ContractAddress;
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
