using System;
using System.Net.Http;
using ClickPay.Wallet.Core.Blockchain;
using Bitcoin = ClickPay.Wallet.Core.Blockchain.Bitcoin;
using EthereumChain = ClickPay.Wallet.Core.Blockchain.Ethereum;
using SolanaChain = ClickPay.Wallet.Core.Blockchain.Solana;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Utility;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace ClickPay.Wallet.Core.DependencyInjection
{
    public static class WalletServiceCollectionExtensions
    {
        public static IServiceCollection AddWalletCore(this IServiceCollection services)
        {
            if (services is null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddSingleton<WalletMnemonicService>();
            services.AddSingleton<WalletErrorLocalizer>();

            services.AddScoped(provider =>
            {
                var options = provider.GetRequiredService<IOptions<Bitcoin.BitcoinWalletOptions>>();
                return new Bitcoin.BitcoinWalletService(options);
            });

            services.AddScoped(provider =>
            {
                var httpClient = provider.GetRequiredService<HttpClient>();
                var options = provider.GetRequiredService<IOptions<Bitcoin.BitcoinWalletOptions>>();
                var networkName = options.Value?.Network ?? Network.Main.ToString();
                var network = Network.GetNetwork(networkName) ?? Network.Main;
                return new Bitcoin.BitcoinNetworkClient(httpClient, network);
            });

            services.AddScoped(provider =>
            {
                var options = provider.GetRequiredService<IOptions<SolanaChain.SolanaWalletOptions>>();
                var httpClient = provider.GetRequiredService<HttpClient>();
                return new SolanaChain.SolanaWalletService(options, httpClient);
            });

            services.AddScoped(provider =>
            {
                var options = provider.GetRequiredService<IOptions<EthereumChain.EthereumWalletOptions>>();
                return new EthereumChain.EthereumWalletService(options);
            });

            services.AddScoped<IWalletProvider, Bitcoin.BitcoinWalletProvider>();
            services.AddScoped<IWalletProvider, SolanaChain.SolanaWalletProvider>();
            services.AddScoped<IWalletProvider, EthereumChain.EthereumWalletProvider>();

            services.AddScoped<WalletProviderRegistry>();

            services.AddSingleton(BlockchainProviderRegistry.Default);

            return services;
        }
    }
}
