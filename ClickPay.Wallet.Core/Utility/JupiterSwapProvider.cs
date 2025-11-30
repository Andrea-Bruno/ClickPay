using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;

namespace ClickPay.Wallet.Core.Utility
{
    public class JupiterSwapProvider : ISwapProvider
    {
        private static readonly HttpClient SwapHttpClient = new HttpClient { BaseAddress = new Uri("https://api.jup.ag/") };
        public string Name => "Jupiter";
        public bool Supports(CryptoAsset from, CryptoAsset to)
        {
            return from.Network == BlockchainNetwork.Solana && to.Network == BlockchainNetwork.Solana;
        }
        public async Task<SwapUtility.SwapQuote?> GetQuoteAsync(SwapUtility.SwapRequest request, CancellationToken ct = default)
        {
            var fromDecimals = request.From.Decimals > 0 ? request.From.Decimals : 9;
            var rawAmount = (ulong)(request.Amount * (decimal)Math.Pow(10, fromDecimals));
            var inputMint = request.From.EffectiveContractAddress ?? throw new InvalidOperationException($"Asset {request.From.Code} has no contract address");
            var outputMint = request.To.EffectiveContractAddress ?? throw new InvalidOperationException($"Asset {request.To.Code} has no contract address");
            var quoteUrl = $"swap/v1/quote?inputMint={inputMint}&outputMint={outputMint}&amount={rawAmount}&slippageBps=50";
            var quoteResp = await SwapHttpClient.GetAsync(quoteUrl, ct);
            if (!quoteResp.IsSuccessStatusCode)
                throw new Exception($"Jupiter API error: {quoteResp.StatusCode} - {await quoteResp.Content.ReadAsStringAsync(ct)}");
            var quoteJson = await quoteResp.Content.ReadAsStringAsync(ct);
            using var quoteDoc = JsonDocument.Parse(quoteJson);
            if (!quoteDoc.RootElement.TryGetProperty("outAmount", out var outAmountProp))
                throw new Exception("Invalid Jupiter response: missing outAmount");
            var outAmount = outAmountProp.GetString();
            if (string.IsNullOrWhiteSpace(outAmount) || !decimal.TryParse(outAmount, out var outAmountValue))
                throw new Exception("Invalid Jupiter response: invalid outAmount");
            var outAmountDecimal = outAmountValue / (decimal)Math.Pow(10, request.To.Decimals > 0 ? request.To.Decimals : 9);
            var exchangeRate = outAmountDecimal / request.Amount;
            return new SwapUtility.SwapQuote
            {
                QuoteId = Guid.NewGuid().ToString(),
                ExchangeRate = exchangeRate,
                MinReceive = outAmountDecimal * 0.95m,
                EstimatedFees = 0.000005m,
                EstimatedConfirmationTime = TimeSpan.FromSeconds(30),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
                RouteData = quoteJson
            };
        }
        public Task<SwapUtility.ValidationResult> ValidateAsync(SwapUtility.SwapRequest request, CancellationToken ct = default)
        {
            // Assume always valid for now, real checks in SwapUtility
            return Task.FromResult(new SwapUtility.ValidationResult { IsValid = true, SufficientFunds = true, NetworkReady = true });
        }
        public Task<SwapUtility.SwapResult> ExecuteSwapAsync(SwapUtility.SwapRequest request, string routeData, CancellationToken ct = default)
        {
            // Execution handled in SwapUtility for now
            return Task.FromResult(new SwapUtility.SwapResult(SwapUtility.OperationResult.NotSupported, "Not implemented in provider", SwapUtility.SwapStatus.Failed));
        }
    }
}
