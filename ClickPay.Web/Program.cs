using ClickPay.Shared.Services;
using ClickPay.Web.Components;
using ClickPay.Web.Services;
using Microsoft.Extensions.Options;
using NBitcoin;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the ClickPay.Shared project

builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddSingleton<LocalizationService>();
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
builder.Services.AddSingleton<BitcoinWalletService>();
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
builder.Services.AddScoped<SolanaWalletService>();
builder.Services.AddScoped<HttpClient>(sp => new HttpClient());
builder.Services.AddScoped<Microsoft.AspNetCore.Components.ProtectedBrowserStorage.ProtectedLocalStorage>();
builder.Services.AddScoped<WalletKeyService>();
builder.Services.AddScoped<BitcoinNetworkClient>(sp =>
{
    var http = sp.GetRequiredService<HttpClient>();
    var options = sp.GetRequiredService<IOptions<BitcoinWalletOptions>>();
    var networkName = options.Value?.Network ?? NBitcoin.Network.Main.ToString();
    var network = NBitcoin.Network.GetNetwork(networkName) ?? NBitcoin.Network.Main;
    return new BitcoinNetworkClient(http, network);
});
builder.Services.AddScoped<MultiChainWalletService>();
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(ClickPay.Shared._Imports).Assembly);

app.Run();
