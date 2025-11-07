using System.Threading;
using System.Threading.Tasks;

namespace ClickPay.Shared.Services
{
    public sealed class NoOpQrScannerService : IQrScannerService
    {
        public Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(QrScanResult.Failed("QrScan_Error_NotAvailable"));
        }
    }
}
