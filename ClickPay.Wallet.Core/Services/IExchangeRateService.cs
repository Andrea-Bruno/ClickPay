using System;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;

namespace ClickPay.Wallet.Core.Services
{
    public interface IExchangeRateService
    {
        Task<ClickPay.Wallet.Core.Utility.CachedExchangeRate?> GetRateAsync(CryptoAsset asset, string fiatCode, Action? refresh = null, CancellationToken cancellationToken = default);
    }
}
