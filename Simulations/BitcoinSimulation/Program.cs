using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Blockchain.Bitcoin;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Options;
using NBitcoin;

Console.WriteLine("=== Bitcoin Simulation ===");

var simulateInsufficient = args.Any(a => string.Equals(a, "--insufficient", StringComparison.OrdinalIgnoreCase));
var simulateForeignUtxo = args.Any(a => string.Equals(a, "--foreign-utxo", StringComparison.OrdinalIgnoreCase));

var btcAsset = WalletAssetHelper.GetDefinition("BTC");
Console.WriteLine($"Loaded asset: {btcAsset.Name} ({btcAsset.Code}) on {btcAsset.Network}");
Console.WriteLine($"Visibility locked: {btcAsset.VisibilityLocked}");
Console.WriteLine($"Resolved chain key: {WalletChains.ResolveChainKey(btcAsset.Network.ToString())}");

const string testMnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
var bitcoinOptions = Options.Create(new BitcoinWalletOptions
{
    Network = Network.TestNet.ToString(),
    Purpose = 84,
    CoinType = 1
});

var walletService = new BitcoinWalletService(bitcoinOptions);
Console.WriteLine($"Using Bitcoin network: {bitcoinOptions.Value.Network}");

WalletAccount account;
try
{
    account = walletService.DeriveAccount(testMnemonic, passphrase: null, accountIndex: 0);
}
catch (Exception ex)
{
    Console.WriteLine("Failed to derive account:");
    Console.WriteLine(ex.ToString());
    return;
}

Console.WriteLine($"Account master fingerprint: {account.MasterFingerprint}");
Console.WriteLine($"Account derivation path: {account.AccountKeyPath}");
Console.WriteLine($"Account xpub: {account.AccountXpub}");
Console.WriteLine($"External[0]: {account.ExternalAddresses.GetAddress(0)}");
Console.WriteLine($"Internal[0]: {account.InternalAddresses.GetAddress(0)}");

var external0 = account.ExternalAddresses.GetAddress(0);
var external1 = account.ExternalAddresses.GetAddress(1);

BitcoinAddress secondAddress;
KeyPath secondKeyPath;

if (simulateForeignUtxo)
{
    Console.WriteLine("Scenario: injecting foreign UTXO to test ownership validation");
    const string foreignMnemonic = "legal winner thank year wave sausage worth useful legal winner thank yellow";
    var foreignAccount = walletService.DeriveAccount(foreignMnemonic, passphrase: null, accountIndex: 0);
    secondAddress = foreignAccount.ExternalAddresses.GetAddress(0);
    secondKeyPath = new KeyPath("0/1");
}
else
{
    secondAddress = external1;
    secondKeyPath = new KeyPath("0/1");
}

var walletCoins = new List<WalletCoin>
{
    new(
        new Coin(
            new OutPoint(uint256.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), 0),
            new TxOut(Money.Coins(0.015m), external0)),
        new KeyPath("0/0")),
    new(
        new Coin(
            new OutPoint(uint256.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"), 1),
            new TxOut(Money.Coins(0.005m), secondAddress)),
        secondKeyPath)
};

Console.WriteLine("Synthetic UTXOs loaded:");
foreach (var coin in walletCoins)
{
    var amountBtc = coin.Coin.Amount.ToDecimal(MoneyUnit.BTC).ToString("F8", CultureInfo.InvariantCulture);
    var coinAddress = coin.Coin.ScriptPubKey.GetDestinationAddress(account.Network)?.ToString() ?? "(non-standard script)";
    Console.WriteLine($"  - {amountBtc} BTC from {coin.Coin.Outpoint} -> {coinAddress} (path m/{account.AccountKeyPath}/{coin.KeyPath})");
}

var destination = account.ExternalAddresses.GetAddress(5);
decimal targetAmountBtc = 0.013m;
if (simulateInsufficient)
{
    targetAmountBtc = 0.025m;
}
else if (simulateForeignUtxo)
{
    targetAmountBtc = 0.0195m;
}

var amountToSend = Money.Coins(targetAmountBtc);
var feeRate = new FeeRate(Money.Satoshis(2_500));
if (simulateInsufficient)
{
    Console.WriteLine("Scenario: forcing insufficient funds error");
}
else if (simulateForeignUtxo)
{
    Console.WriteLine("Scenario: requires spending every recorded UTXO (exposes ownership bugs)");
}

Console.WriteLine($"Target send amount: {amountToSend.ToDecimal(MoneyUnit.BTC):F8} BTC");
Console.WriteLine($"Send to: {destination}");
Console.WriteLine($"Using fee rate: {feeRate.FeePerK.Satoshi / 1000m:F2} sat/vB");

try
{
    var psbt = walletService.BuildTransaction(account, walletCoins, destination, amountToSend, feeRate, changeIndex: 0);
    Console.WriteLine("Constructed PSBT:");
    Console.WriteLine(psbt.ToBase64());

    var signed = walletService.SignTransaction(psbt, account);
    var finalPsbt = signed.Clone();
    var finalTx = walletService.FinalizeTransaction(finalPsbt);

    Console.WriteLine($"Signed transaction size: {finalTx.GetVirtualSize()} vB");
    Console.WriteLine($"Transaction ID (pre-broadcast): {finalTx.GetHash()}");

    Console.WriteLine("Outputs:");
    foreach (var output in finalTx.Outputs)
    {
        var address = output.ScriptPubKey.GetDestinationAddress(account.Network);
        var target = address?.ToString() ?? output.ScriptPubKey.ToString();
        var btcAmount = output.Value.ToDecimal(MoneyUnit.BTC).ToString("F8", CultureInfo.InvariantCulture);
        Console.WriteLine($"  -> {btcAmount} BTC to {target}");
    }

    var hex = finalTx.ToHex();
    var preview = hex.Length > 120 ? hex[..120] + "..." : hex;
    Console.WriteLine($"Transaction hex preview: {preview}");
}
catch (Exception ex)
{
    Console.WriteLine("Failed to build or sign the transaction:");
    Console.WriteLine(ex.ToString());
}

try
{
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var networkClient = new BitcoinNetworkClient(httpClient, account.Network);
    var recommended = await networkClient.GetRecommendedFeeRateAsync();
    Console.WriteLine($"Remote fee estimate (6 blocks target): {recommended.FeePerK.Satoshi / 1000m:F2} sat/vB");
}
catch (Exception ex)
{
    Console.WriteLine("Fee estimate lookup skipped (likely offline environment):");
    Console.WriteLine(ex.Message);
}

Console.WriteLine("=== Simulation complete ===");
