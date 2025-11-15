using System;

namespace ClickPay.Wallet.Core.Utility
{
    public sealed record CachedExchangeRate(decimal Value, DateTime TimestampUtc);
}
