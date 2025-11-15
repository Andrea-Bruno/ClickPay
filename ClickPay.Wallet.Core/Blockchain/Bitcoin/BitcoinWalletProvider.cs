using ClickPay.Wallet.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Wallet;
using NBitcoin;

namespace ClickPay.Wallet.Core.Blockchain.Bitcoin
{
    internal sealed class BitcoinWalletProvider : IWalletProvider
    {
        private readonly BitcoinWalletService _walletService;
        private readonly BitcoinNetworkClient _networkClient;
        private readonly BitcoinWalletOptions _options;

        public BitcoinWalletProvider(
            BitcoinWalletService walletService,
            BitcoinNetworkClient networkClient,
            Microsoft.Extensions.Options.IOptions<BitcoinWalletOptions> optionsAccessor)
        {
            _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
            _networkClient = networkClient ?? throw new ArgumentNullException(nameof(networkClient));
            _options = optionsAccessor?.Value ?? BitcoinWalletOptions.Default;
        }

        public BlockchainNetwork Network => BlockchainNetwork.Bitcoin;

        public bool SupportsAsset(CryptoAsset asset)
        {
            return asset is not null && asset.Network == BlockchainNetwork.Bitcoin;
        }

        public async Task<WalletOverview> GetOverviewAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            var chainState = GetChainState(vault);
            var receiveAddress = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);
            var changeAddress = account.InternalAddresses.GetAddress(chainState.InternalAddressIndex);

            var receiveBalanceTask = _networkClient.GetBalanceAsync(receiveAddress, cancellationToken);
            var changeBalanceTask = _networkClient.GetBalanceAsync(changeAddress, cancellationToken);
            await Task.WhenAll(receiveBalanceTask, changeBalanceTask).ConfigureAwait(false);

            var balance = (receiveBalanceTask.Result?.TotalConfirmed ?? Money.Zero) + (changeBalanceTask.Result?.TotalConfirmed ?? Money.Zero);
            var transactions = await MapTransactionsAsync(account, receiveAddress, changeAddress, chainState, cancellationToken).ConfigureAwait(false);

