using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Shared.CryptoAssets;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace ClickPay.Shared.Services
{
    public class MultiChainWalletService
    {
        private readonly WalletKeyService _vaults;
        private readonly BitcoinWalletService _bitcoin;
        private readonly SolanaWalletService _solana;
        private readonly BitcoinNetworkClient _bitcoinNetworkClient;
        private readonly SolanaWalletOptions _solanaOptions;
        private readonly BitcoinWalletOptions _bitcoinOptions;

        public MultiChainWalletService(
            WalletKeyService vaults,
            BitcoinWalletService bitcoin,
            SolanaWalletService solana,
            BitcoinNetworkClient bitcoinNetworkClient,
            IOptions<BitcoinWalletOptions> bitcoinOptions,
            IOptions<SolanaWalletOptions> solanaOptions)
        {
            _vaults = vaults;
            _bitcoin = bitcoin;
            _solana = solana;
            _bitcoinNetworkClient = bitcoinNetworkClient;
            _bitcoinOptions = bitcoinOptions.Value ?? BitcoinWalletOptions.Default;
            _solanaOptions = solanaOptions.Value ?? SolanaWalletOptions.Default;
        }

        public async Task<WalletOverview> GetOverviewAsync(WalletAsset asset, CancellationToken cancellationToken = default)
        {
            var definition = WalletAssetHelper.GetDefinition(asset);

            return asset switch
            {
                WalletAsset.Bitcoin => await GetBitcoinOverviewAsync(definition, cancellationToken).ConfigureAwait(false),
                WalletAsset.Eurc => await GetSolanaTokenOverviewAsync(WalletAsset.Eurc, definition, cancellationToken).ConfigureAwait(false),
                WalletAsset.Xaut => await GetSolanaTokenOverviewAsync(WalletAsset.Xaut, definition, cancellationToken).ConfigureAwait(false),
                WalletAsset.Sol => await GetSolOverviewAsync(definition, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Asset {asset} non supportato.")
            };
        }

        public async Task<WalletReceiveInfo> GetReceiveInfoAsync(WalletAsset asset, CancellationToken cancellationToken = default)
        {
            var definition = WalletAssetHelper.GetDefinition(asset);

            return asset switch
            {
                WalletAsset.Bitcoin => await GetBitcoinReceiveInfoAsync(definition, cancellationToken).ConfigureAwait(false),
                WalletAsset.Eurc => await GetSolanaReceiveInfoAsync(WalletAsset.Eurc, definition, cancellationToken).ConfigureAwait(false),
                WalletAsset.Xaut => await GetSolanaReceiveInfoAsync(WalletAsset.Xaut, definition, cancellationToken).ConfigureAwait(false),
                WalletAsset.Sol => await GetSolanaReceiveInfoAsync(WalletAsset.Sol, definition, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Asset {asset} non supportato.")
            };
        }

        public async Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(WalletAsset asset, CancellationToken cancellationToken = default)
        {
            return asset switch
            {
                WalletAsset.Bitcoin => await GetBitcoinTransactionsAsync(cancellationToken).ConfigureAwait(false),
                WalletAsset.Eurc => await GetSolanaTransactionsAsync(WalletAsset.Eurc, cancellationToken).ConfigureAwait(false),
                WalletAsset.Xaut => await GetSolanaTransactionsAsync(WalletAsset.Xaut, cancellationToken).ConfigureAwait(false),
                WalletAsset.Sol => await GetSolanaTransactionsAsync(WalletAsset.Sol, cancellationToken).ConfigureAwait(false),
                _ => Array.Empty<WalletTransaction>()
            };
        }

        public async Task<WalletSendResult> SendAsync(WalletAsset asset, string destination, decimal amount, CancellationToken cancellationToken = default)
        {
            var definition = WalletAssetHelper.GetDefinition(asset);

            return asset switch
            {
                WalletAsset.Bitcoin => await SendBitcoinAsync(definition, destination, amount, cancellationToken).ConfigureAwait(false),
                WalletAsset.Eurc => await SendSolanaAsync(WalletAsset.Eurc, definition, destination, amount, cancellationToken).ConfigureAwait(false),
                WalletAsset.Xaut => await SendSolanaAsync(WalletAsset.Xaut, definition, destination, amount, cancellationToken).ConfigureAwait(false),
                WalletAsset.Sol => await SendSolanaAsync(WalletAsset.Sol, definition, destination, amount, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Asset {asset} non supportato.")
            };
        }

        public async Task<decimal> GetSolBalanceAsync(CancellationToken cancellationToken = default)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _solana.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            return await _solana.GetNativeSolBalanceAsync(account, cancellationToken).ConfigureAwait(false);
        }

        private async Task<WalletVault> RequireVaultAsync(CancellationToken cancellationToken)
        {
            var vault = await _vaults.GetVaultAsync(cancellationToken).ConfigureAwait(false);
            return vault ?? throw new InvalidOperationException("Nessun wallet trovato. Completa l'onboarding.");
        }

        private WalletVaultChainState GetChainState(WalletVault vault, string chainKey)
        {
            return vault.TryGetChainState(chainKey, out var state) ? state : WalletVaultChainState.Default;
        }

        private async Task<WalletOverview> GetBitcoinOverviewAsync(CryptoAsset definition, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _bitcoin.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var chainState = GetChainState(vault, WalletChains.Bitcoin);
            var receiveAddress = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);
            var changeAddress = account.InternalAddresses.GetAddress(chainState.InternalAddressIndex);

            var receiveBalanceTask = _bitcoinNetworkClient.GetBalanceAsync(receiveAddress, cancellationToken);
            var changeBalanceTask = _bitcoinNetworkClient.GetBalanceAsync(changeAddress, cancellationToken);
            await Task.WhenAll(receiveBalanceTask, changeBalanceTask).ConfigureAwait(false);

            var balance = (receiveBalanceTask.Result?.TotalConfirmed ?? Money.Zero) + (changeBalanceTask.Result?.TotalConfirmed ?? Money.Zero);
            var transactions = await MapBitcoinTransactionsAsync(account, receiveAddress, changeAddress, chainState, cancellationToken).ConfigureAwait(false);

            return new WalletOverview(
                WalletAsset.Bitcoin,
                definition.Code,
                balance.ToDecimal(MoneyUnit.BTC),
                receiveAddress.ToString(),
                transactions,
                null,
                $"{balance.ToDecimal(MoneyUnit.Satoshi)} sats");
        }

        private async Task<IReadOnlyList<WalletTransaction>> MapBitcoinTransactionsAsync(WalletAccount account, BitcoinAddress primaryAddress, BitcoinAddress changeAddress, WalletVaultChainState chainState, CancellationToken cancellationToken)
        {
            var trackedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                primaryAddress.ToString(),
                changeAddress.ToString()
            };

            var primaryTransactionsTask = _bitcoinNetworkClient.GetTransactionsAsync(primaryAddress, limit: 25, cancellationToken);
            var changeTransactionsTask = _bitcoinNetworkClient.GetTransactionsAsync(changeAddress, limit: 25, cancellationToken);
            await Task.WhenAll(primaryTransactionsTask, changeTransactionsTask).ConfigureAwait(false);

            var rawTransactions = primaryTransactionsTask.Result
                .Concat(changeTransactionsTask.Result)
                .GroupBy(tx => tx.TxId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var list = new List<WalletTransaction>();

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
                    null,
                    null,
                    tx.Fee / 100_000_000m));
            }

            return list.OrderByDescending(t => t.Timestamp).ToArray();
        }

        private async Task<WalletReceiveInfo> GetBitcoinReceiveInfoAsync(CryptoAsset definition, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _bitcoin.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var chainState = GetChainState(vault, WalletChains.Bitcoin);
            var address = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);
            return new WalletReceiveInfo(WalletAsset.Bitcoin, definition.Code, address.ToString());
        }

        private async Task<IReadOnlyList<WalletTransaction>> GetBitcoinTransactionsAsync(CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _bitcoin.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var chainState = GetChainState(vault, WalletChains.Bitcoin);
            var address = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);
            var changeAddress = account.InternalAddresses.GetAddress(chainState.InternalAddressIndex);
            return await MapBitcoinTransactionsAsync(account, address, changeAddress, chainState, cancellationToken).ConfigureAwait(false);
        }

        private async Task<WalletSendResult> SendBitcoinAsync(CryptoAsset definition, string destination, decimal amount, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _bitcoin.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var chainState = GetChainState(vault, WalletChains.Bitcoin);

            var destinationAddress = BitcoinAddress.Create(destination, account.Network);
            var receiveAddress = account.ExternalAddresses.GetAddress(chainState.ExternalAddressIndex);
            var changeAddress = account.InternalAddresses.GetAddress(chainState.InternalAddressIndex);

            var externalKeyPath = new KeyPath($"0/{chainState.ExternalAddressIndex}");
            var internalKeyPath = new KeyPath($"1/{chainState.InternalAddressIndex}");

            var allUtxos = new List<(BlockstreamUtxo Utxo, KeyPath Path)>();

            var externalUtxos = await _bitcoinNetworkClient.GetUtxosAsync(receiveAddress, cancellationToken).ConfigureAwait(false);
            foreach (var utxo in externalUtxos)
            {
                allUtxos.Add((utxo, externalKeyPath));
            }

            if (!string.Equals(receiveAddress.ToString(), changeAddress.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                var internalUtxos = await _bitcoinNetworkClient.GetUtxosAsync(changeAddress, cancellationToken).ConfigureAwait(false);
                foreach (var utxo in internalUtxos)
                {
                    allUtxos.Add((utxo, internalKeyPath));
                }
            }

            if (allUtxos.Count == 0)
            {
                throw new InvalidOperationException("Nessun UTXO disponibile per costruire la transazione BTC.");
            }

            var coins = new List<WalletCoin>();
            foreach (var (utxo, keyPath) in allUtxos)
            {
                var outPoint = new OutPoint(uint256.Parse(utxo.TxId), utxo.Vout);
                var script = Script.FromHex(utxo.ScriptPubKey);
                var txOut = new TxOut(Money.Satoshis(utxo.Value), script);
                coins.Add(new WalletCoin(new Coin(outPoint, txOut), keyPath));
            }

            var feeRate = await _bitcoinNetworkClient.GetRecommendedFeeRateAsync(cancellationToken).ConfigureAwait(false);
            var internalAddress = account.InternalAddresses.GetAddress(chainState.InternalAddressIndex);
            var psbt = _bitcoin.BuildTransaction(account, coins, destinationAddress, Money.Coins(amount), feeRate, chainState.InternalAddressIndex);
            var signed = _bitcoin.SignTransaction(psbt, account);
            var finalized = _bitcoin.FinalizeTransaction(signed);
            var txId = await _bitcoinNetworkClient.BroadcastAsync(finalized, cancellationToken).ConfigureAwait(false);

            return new WalletSendResult(WalletAsset.Bitcoin, definition.Code, txId, DateTimeOffset.UtcNow);
        }

        private async Task<WalletOverview> GetSolanaTokenOverviewAsync(WalletAsset asset, CryptoAsset definition, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _solana.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var balance = await _solana.GetTokenBalanceAsync(account, asset, cancellationToken).ConfigureAwait(false);
            var transactions = await _solana.GetRecentTransactionsAsync(account, asset, _solanaOptions.TransactionHistoryLimit, cancellationToken).ConfigureAwait(false);

            decimal? fiatEstimate = asset == WalletAsset.Eurc ? balance.Amount : null;

            return new WalletOverview(
                asset,
                definition.Code,
                balance.Amount,
                account.PublicKey,
                transactions,
                fiatEstimate,
                null);
        }

        private async Task<WalletOverview> GetSolOverviewAsync(CryptoAsset definition, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _solana.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var balance = await _solana.GetNativeSolBalanceAsync(account, cancellationToken).ConfigureAwait(false);
            var transactions = await _solana.GetRecentTransactionsAsync(account, WalletAsset.Sol, _solanaOptions.TransactionHistoryLimit, cancellationToken).ConfigureAwait(false);

            return new WalletOverview(
                WalletAsset.Sol,
                definition.Code,
                balance,
                account.PublicKey,
                transactions,
                null,
                null);
        }

        private async Task<WalletReceiveInfo> GetSolanaReceiveInfoAsync(WalletAsset asset, CryptoAsset definition, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _solana.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var metadata = BuildSolanaTokenMetadata(asset, definition);
            return new WalletReceiveInfo(asset, definition.Code, account.PublicKey, metadata);
        }

        private IReadOnlyDictionary<string, string>? BuildSolanaTokenMetadata(WalletAsset asset, CryptoAsset definition)
        {
            if (asset == WalletAsset.Sol)
            {
                return null;
            }

            var result = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(definition.ContractAddress))
            {
                result["mint"] = definition.ContractAddress;
            }

            if (definition.Decimals > 0)
            {
                result["decimals"] = definition.Decimals.ToString(CultureInfo.InvariantCulture);
            }

            return result.Count > 0 ? result : null;
        }

        private async Task<IReadOnlyList<WalletTransaction>> GetSolanaTransactionsAsync(WalletAsset asset, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _solana.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            return await _solana.GetRecentTransactionsAsync(account, asset, _solanaOptions.TransactionHistoryLimit, cancellationToken).ConfigureAwait(false);
        }

        private async Task<WalletSendResult> SendSolanaAsync(WalletAsset asset, CryptoAsset definition, string destination, decimal amount, CancellationToken cancellationToken)
        {
            var vault = await RequireVaultAsync(cancellationToken).ConfigureAwait(false);
            var account = _solana.DeriveAccount(vault.Mnemonic, vault.Passphrase, vault.AccountIndex);
            var result = await _solana.SendAsync(account, asset, destination, amount, cancellationToken).ConfigureAwait(false);
            return result.Asset == asset && !string.Equals(result.Symbol, definition.Code, StringComparison.Ordinal)
                ? result with { Symbol = definition.Code }
                : result;
        }
    }
}
