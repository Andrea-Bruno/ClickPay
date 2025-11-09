using System;
using System.Collections.Generic;
using System.Linq;
using ClickPay.Wallet.Core.CryptoAssets;

namespace ClickPay.Wallet.Core.Blockchain
{
    public sealed class WalletProviderRegistry
    {
        private readonly IReadOnlyDictionary<BlockchainNetwork, IWalletProvider> _providersByNetwork;

        public WalletProviderRegistry(IEnumerable<IWalletProvider> providers)
        {
            if (providers is null)
            {
                throw new ArgumentNullException(nameof(providers));
            }

            var map = new Dictionary<BlockchainNetwork, IWalletProvider>();
            foreach (var provider in providers)
            {
                if (provider is null)
                {
                    continue;
                }

                if (map.TryGetValue(provider.Network, out var existing))
                {
                    throw new InvalidOperationException($"Multiple wallet providers registered for network {provider.Network} ({existing.GetType().Name} and {provider.GetType().Name}).");
                }

                map[provider.Network] = provider;
            }

            _providersByNetwork = map;
        }

        internal IReadOnlyDictionary<BlockchainNetwork, IWalletProvider> Providers => _providersByNetwork;

    public IReadOnlyCollection<BlockchainNetwork> GetRegisteredNetworks()
        {
            return _providersByNetwork.Keys.ToArray();
        }

        internal IWalletProvider Resolve(CryptoAsset asset)
        {
            if (asset is null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (!_providersByNetwork.TryGetValue(asset.Network, out var provider))
            {
                throw new WalletProviderUnavailableException(asset.Network);
            }

            if (!provider.SupportsAsset(asset))
            {
                throw new WalletAssetNotSupportedException(asset.Code, asset.Network);
            }

            return provider;
        }
    }

    public sealed class WalletProviderUnavailableException : Exception
    {
        public WalletProviderUnavailableException(BlockchainNetwork network)
            : base($"No wallet provider registered for network {network}.")
        {
            Network = network;
        }

        public BlockchainNetwork Network { get; }
    }

    public sealed class WalletAssetNotSupportedException : Exception
    {
        public WalletAssetNotSupportedException(string assetCode, BlockchainNetwork network)
            : base($"Asset {assetCode} is not supported by provider for network {network}.")
        {
            AssetCode = assetCode;
            Network = network;
        }

        public string AssetCode { get; }

        public BlockchainNetwork Network { get; }
    }
}
