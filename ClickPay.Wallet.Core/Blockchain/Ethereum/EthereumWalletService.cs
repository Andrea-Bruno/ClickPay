using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Options;
using Nethereum.Hex.HexTypes;
using Nethereum.HdWallet;
using Nethereum.JsonRpc.Client;
using Nethereum.StandardTokenEIP20;
using Nethereum.Util;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using EthereumMnemonicWallet = Nethereum.HdWallet.Wallet;

namespace ClickPay.Wallet.Core.Blockchain.Ethereum
{
    internal sealed class EthereumWalletService
    {
        private readonly EthereumWalletOptions _options;
        private readonly TimeSpan _rpcTimeout;
        private readonly HttpClient _httpClient;

        public EthereumWalletService(IOptions<EthereumWalletOptions> optionsAccessor)
        {
            _options = optionsAccessor.Value ?? EthereumWalletOptions.Default;
            var timeoutSeconds = _options.RpcTimeoutSeconds > 0 ? _options.RpcTimeoutSeconds : EthereumWalletOptions.DefaultRpcTimeoutSeconds;
            _rpcTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300));
            _httpClient = new HttpClient
            {
                Timeout = _rpcTimeout
            };

            ClientBase.ConnectionTimeout = _rpcTimeout;
        }

        public EthereumWalletAccount DeriveAccount(string mnemonic, string? passphrase, int accountIndex, int addressIndex)
        {
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                throw new ArgumentException("Mnemonic mancante.", nameof(mnemonic));
            }

            if (accountIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(accountIndex));
            }

            if (addressIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(addressIndex));
            }

            var basePath = BuildAccountBasePath(accountIndex);
            var wallet = new EthereumMnemonicWallet(mnemonic.Trim(), passphrase ?? string.Empty, basePath, random: null);
            var rawAccount = wallet.GetAccount(addressIndex);
            var derivationPath = $"{basePath}/{addressIndex}";
            var account = new Account(rawAccount.PrivateKey, _options.ChainId);
            return new EthereumWalletAccount(account, derivationPath, accountIndex, addressIndex);
        }

        public async Task<decimal> GetNativeBalanceAsync(EthereumWalletAccount account, CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(account, async web3 =>
            {
                var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(account.Address).ConfigureAwait(false);
                return UnitConversion.Convert.FromWei(balanceWei.Value);
            }).ConfigureAwait(false);
        }

        public async Task<decimal> GetTokenBalanceAsync(EthereumWalletAccount account, CryptoAsset asset, CancellationToken cancellationToken = default)
        {
            EnsureEthereumToken(asset);

            return await ExecuteAsync(account, async web3 =>
            {
                var service = new StandardTokenService(web3, asset.ContractAddress!);
                var balance = await service.BalanceOfQueryAsync(account.Address).ConfigureAwait(false);
                var decimals = asset.Decimals > 0 ? asset.Decimals : EthereumWalletOptions.DefaultTokenDecimals;
                return UnitConversion.Convert.FromWei(balance, decimals);
            }).ConfigureAwait(false);
        }

        public async Task<string> SendNativeAsync(EthereumWalletAccount account, string destinationAddress, decimal amount, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destinationAddress))
            {
                throw new ArgumentException("Indirizzo di destinazione mancante.", nameof(destinationAddress));
            }

            if (amount <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "L'importo deve essere positivo.");
            }

            var normalizedDestination = NormalizeAddress(destinationAddress);
            if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(normalizedDestination))
            {
                throw new InvalidOperationException("Indirizzo Ethereum non valido.");
            }

            return await ExecuteAsync(account, async web3 =>
            {
                var weiAmount = UnitConversion.Convert.ToWei(amount);

                var txInput = new Nethereum.RPC.Eth.DTOs.TransactionInput
                {
                    From = account.Address,
                    To = normalizedDestination,
                    Value = new HexBigInteger(weiAmount)
                };

                var gasEstimate = await web3.TransactionManager.EstimateGasAsync(txInput).ConfigureAwait(false);
                var gasPrice = await GetGasPriceAsync(web3).ConfigureAwait(false);

                txInput.Gas = gasEstimate;
                txInput.GasPrice = gasPrice;

                return await web3.TransactionManager.SendTransactionAsync(txInput).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        public async Task<string> SendTokenAsync(EthereumWalletAccount account, CryptoAsset asset, string destinationAddress, decimal amount, CancellationToken cancellationToken = default)
        {
            EnsureEthereumToken(asset);

            if (string.IsNullOrWhiteSpace(destinationAddress))
            {
                throw new ArgumentException("Indirizzo di destinazione mancante.", nameof(destinationAddress));
            }

            if (amount <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "L'importo deve essere positivo.");
            }

            var normalizedDestination = NormalizeAddress(destinationAddress);
            if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(normalizedDestination))
            {
                throw new InvalidOperationException("Indirizzo Ethereum non valido.");
            }

            return await ExecuteAsync(account, async web3 =>
            {
                var service = new StandardTokenService(web3, asset.ContractAddress!);
                var decimals = asset.Decimals > 0 ? asset.Decimals : EthereumWalletOptions.DefaultTokenDecimals;
                var rawAmount = UnitConversion.Convert.ToWei(amount, decimals);

                return await service.TransferRequestAsync(normalizedDestination, rawAmount).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<WalletTransaction>> GetRecentTransactionsAsync(EthereumWalletAccount account, CryptoAsset asset, int? limit = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WalletTransaction>>(Array.Empty<WalletTransaction>());
        }

        private Web3 CreateWeb3(EthereumWalletAccount account)
        {
            var endpoint = new Uri(_options.RpcEndpoint);
            var rpcClient = new RpcClient(endpoint, _httpClient);
            return new Web3(account.Account, rpcClient);
        }

        private string BuildAccountBasePath(int accountIndex)
        {
            var purpose = _options.Purpose;
            var coinType = _options.CoinType;
            var change = _options.ChangeBranch;
            return $"m/{purpose}'/{coinType}'/{accountIndex}'/{change}";
        }

        private static string NormalizeAddress(string address) => address.Trim();

        private static void EnsureEthereumToken(CryptoAsset asset)
        {
            if (asset is null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (asset.Network != BlockchainNetwork.Ethereum)
            {
                throw new InvalidOperationException($"L'asset {asset.Code} non appartiene alla rete Ethereum.");
            }

            if (string.IsNullOrWhiteSpace(asset.ContractAddress))
            {
                throw new InvalidOperationException($"L'asset {asset.Code} non specifica un contractAddress per le operazioni ERC-20.");
            }
        }

        private async Task<HexBigInteger> GetGasPriceAsync(Web3 web3)
        {
            var gasPrice = await web3.Eth.GasPrice.SendRequestAsync().ConfigureAwait(false);
            if (gasPrice.Value != 0)
            {
                return gasPrice;
            }

            var fallbackWei = UnitConversion.Convert.ToWei(_options.FallbackGasPriceGwei, UnitConversion.EthUnit.Gwei);
            return new HexBigInteger(fallbackWei);
        }

        private async Task<T> ExecuteAsync<T>(EthereumWalletAccount account, Func<Web3, Task<T>> operation)
        {
            var web3 = CreateWeb3(account);

            try
            {
                return await operation(web3).ConfigureAwait(false);
            }
            catch (RpcClientTimeoutException ex)
            {
                throw new TimeoutException("Operazione Ethereum scaduta.", ex);
            }
            catch (RpcClientUnknownException ex)
            {
                throw new InvalidOperationException($"Errore RPC Ethereum: {ex.Message}", ex);
            }
        }
    }

    internal sealed record EthereumWalletAccount(Account Account, string DerivationPath, int AccountIndex, int AddressIndex)
    {
        public string Address => Account.Address;
        public string PrivateKey => Account.PrivateKey;
    }

    public sealed class EthereumWalletOptions
    {
        public const int DefaultPurpose = 44;
        public const int DefaultCoinType = 60;
        public const int DefaultTokenDecimals = 18;
        public const int DefaultRpcTimeoutSeconds = 30;

        public static EthereumWalletOptions Default => new();

        public string RpcEndpoint { get; set; } = "https://ethereum.publicnode.com";
        public long ChainId { get; set; } = 1;
        public int FallbackGasPriceGwei { get; set; } = 15;
        public int Purpose { get; set; } = DefaultPurpose;
        public int CoinType { get; set; } = DefaultCoinType;
        public int ChangeBranch { get; set; } = 0;
        public int TransactionHistoryLimit { get; set; } = 25;
        public int RpcTimeoutSeconds { get; set; } = DefaultRpcTimeoutSeconds;
    }
}
