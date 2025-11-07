using ClickPay.Services;
using ClickPay.Shared.Services;
using Microsoft.AspNetCore.Components.ProtectedBrowserStorage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using System.Net.Http;
using ZXing.Net.Maui.Controls;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

namespace ClickPay
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            // Data Protection per ProtectedLocalStorage
            builder.Services
                .AddDataProtection()
                .SetApplicationName("ClickPay")
                .PersistKeysToFileSystem(new DirectoryInfo(FileSystem.AppDataDirectory));

            // Servizi condivisi
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
                options.RpcEndpoint = SolanaWalletOptions.DevnetRpcEndpoint;
                options.Commitment = SolanaWalletOptions.DefaultCommitment;
                options.EurcMintAddress = SolanaWalletOptions.DevnetEurcMint;
#else
                options.RpcEndpoint = SolanaWalletOptions.MainnetRpcEndpoint;
                options.Commitment = SolanaWalletOptions.ProductionCommitment;
                options.EurcMintAddress = SolanaWalletOptions.MainnetEurcMint;
#endif
                if (int.TryParse(builder.Configuration["Solana:TransactionHistoryLimit"], out var historyLimit) && historyLimit > 0)
                {
                    options.TransactionHistoryLimit = historyLimit;
                }
            });
            builder.Services.AddSingleton<SolanaWalletService>();
            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddScoped<ProtectedLocalStorage>();
            builder.Services.AddScoped<WalletKeyService>();
            builder.Services.AddScoped<ILocalSecureStore, LocalSecureStore>();
#if ANDROID || IOS || MACCATALYST
            builder.Services.AddSingleton<IQrScannerService, MauiQrScannerService>();
#else
            builder.Services.AddSingleton<IQrScannerService, NoOpQrScannerService>();
#endif
            builder.Services.AddScoped<BitcoinNetworkClient>(sp =>
            {
                var http = sp.GetRequiredService<HttpClient>();
                var options = sp.GetRequiredService<IOptions<BitcoinWalletOptions>>();
                var networkName = options.Value?.Network ?? NBitcoin.Network.Main.ToString();
                var network = NBitcoin.Network.GetNetwork(networkName) ?? NBitcoin.Network.Main;
                return new BitcoinNetworkClient(http, network);
            });
            builder.Services.AddScoped<MultiChainWalletService>();
            builder.Services.AddSingleton<PaymentRequestParser>();

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
