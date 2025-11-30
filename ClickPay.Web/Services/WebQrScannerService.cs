using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Services;
using Microsoft.JSInterop;

namespace ClickPay.Web.Services
{
    public sealed class WebQrScannerService : IQrScannerService, IAsyncDisposable
    {
        private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

        public WebQrScannerService(IJSRuntime jsRuntime)
        {
            if (jsRuntime is null)
            {
                throw new ArgumentNullException(nameof(jsRuntime));
            }

            _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/ClickPay.Shared/js/qr-scanner.js").AsTask());
        }

        public async Task<QrScanResult> ScanAsync(QrScanRequest request, CancellationToken cancellationToken = default)
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            var dto = await module.InvokeAsync<QrScanResultDto>("scanQr", cancellationToken, new
            {
                asset = (request.AssetCode ?? string.Empty).ToLowerInvariant(),
                symbol = request.ExpectedSymbol,
                promptTitle = request.PromptTitle,
                promptMessage = request.PromptMessage,
                cancelLabel = request.CancelLabel
            }).ConfigureAwait(false);

            if (dto is null)
            {
                return QrScanResult.Failed("QrScan_Error_Generic");
            }

            if (dto.Cancelled)
            {
                return QrScanResult.Canceled();
            }

            if (!dto.Success)
            {
                return QrScanResult.Failed(string.IsNullOrWhiteSpace(dto.ErrorCode) ? "QrScan_Error_Generic" : dto.ErrorCode);
            }

            return string.IsNullOrWhiteSpace(dto.Payload)
                ? QrScanResult.Failed("QrScan_Error_NoData")
                : QrScanResult.Completed(dto.Payload);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_moduleTask.IsValueCreated)
            {
                return;
            }

            try
            {
                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // Circuit already terminated: we ignore the error because the module is no longer reachable.
            }
        }

        private sealed record QrScanResultDto(bool Success, string? Payload, string? ErrorCode, bool Cancelled);
    }
}