            return new WalletOverview(
                asset.Code,
                GetDisplaySymbol(asset),
                balance.ToDecimal(MoneyUnit.BTC),
                transactions,
                FiatEstimate: null,
                NativeBalanceDescriptor: $"{balance.Satoshi} sats");
        }

        public Task<WalletReceiveInfo> GetReceiveInfoAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            var chainState = GetChainState(vault);
            var address = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);

            return Task.FromResult(new WalletReceiveInfo(asset.Code, GetDisplaySymbol(asset), address.ToString()));
        }

        public async Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken)
        {
            var account = DeriveAccount(vault);
            var chainState = GetChainState(vault);
            var receiveAddress = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);
            var changeAddress = account.InternalAddresses.GetAddress(chainState.InternalAddressIndex);
            return await MapTransactionsAsync(account, receiveAddress, changeAddress, chainState, cancellationToken).ConfigureAwait(false);
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
            var chainState = GetChainState(vault);

            BitcoinAddress destination;
            try
            {
                destination = BitcoinAddress.Create(request.DestinationAddress.Trim(), account.Network);
            }
            catch (Exception ex)
            {
                throw WalletError.InvalidAddress("Indirizzo Bitcoin non valido.", ex);
            }

            var receiveAddress = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);
            var changeAddress = account.InternalAddresses.GetAddress(chainState.InternalAddressIndex);

            var externalKeyPath = new KeyPath($"0/{chainState.ExternalAddressIndex}");
            var internalKeyPath = new KeyPath($"1/{chainState.InternalAddressIndex}");

            var coins = await CollectCoinsAsync(account, receiveAddress, changeAddress, externalKeyPath, internalKeyPath, cancellationToken).ConfigureAwait(false);
            if (coins.Count == 0)
            {
                throw WalletError.OperationFailed("Nessun UTXO disponibile per costruire la transazione BTC.");
            }

            var feeRate = await _networkClient.GetRecommendedFeeRateAsync(cancellationToken).ConfigureAwait(false);
            var psbt = _walletService.BuildTransaction(account, coins, destination, Money.Coins(request.Amount), feeRate, chainState.InternalAddressIndex);
            var signed = _walletService.SignTransaction(psbt, account);
            var finalized = _walletService.FinalizeTransaction(signed);
            var txId = await _networkClient.BroadcastAsync(finalized, cancellationToken).ConfigureAwait(false);

            return new WalletSendResult(asset.Code, GetDisplaySymbol(asset), txId, DateTimeOffset.UtcNow);
        }

        private WalletAccount DeriveAccount(WalletVault vault)
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

        private static WalletVaultChainState GetChainState(WalletVault vault)
        {
            if (vault.TryGetChainState(WalletChains.Bitcoin, out var state))
            {
                return state ?? WalletVaultChainState.Default;
            }

            return WalletVaultChainState.Default;
        }

        private async Task<IReadOnlyList<WalletTransaction>> MapTransactionsAsync(
            WalletAccount account,
            BitcoinAddress receiveAddress,
            BitcoinAddress changeAddress,
            WalletVaultChainState chainState,
            CancellationToken cancellationToken)
        {
            var trackedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                receiveAddress.ToString(),
                changeAddress.ToString()
            };

            var receiveTask = _networkClient.GetTransactionsAsync(receiveAddress, limit: _options.TransactionHistoryLimit > 0 ? _options.TransactionHistoryLimit : 25, cancellationToken);
            var changeTask = _networkClient.GetTransactionsAsync(changeAddress, limit: _options.TransactionHistoryLimit > 0 ? _options.TransactionHistoryLimit : 25, cancellationToken);
            await Task.WhenAll(receiveTask, changeTask).ConfigureAwait(false);

            var rawTransactions = receiveTask.Result
                .Concat(changeTask.Result)
                .GroupBy(tx => tx.TxId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            var list = new List<WalletTransaction>(rawTransactions.Count);
            foreach (var tx in rawTransactions)
            {
                var incoming = tx.Vout.Where(v => trackedAddresses.Contains(v.Address)).Sum(v => v.AsBtc);
                var outgoing = tx.Vin.Select(v => v.Prevout).Where(v => trackedAddresses.Contains(v.Address)).Sum(v => v.AsBtc);
                var delta = incoming - outgoing;
                var isIncoming = delta >= 0m;
                var absolute = Math.Abs(delta);
                var timestamp = tx.Status.BlockTime is null
                    ? DateTimeOffset.UtcNow
                    : DateTimeOffset.FromUnixTimeSeconds(tx.Status.BlockTime.Value);

                list.Add(new WalletTransaction(
                    tx.TxId,
                    timestamp,
                    absolute,
                    isIncoming,
                    Counterparty: null,
                    Memo: null,
                    Fee: tx.Fee / 100_000_000m));
            }

            return list.OrderByDescending(t => t.Timestamp).ToArray();
        }

        private async Task<List<WalletCoin>> CollectCoinsAsync(
            WalletAccount account,
            BitcoinAddress receiveAddress,
            BitcoinAddress changeAddress,
            KeyPath externalPath,
            KeyPath internalPath,
            CancellationToken cancellationToken)
        {
            var all = new List<WalletCoin>();

            var receiveUtxos = await _networkClient.GetUtxosAsync(receiveAddress, cancellationToken).ConfigureAwait(false);
            foreach (var utxo in receiveUtxos)
            {
                var outPoint = new OutPoint(uint256.Parse(utxo.TxId), utxo.Vout);
                var script = Script.FromHex(utxo.ScriptPubKey);
                var txOut = new TxOut(Money.Satoshis(utxo.Value), script);
                all.Add(new WalletCoin(new Coin(outPoint, txOut), externalPath));
            }

            if (!string.Equals(receiveAddress.ToString(), changeAddress.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var changeUtxos = await _networkClient.GetUtxosAsync(changeAddress, cancellationToken).ConfigureAwait(false);
                foreach (var utxo in changeUtxos)
                {
                    var outPoint = new OutPoint(uint256.Parse(utxo.TxId), utxo.Vout);
                    var script = Script.FromHex(utxo.ScriptPubKey);
                    var txOut = new TxOut(Money.Satoshis(utxo.Value), script);
                    all.Add(new WalletCoin(new Coin(outPoint, txOut), internalPath));
                }
            }

            return all;
        }

        private static string GetDisplaySymbol(CryptoAsset asset)
        {
            return string.IsNullOrWhiteSpace(asset.Symbol) ? asset.Code : asset.Symbol;
        }
    }
}
