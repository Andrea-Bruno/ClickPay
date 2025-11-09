using System;
using System.Globalization;
using System.Net.Http;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Blockchain.Solana;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Options;
using Solnet.Programs;
using Solnet.Wallet;

var useDevnet = false;
string? overrideRpc = null;
var skipSend = false;

for (var i = 0; i < args.Length; i++)
{
    var argument = args[i];
    switch (argument)
    {
        case "--devnet":
            useDevnet = true;
            break;
        case "--rpc":
            if (i + 1 >= args.Length)
            {
                Console.WriteLine("Missing value for --rpc.");
                PrintUsage();
                return;
            }

            overrideRpc = args[++i];
            break;
        case "--skip-send":
            skipSend = true;
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return;
        default:
            Console.WriteLine($"Unrecognized argument: {argument}");
            PrintUsage();
            return;
    }
}

CryptoAsset eurcAsset;
try
{
    eurcAsset = WalletAssetHelper.GetDefinition("EURC");
}
catch (Exception ex)
{
    Console.WriteLine("Unable to load EURC asset definition:");
    Console.WriteLine(ex.Message);
    return;
}

var environmentLabel = useDevnet ? "devnet" : "mainnet";
Console.WriteLine($"Loaded asset: {eurcAsset.Name} ({eurcAsset.Code}) on {eurcAsset.Network} (simulation target: {environmentLabel})");
Console.WriteLine($"Visibility locked: {eurcAsset.VisibilityLocked}");
Console.WriteLine($"Resolved chain key: {WalletChains.ResolveChainKey(eurcAsset.Network.ToString())}");

var fallbackDecimals = Math.Max(1, (int)SolanaWalletOptions.Default.EurcDecimals);
var decimals = eurcAsset.Decimals > 0 ? eurcAsset.Decimals : fallbackDecimals;

var mintAddress = useDevnet
    ? SolanaWalletOptions.DevnetEurcMint
    : eurcAsset.ContractAddress ?? string.Empty;

if (string.IsNullOrWhiteSpace(mintAddress))
{
    Console.WriteLine("EURC asset definition does not include a mint address for the selected environment.");
    return;
}

Console.WriteLine($"Mint address ({environmentLabel}): {mintAddress}");

var effectiveAsset = CloneAsset(eurcAsset, mintAddress, decimals);

const string mnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

var rpcEndpoint = overrideRpc ?? (useDevnet ? SolanaWalletOptions.DevnetRpcEndpoint : SolanaWalletOptions.MainnetRpcEndpoint);
var commitment = useDevnet ? SolanaWalletOptions.DefaultCommitment : SolanaWalletOptions.ProductionCommitment;

if (!TryValidateRpcEndpoint(rpcEndpoint, out var rpcError))
{
    Console.WriteLine(rpcError);
    return;
}

var solanaOptions = Options.Create(new SolanaWalletOptions
{
    RpcEndpoint = rpcEndpoint,
    Commitment = commitment,
    EurcMintAddress = mintAddress,
    EurcDecimals = (byte)Math.Clamp(decimals, 0, byte.MaxValue)
});

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
var solanaService = new SolanaWalletService(solanaOptions, httpClient);

SolanaWalletAccount account;
try
{
    account = solanaService.DeriveAccount(mnemonic, passphrase: null, accountIndex: 0);
}
catch (Exception ex)
{
    Console.WriteLine("Failed to derive Solana account:");
    Console.WriteLine(ex.ToString());
    return;
}

var walletAddress = account.Account.PublicKey.Key;
Console.WriteLine($"Derived public key: {walletAddress}");
Console.WriteLine($"Derivation path: {account.DerivationPath}");

var ownerKey = account.Account.PublicKey;

try
{
    var mintKey = new PublicKey(effectiveAsset.ContractAddress ?? string.Empty);
    var associatedAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(ownerKey, mintKey);
    Console.WriteLine($"Associated token account: {associatedAccount}");
}
catch (Exception ex)
{
    Console.WriteLine("Failed to derive associated token account:");
    Console.WriteLine(ex.Message);
}

try
{
    var balanceSol = await solanaService.GetNativeSolBalanceAsync(account);
    Console.WriteLine($"Native SOL balance: {balanceSol.ToString("F9", CultureInfo.InvariantCulture)} SOL");
}
catch (Exception ex)
{
    Console.WriteLine("Unable to query native SOL balance (likely RPC issue):");
    Console.WriteLine(ex.Message);
}

var missingTokenAccount = false;

try
{
    var eurcBalance = await solanaService.GetTokenBalanceAsync(account, effectiveAsset);
    Console.WriteLine($"EURC token balance: {eurcBalance.Amount.ToString("F6", CultureInfo.InvariantCulture)} EURC");
    Console.WriteLine($"Token account decimals: {eurcBalance.Decimals}");
    Console.WriteLine($"Token account owner: {eurcBalance.Owner}");

    if (string.IsNullOrWhiteSpace(eurcBalance.Owner))
    {
        Console.WriteLine("No associated EURC token account detected. Fund SOL and mint EURC before retrying.");
        missingTokenAccount = true;
        skipSend = true;
    }
}
catch (Exception ex)
{
    Console.WriteLine("Unable to query EURC token balance:");
    Console.WriteLine(ex.Message);
    missingTokenAccount = true;
    skipSend = true;
}

if (skipSend)
{
    var reason = missingTokenAccount
        ? "Skipping send simulation because the token account is unavailable."
        : "Skipping send simulation per configuration.";
    Console.WriteLine(reason);
}
else
{
    try
    {
        Console.WriteLine("Attempting to send 0.10 EURC to self (expected to fail without funds)...");
    var sendResult = await solanaService.SendAsync(account, effectiveAsset, walletAddress, 0.10m);
        Console.WriteLine($"Transaction broadcast succeeded unexpectedly! TxId: {sendResult.TransactionId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Send simulation failed as expected:");
        Console.WriteLine(ex.Message);
    }
}

Console.WriteLine("=== Simulation complete ===");

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run --project Simulations/SolanaEurcSimulation -- [--devnet] [--rpc <url>] [--skip-send]");
    Console.WriteLine("  --devnet     Use Solana devnet defaults instead of mainnet.");
    Console.WriteLine("  --rpc <url>  Override the RPC endpoint.");
    Console.WriteLine("  --skip-send  Skip the transfer attempt.");
}

static bool TryValidateRpcEndpoint(string endpoint, out string error)
{
    error = string.Empty;

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        error = "The RPC endpoint cannot be empty.";
        return false;
    }

    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
    {
        error = "The provided RPC endpoint is not a valid URI.";
        return false;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        error = "The RPC endpoint must use HTTP or HTTPS.";
        return false;
    }

    return true;
}

static CryptoAsset CloneAsset(CryptoAsset source, string contractAddress, int decimals)
{
    return new CryptoAsset
    {
        Name = source.Name,
        Code = source.Code,
        Network = source.Network,
        ContractAddress = contractAddress,
        Decimals = decimals,
        ChainId = source.ChainId,
        LogoUrl = source.LogoUrl,
        CoingeckoId = source.CoingeckoId,
        ExplorerUrl = source.ExplorerUrl,
        MenuCustomLabel = source.MenuCustomLabel,
        RouteSegment = source.RouteSegment,
        Aliases = source.Aliases,
        SortOrder = source.SortOrder,
        Symbol = source.Symbol,
        Hidden = source.Hidden,
        FeeAssetCodes = source.FeeAssetCodes,
        NetworkFeeMinimumBalance = source.NetworkFeeMinimumBalance,
        VisibilityLocked = source.VisibilityLocked
    };
}
