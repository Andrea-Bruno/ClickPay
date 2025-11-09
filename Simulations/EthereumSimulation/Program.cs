using System.Globalization;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Blockchain.Ethereum;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Options;
using HdWallet = Nethereum.HdWallet.Wallet;

Console.WriteLine("=== Ethereum Simulation ===");

// Ensure the asset registry is loaded and ETH metadata is available.
var ethAsset = WalletAssetHelper.GetDefinition("ETH");
Console.WriteLine($"Loaded asset: {ethAsset.Name} ({ethAsset.Code}) on {ethAsset.Network}");
Console.WriteLine($"Visibility locked: {ethAsset.VisibilityLocked}");
Console.WriteLine($"Resolved chain key: {WalletChains.ResolveChainKey(ethAsset.Network.ToString())}");

// Derive an account from a deterministic mnemonic that is safe for testing (never use this mnemonic in production).
const string testMnemonic = "test test test test test test test test test test test junk";
var options = Options.Create(new EthereumWalletOptions
{
    RpcEndpoint = "https://ethereum-sepolia.blockpi.network/v1/rpc/public",
    ChainId = 11155111L,
    FallbackGasPriceGwei = 30
});

var walletService = new EthereumWalletService(options);
var debugWallet = new HdWallet(testMnemonic, string.Empty);
var debugAccount = debugWallet.GetAccount(0);
Console.WriteLine($"Debug derived address (default path): {debugAccount.Address}");

var walletMethods = typeof(HdWallet).GetMethods()
    .Where(m => m.Name == "GetAccount")
    .Select(m => m.ToString())
    .Distinct()
    .ToArray();

Console.WriteLine("Available GetAccount overloads:");
foreach (var signature in walletMethods)
{
    Console.WriteLine("  " + signature);
}

var constructors = typeof(HdWallet).GetConstructors()
    .Select(c => c.ToString())
    .ToArray();

Console.WriteLine("Wallet constructors:");
foreach (var ctor in constructors)
{
    Console.WriteLine("  " + ctor);
}

try
{
    var accountFromPath = debugWallet.GetAccount("m/44'/60'/0'/0", 0);
    Console.WriteLine($"Account via explicit path: {accountFromPath.Address}");
}
catch (Exception ex)
{
    Console.WriteLine("Path-based derivation threw:");
    Console.WriteLine(ex.Message);
}

try
{
    var customBaseWallet = new HdWallet(testMnemonic, string.Empty, "m/44'/60'/1'/0", null);
    var secondAccount = customBaseWallet.GetAccount(0);
    Console.WriteLine($"Derived account with custom base path: {secondAccount.Address}");
}
catch (Exception ex)
{
    Console.WriteLine("Custom base path derivation threw:");
    Console.WriteLine(ex.Message);
}

EthereumWalletAccount account;

try
{
    account = walletService.DeriveAccount(testMnemonic, passphrase: null, accountIndex: 0, addressIndex: 0);
    Console.WriteLine($"Derived test address: {account.Address}");
}
catch (Exception ex)
{
    Console.WriteLine("Failed to derive test account:");
    Console.WriteLine(ex.ToString());
    return;
}

try
{
    // Query native balance (expected to be zero on a fresh mnemonic, but call verifies RPC connectivity).
    var balance = await walletService.GetNativeBalanceAsync(account);
    Console.WriteLine($"Native balance: {balance.ToString("F18", CultureInfo.InvariantCulture)} ETH");

    // Attempt to send a tiny self-transaction to verify transaction construction. This should fail with
    // an insufficient funds error, confirming that RPC calls and validation logic are exercised.
    try
    {
        Console.WriteLine("Attempting to broadcast 0.000001 ETH to self (expected to fail without funds)...");
        var txId = await walletService.SendNativeAsync(account, account.Address, 0.000001m);
        Console.WriteLine($"Transaction broadcast unexpectedly succeeded! TxId: {txId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Send simulation failed as expected. Message:");
        Console.WriteLine(ex.Message);
    }
}
catch (Exception ex)
{
    Console.WriteLine("Simulation aborted due to RPC or configuration error:");
    Console.WriteLine(ex.ToString());
}

Console.WriteLine("=== Simulation complete ===");
