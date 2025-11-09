using System;

namespace ClickPay.Wallet.Core.Services
{
    public sealed class MarketDataCacheOptions
    {
        public string? CacheDirectory { get; set; }

        public TimeSpan EntryLifetime { get; set; } = TimeSpan.FromMinutes(5);
    }
}
