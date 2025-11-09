using ClickPay.Services;
using ClickPay.Shared.Preferences;
using ClickPay.Shared.Services;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Blockchain;
using ClickPay.Wallet.Core.Blockchain.Bitcoin;
using ClickPay.Wallet.Core.Blockchain.Solana;
using ClickPay.Wallet.Core.Blockchain.Ethereum;
using ClickPay.Wallet.Core.DependencyInjection;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Storage;
using NBitcoin;
using System.Net.Http;
using System;
using System.IO;
using ZXing.Net.Maui.Controls;

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

            // Servizi condivisi
            builder.Services.AddSingleton<IFormFactor, FormFactor>();
            builder.Services.AddSingleton<ClickPay.Shared.Services.LocalizationService>();
            builder.Services.AddScoped<FiatPreferenceStore>();
            builder.Services.Configure<MarketDataCacheOptions>(options =>
            {
                options.CacheDirectory = Path.Combine(FileSystem.AppDataDirectory, "cache");
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
            builder.Services.Configure<EthereumWalletOptions>(options =>
            {
#if DEBUG
                options.RpcEndpoint = "https://ethereum-sepolia.blockpi.network/v1/rpc/public";
                options.ChainId = 11155111;
#else
                options.RpcEndpoint = "https://ethereum.publicnode.com";
                options.ChainId = 1;
#endif
            });
            builder.Services.AddSingleton<HttpClient>();
            builder.Services.AddSingleton<IExchangeRateService>(sp =>
            {
                var http = sp.GetRequiredService<HttpClient>();
                var opts = sp.GetRequiredService<IOptions<MarketDataCacheOptions>>();
                var logger = sp.GetService<ILogger<CoinGeckoExchangeRateService>>();
                return new CoinGeckoExchangeRateService(http, opts, logger);
            });
            builder.Services.AddScoped<ILocalSecureStore, SecureStorageLocalSecureStore>();
#if ANDROID
            builder.Services.AddScoped<IBiometricLockService, AndroidBiometricLockService>();
#elif IOS
            builder.Services.AddScoped<IBiometricLockService, IosBiometricLockService>();
#else
            builder.Services.AddScoped<IBiometricLockService, NoOpBiometricLockService>();
#endif
#if ANDROID || IOS || MACCATALYST
            builder.Services.AddSingleton<IQrScannerService, MauiQrScannerService>();
#else
            builder.Services.AddSingleton<IQrScannerService, NoOpQrScannerService>();
#endif
            builder.Services.AddWalletCore();
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
