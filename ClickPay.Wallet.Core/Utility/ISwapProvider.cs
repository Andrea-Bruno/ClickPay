using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;

namespace ClickPay.Wallet.Core.Utility
{
    public interface ISwapProvider
    {
        string Name { get; }
        bool Supports(CryptoAsset from, CryptoAsset to);
        Task<SwapUtility.SwapQuote?> GetQuoteAsync(SwapUtility.SwapRequest request, CancellationToken cancellationToken = default);
        Task<SwapUtility.ValidationResult> ValidateAsync(SwapUtility.SwapRequest request, CancellationToken cancellationToken = default);
        Task<SwapUtility.SwapResult> ExecuteSwapAsync(SwapUtility.SwapRequest request, string routeData, CancellationToken cancellationToken = default);
    }
}
