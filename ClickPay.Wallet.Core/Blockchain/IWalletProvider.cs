using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Wallet;

namespace ClickPay.Wallet.Core.Blockchain
{
    public interface IWalletProvider
    {
        BlockchainNetwork Network { get; }

        bool SupportsAsset(CryptoAsset asset);

        Task<WalletOverview> GetOverviewAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken);

        Task<WalletReceiveInfo> GetReceiveInfoAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken);

        Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(CryptoAsset asset, WalletVault vault, CancellationToken cancellationToken);

        Task<WalletSendResult> SendAsync(CryptoAsset asset, WalletVault vault, WalletSendRequest request, CancellationToken cancellationToken);
    }

    public sealed record WalletSendRequest(string DestinationAddress, decimal Amount);
}
