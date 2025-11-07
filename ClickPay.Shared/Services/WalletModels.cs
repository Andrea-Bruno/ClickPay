using System;
using System.Collections.Generic;

namespace ClickPay.Shared.Services
{
    public enum WalletAsset
    {
        Bitcoin,
        Eurc,
        Xaut,
        Sol
    }

    public sealed record WalletOverview(
        WalletAsset Asset,
        string Symbol,
        decimal Balance,
        string PrimaryAddress,
        IReadOnlyList<WalletTransaction> Transactions,
        decimal? FiatEstimate,
        string? NativeBalanceDescriptor = null
    );

    public sealed record WalletTransaction(
        string TransactionId,
        DateTimeOffset Timestamp,
        decimal Amount,
        bool IsIncoming,
        string? Counterparty,
        string? Memo,
        decimal? Fee = null
    )
    {
        public bool IsOutgoing => !IsIncoming;
    }

    public sealed record WalletReceiveInfo(
        WalletAsset Asset,
        string Symbol,
        string Address,
        IReadOnlyDictionary<string, string>? Metadata = null
    );

    public sealed record WalletSendResult(
        WalletAsset Asset,
        string Symbol,
        string TransactionId,
        DateTimeOffset SubmittedAtUtc
    );
}
