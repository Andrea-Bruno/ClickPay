using ClickPay.Shared.Preferences;
using ClickPay.Shared.Services;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Blockchain;
using ClickPay.Wallet.Core.Blockchain.Bitcoin;
using ClickPay.Wallet.Core.Blockchain.Solana;
using ClickPay.Wallet.Core.Blockchain.Ethereum;
using ClickPay.Wallet.Core.DependencyInjection;
using ClickPay.Wallet.Core.Wallet;
using ClickPay.Web.Components;
using ClickPay.Web.Services;
using Microsoft.Extensions.Options;
using NBitcoin;
using LocalizationService = ClickPay.Shared.Services.LocalizationService;
using System.IO;
using System;

var builder = WebApplication.CreateBuilder(args);

// Inizializza l'istanza globale dell'API client
API.ApiGlobals.Initialize(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the ClickPay.Shared project

builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<UserPreferenceService>();
builder.Services.AddScoped<FiatPreferenceStore>();
builder.Services.Configure<MarketDataCacheOptions>(options =>
{
    options.CacheDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "cache");
    options.EntryLifetime = TimeSpan.FromMinutes(5);
});
builder.Services.Configure<BitcoinWalletOptions>(options =>
{
#if DEBUG
    options.Network = NBitcoin.Network.TestNet.ToString();
    options.CoinType = 1;
#else
    options.Network = NBitcoin.Network.Main.ToString();
    options.CoinType = 0;
#endif
});
builder.Services.Configure<SolanaWalletOptions>(options =>
{
#if DEBUG
    options.RpcEndpoint = builder.Configuration["Solana:Devnet:RpcEndpoint"] ?? SolanaWalletOptions.DevnetRpcEndpoint;
    options.Commitment = builder.Configuration["Solana:Devnet:Commitment"] ?? SolanaWalletOptions.DefaultCommitment;
    options.EurcMintAddress = builder.Configuration["Solana:Devnet:EurcMintAddress"] ?? SolanaWalletOptions.DevnetEurcMint;
#else
    options.RpcEndpoint = builder.Configuration["Solana:Mainnet:RpcEndpoint"] ?? SolanaWalletOptions.MainnetRpcEndpoint;
    options.Commitment = builder.Configuration["Solana:Mainnet:Commitment"] ?? SolanaWalletOptions.ProductionCommitment;
    options.EurcMintAddress = builder.Configuration["Solana:Mainnet:EurcMintAddress"] ?? SolanaWalletOptions.MainnetEurcMint;
#endif

    if (int.TryParse(builder.Configuration["Solana:TransactionHistoryLimit"], out var historyLimit) && historyLimit > 0)
    {
        options.TransactionHistoryLimit = historyLimit;
    }
});
builder.Services.Configure<EthereumWalletOptions>(options =>
{
#if DEBUG
    options.RpcEndpoint = builder.Configuration["Ethereum:Testnet:RpcEndpoint"] ?? "https://ethereum-sepolia.blockpi.network/v1/rpc/public";
    options.ChainId = builder.Configuration.GetValue<long?>("Ethereum:Testnet:ChainId") ?? 11155111L;
#else
    options.RpcEndpoint = builder.Configuration["Ethereum:Mainnet:RpcEndpoint"] ?? "https://ethereum.publicnode.com";
    options.ChainId = builder.Configuration.GetValue<long?>("Ethereum:Mainnet:ChainId") ?? 1L;
#endif

    if (int.TryParse(builder.Configuration["Ethereum:TransactionHistoryLimit"], out var historyLimit) && historyLimit > 0)
    {
        options.TransactionHistoryLimit = historyLimit;
    }

    if (int.TryParse(builder.Configuration["Ethereum:FallbackGasPriceGwei"], out var fallbackGwei) && fallbackGwei > 0)
    {
        options.FallbackGasPriceGwei = fallbackGwei;
    }
});
builder.Services.AddScoped<HttpClient>(sp => new HttpClient());
builder.Services.AddScoped<IExchangeRateService, CoinGeckoExchangeRateService>();
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<ILocalSecureStore, DataProtectionLocalSecureStore>();
}
else
{
    builder.Services.AddSingleton<ILocalSecureStore, UnsupportedLocalSecureStore>();
}
builder.Services.AddScoped<IBiometricLockService, WebBiometricLockService>();
builder.Services.AddWalletCore();
builder.Services.AddScoped<IQrScannerService, WebQrScannerService>();
builder.Services.AddSingleton<PaymentRequestParser>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<RootDocument>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ClickPay.Shared._Imports).Assembly);

app.Run();
