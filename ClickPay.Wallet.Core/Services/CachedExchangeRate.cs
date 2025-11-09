using System;

namespace ClickPay.Wallet.Core.Services
{
    public sealed record CachedExchangeRate(decimal Value, DateTime TimestampUtc);
}
