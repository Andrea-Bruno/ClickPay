using System;

namespace ClickPay.Wallet.Core.Services;

internal static class ServiceCacheDefaults
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
}
