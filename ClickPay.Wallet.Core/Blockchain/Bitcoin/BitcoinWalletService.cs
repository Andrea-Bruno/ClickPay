using System;
using System.Collections.Generic;
using System.Linq;
using ClickPay.Wallet.Core.Services;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace ClickPay.Wallet.Core.Blockchain.Bitcoin
{
    internal sealed class BitcoinWalletService
    {
        private readonly BitcoinWalletOptions _options;
        private readonly Network _network;
        private readonly KeyPath _accountBasePath;

        public BitcoinWalletService()
            : this(Options.Create(BitcoinWalletOptions.Default))
        {
        }

        public BitcoinWalletService(IOptions<BitcoinWalletOptions> optionsAccessor)
        {
            _options = optionsAccessor.Value ?? BitcoinWalletOptions.Default;
            _network = Network.GetNetwork(_options.Network) ?? Network.Main;
            _accountBasePath = new KeyPath($"{_options.Purpose}'/{_options.CoinType}'");
        }

        public string GenerateMnemonic(int wordCount = 24)
        {
            if (!BitcoinWalletOptions.ValidWordCounts.Contains(wordCount))
            {
                throw WalletError.OperationFailed();
            }

            var mnemonic = new Mnemonic(Wordlist.English, ResolveWordCount(wordCount));
            return mnemonic.ToString();
        }

        public bool ValidateMnemonic(string? mnemonic)
        {
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                return false;
            }

            try
            {
                _ = new Mnemonic(mnemonic.Trim());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public WalletAccount DeriveAccount(string mnemonic, string? passphrase = null, int accountIndex = 0)
        {
            var parsedMnemonic = new Mnemonic(mnemonic.Trim());
            var masterExtKey = parsedMnemonic.DeriveExtKey(passphrase ?? string.Empty);
            var masterExtPubKey = masterExtKey.Neuter();
            var masterFingerprint = HDFingerprint.FromKeyId(masterExtPubKey.PubKey.Hash);

            var accountPath = _accountBasePath.Derive(new KeyPath($"{accountIndex}'"));
            var accountExtKey = masterExtKey.Derive(accountPath);
            var accountXprv = accountExtKey.GetWif(_network).ToString();
            var accountXpub = accountExtKey.Neuter();

            return new WalletAccount(
                Mnemonic: parsedMnemonic.ToString(),
                Passphrase: passphrase ?? string.Empty,
                Network: _network,
                MasterFingerprint: masterFingerprint,
                AccountIndex: accountIndex,
                AccountKeyPath: accountPath,
                AccountXprv: accountXprv,
                AccountXpub: accountXpub.ToString(_network),
                ExternalAddresses: new AddressDerivation(this, accountExtKey, AccountScope.External),
                InternalAddresses: new AddressDerivation(this, accountExtKey, AccountScope.Internal)
            );
        }

        public Money CalculateBalance(IEnumerable<WalletCoin> coins)
        {
            if (coins is null)
            {
                throw new ArgumentNullException(nameof(coins));
            }

            return coins.Aggregate(Money.Zero, (current, coin) => current + coin.Coin.Amount);
        }

        public PSBT BuildTransaction(
            WalletAccount account,
            IEnumerable<WalletCoin> coins,
            BitcoinAddress destination,
            Money amount,
            FeeRate feeRate,
            int changeIndex = 0)
        {
            if (account is null)
            {
                throw WalletError.OperationFailed();
            }

            if (coins is null)
            {
                throw WalletError.OperationFailed();
            }

            var coinArray = coins as WalletCoin[] ?? coins.ToArray();
            if (coinArray.Length == 0)
            {
                throw WalletError.OperationFailed();
            }

            var accountExtKey = ExtKey.Parse(account.AccountXprv, _network);
            var accountExtPubKey = accountExtKey.Neuter();
            var changeAddress = account.InternalAddresses.GetAddress(changeIndex);

            var ownershipMetadata = new List<(WalletCoin Coin, PubKey PubKey, RootedKeyPath KeyPath)>(coinArray.Length);

            foreach (var walletCoin in coinArray)
            {
                if (walletCoin is null)
                {
                    throw WalletError.OperationFailed();
                }

                var derivedKey = accountExtKey.Derive(walletCoin.KeyPath);
                var pubKey = derivedKey.Neuter().PubKey;
                var expectedScript = pubKey.GetAddress(ScriptPubKeyType.Segwit, _network).ScriptPubKey;

                if (expectedScript != walletCoin.Coin.ScriptPubKey)
                {
                    throw WalletError.OperationFailed();
                }

                var rooted = new RootedKeyPath(account.MasterFingerprint, account.AccountKeyPath.Derive(walletCoin.KeyPath));
                ownershipMetadata.Add((walletCoin, pubKey, rooted));
            }

            var builder = _network.CreateTransactionBuilder();
            builder.AddCoins(coinArray.Select(c => c.Coin));
            builder.Send(destination, amount);
            builder.SetChange(changeAddress);
            builder.SendEstimatedFees(feeRate);

            var psbt = builder.BuildPSBT(false);

            foreach (var (coin, pubKey, rooted) in ownershipMetadata)
            {
                psbt.AddKeyPath(pubKey, rooted);
            }

            return psbt;
        }

        public PSBT SignTransaction(PSBT psbt, WalletAccount account)
        {
            if (psbt is null || account is null)
            {
                throw WalletError.OperationFailed();
            }

            var accountExtKey = ExtKey.Parse(account.AccountXprv, _network);
            return psbt.SignAll(ScriptPubKeyType.Segwit, accountExtKey, new RootedKeyPath(account.MasterFingerprint, account.AccountKeyPath));
        }

        public Transaction FinalizeTransaction(PSBT psbt)
        {
            if (psbt is null)
            {
                throw WalletError.OperationFailed();
            }

            if (!psbt.TryFinalize(out var errors))
            {
                throw WalletError.OperationFailed();
            }

            return psbt.ExtractTransaction();
        }

        internal BitcoinAddress DeriveAddress(ExtKey accountKey, AccountScope scope, int index)
        {
            var chain = scope == AccountScope.External ? 0u : 1u;
            var childKey = accountKey.Derive(chain).Derive((uint)index);
            return childKey.Neuter().PubKey.GetAddress(ScriptPubKeyType.Segwit, _network);
        }

        private static WordCount ResolveWordCount(int wordCount) => wordCount switch
        {
            12 => WordCount.Twelve,
            15 => WordCount.Fifteen,
            18 => WordCount.Eighteen,
            21 => WordCount.TwentyOne,
            24 => WordCount.TwentyFour,
            _ => throw new ArgumentOutOfRangeException(nameof(wordCount))
        };
    }

    internal enum AccountScope
    {
        External,
        Internal
    }

    internal sealed record WalletCoin(Coin Coin, KeyPath KeyPath);

    internal sealed record WalletAccount(
        string Mnemonic,
        string Passphrase,
        Network Network,
        HDFingerprint MasterFingerprint,
        int AccountIndex,
        KeyPath AccountKeyPath,
        string AccountXprv,
        string AccountXpub,
        AddressDerivation ExternalAddresses,
        AddressDerivation InternalAddresses
    );

    internal sealed class AddressDerivation
    {
        private readonly BitcoinWalletService _service;
        private readonly ExtKey _accountKey;
        private readonly AccountScope _scope;

        internal AddressDerivation(BitcoinWalletService service, ExtKey accountKey, AccountScope scope)
        {
            _service = service;
            _accountKey = accountKey;
            _scope = scope;
        }

        public BitcoinAddress GetAddress(int index)
        {
            if (index < 0)
            {
                throw WalletError.AddressIndexOutOfRange();
            }

            return _service.DeriveAddress(_accountKey, _scope, index);
        }
    }

    public sealed class BitcoinWalletOptions
    {
        public static readonly int[] ValidWordCounts = { 12, 15, 18, 21, 24 };

        public static BitcoinWalletOptions Default => new BitcoinWalletOptions();

        public string Network { get; set; } = global::NBitcoin.Network.Main.ToString();
        public int Purpose { get; set; } = 84;
        public int CoinType { get; set; } = 0;
        public int TransactionHistoryLimit { get; set; } = 25;
    }
}
