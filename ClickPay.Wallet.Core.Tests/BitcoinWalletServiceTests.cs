using System;
using ClickPay.Wallet.Core.Blockchain.Bitcoin;
using Microsoft.Extensions.Options;
using NBitcoin;
using Xunit;

namespace ClickPay.Wallet.Core.Tests;

public class BitcoinWalletServiceTests
{
    private const string PrimaryMnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string ForeignMnemonic = "legal winner thank year wave sausage worth useful legal winner thank yellow";

    private static BitcoinWalletService CreateService()
    {
        var options = Options.Create(new BitcoinWalletOptions
        {
            Network = Network.TestNet.ToString(),
            Purpose = 84,
            CoinType = 1
        });

        return new BitcoinWalletService(options);
    }

    [Fact]
    public void BuildTransaction_WithOwnedCoins_Succeeds()
    {
        var service = CreateService();
        var account = service.DeriveAccount(PrimaryMnemonic, passphrase: null, accountIndex: 0);

        var external0 = account.ExternalAddresses.GetAddress(0);
        var external1 = account.ExternalAddresses.GetAddress(1);
        var destination = account.ExternalAddresses.GetAddress(5);

        var coins = new[]
        {
            new WalletCoin(
                new Coin(
                    new OutPoint(uint256.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), 0),
                    new TxOut(Money.Coins(0.015m), external0)),
                new KeyPath("0/0")),
            new WalletCoin(
                new Coin(
                    new OutPoint(uint256.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), 1),
                    new TxOut(Money.Coins(0.005m), external1)),
                new KeyPath("0/1"))
        };

        var feeRate = new FeeRate(Money.Satoshis(1_500));
        var psbt = service.BuildTransaction(account, coins, destination, Money.Coins(0.01m), feeRate, changeIndex: 0);

    Assert.NotNull(psbt);
    Assert.NotEmpty(psbt.Inputs);
    Assert.Contains(psbt.Inputs, input => input.PrevOut == coins[0].Coin.Outpoint);
    }

    [Fact]
    public void BuildTransaction_WithForeignCoin_Throws()
    {
        var service = CreateService();
        var account = service.DeriveAccount(PrimaryMnemonic, passphrase: null, accountIndex: 0);
        var foreignAccount = service.DeriveAccount(ForeignMnemonic, passphrase: null, accountIndex: 0);

        var coins = new[]
        {
            new WalletCoin(
                new Coin(
                    new OutPoint(uint256.Parse("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"), 0),
                    new TxOut(Money.Coins(0.015m), account.ExternalAddresses.GetAddress(0))),
                new KeyPath("0/0")),
            new WalletCoin(
                new Coin(
                    new OutPoint(uint256.Parse("dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"), 1),
                    new TxOut(Money.Coins(0.005m), foreignAccount.ExternalAddresses.GetAddress(0))),
                new KeyPath("0/1"))
        };

        var destination = account.ExternalAddresses.GetAddress(8);
        var feeRate = new FeeRate(Money.Satoshis(1_500));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            service.BuildTransaction(account, coins, destination, Money.Coins(0.01m), feeRate, changeIndex: 0));

        Assert.Contains("non appartiene", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
