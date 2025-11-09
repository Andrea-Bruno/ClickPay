using System.Threading;
using System.Threading.Tasks;

namespace ClickPay.Wallet.Core.Services;

public interface IQrScannerService
{
    Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken = default);
}

public sealed record QrScanRequest(
    string AssetCode,
    string? ExpectedSymbol,
    string PromptTitle,
    string PromptMessage,
    string CancelLabel);

public sealed record QrScanResult(bool Success, string? Payload, string? ErrorCode, bool Cancelled)
{
    public static QrScanResult Completed(string payload) => new(true, payload, null, false);
    public static QrScanResult Failed(string errorCode) => new(false, null, errorCode, false);
    public static QrScanResult Canceled() => new(false, null, null, true);
}
