using System;
using System.Collections.Generic;

namespace ClickPay.Wallet.Core.Wallet;

public sealed record WalletOverview(
    string AssetCode,
    string Symbol,
    decimal Balance,
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
    string AssetCode,
    string Symbol,
    string Address,
    IReadOnlyDictionary<string, string>? Metadata = null
);

public sealed record WalletSendResult(
    string AssetCode,
    string Symbol,
    string TransactionId,
    DateTimeOffset SubmittedAtUtc
);
