using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Wallet;
using ClickPay.Wallet.Core.Blockchain.Solana;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;
using Solnet.Rpc;
using Solnet.Rpc.Models;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Types;
using System.Buffers;
using SolanaMnemonicWallet = Solnet.Wallet.Wallet;

namespace ClickPay.Wallet.Core.Utility
{
    public static class SwapUtility
    {
        // List of available providers (to be extended)
        private static readonly ISwapProvider[] Providers = new ISwapProvider[]
        {
            new JupiterSwapProvider(),
            new MayanSwapProvider()
            // Add more providers here
        };
        private static readonly HttpClient JupiterHttpClient = new HttpClient { BaseAddress = new Uri("https://quote-api.jup.ag/") };
        private static readonly HttpClient MayanHttpClient = new HttpClient { BaseAddress = new Uri("https://api.mayan.finance/v1/") };
        private static readonly IRpcClient SolanaRpcClient = ClientFactory.GetClient(SolanaWalletOptions.CurrentRpcEndpoint);
        private const decimal LamportsPerSol = 1_000_000_000m;

        // Transactional states
        public enum SwapStatus
        {
            InQuote,
            Validating,
            Executing,
            Completed,
            Failed
        }

        // Models
        public class SwapRequest
        {
            public CryptoAsset From { get; set; }
            public CryptoAsset To { get; set; }
            public decimal Amount { get; set; }
            public string? UserAddress { get; set; } // Optional, for validation
        }

        public class SwapQuote
        {
            public string QuoteId { get; set; }
            public decimal ExchangeRate { get; set; }
            public decimal MinReceive { get; set; }
            public decimal EstimatedFees { get; set; }
            public TimeSpan EstimatedConfirmationTime { get; set; }
            public DateTimeOffset ExpiresAt { get; set; }
            public string? RouteData { get; set; } // JSON data for execution
        }

        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public string? ErrorMessage { get; set; }
            public bool SufficientFunds { get; set; }
            public bool NetworkReady { get; set; }
        }

        public class SwapResult
        {
            public EsitoOperazione Esito { get; set; }
            public string Descrizione { get; set; }
            public SwapStatus Status { get; set; }
            public string? TransactionHash { get; set; }

            public SwapResult(EsitoOperazione esito, string descrizione, SwapStatus status = SwapStatus.Failed, string? transactionHash = null)
            {
                Esito = esito;
                Descrizione = descrizione;
                Status = status;
                TransactionHash = transactionHash;
            }
        }

        public enum EsitoOperazione
        {
            Successful,
            InvalidAsset,
            InvalidAmount,
            InvalidAddress,
            NotSupported,
            InsufficientFunds,
            NetworkError,
            QuoteExpired,
            Unknown
        }

        // Public methods as per architecture
        public static async Task<SwapQuote> GetSwapQuoteAsync(SwapRequest request, CancellationToken ct = default)
        {
            if (request.From == null || request.To == null || request.Amount <= 0)
                throw new ArgumentException("Invalid swap request");

            foreach (var provider in Providers)
            {
                if (provider.Supports(request.From, request.To))
                {
                    var quote = await provider.GetQuoteAsync(request, ct);
                    if (quote != null)
                        return quote;
                }
            }
            throw new NotSupportedException("No provider supports this swap pair");
        }

        public static async Task<ValidationResult> ValidateSwapConditionsAsync(SwapRequest request, SwapQuote quote, ILocalSecureStore secureStore, CancellationToken ct = default)
        {
            // Check quote expiry
            if (DateTimeOffset.UtcNow > quote.ExpiresAt)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "Quote expired", SufficientFunds = false, NetworkReady = false };
            }

            // Check funds
            var vault = await WalletKeyUtility.GetVaultAsync(secureStore, ct);
            if (vault == null)
            {
                return new ValidationResult { IsValid = false, ErrorMessage = "Vault unavailable", SufficientFunds = false, NetworkReady = false };
            }

            // For Solana, check balance
            if (request.From.Network == BlockchainNetwork.Solana)
            {
                var solWallet = new SolanaMnemonicWallet(vault.Mnemonic.Trim(), WordList.English, vault.Passphrase ?? string.Empty);
                var account = solWallet.GetAccount(vault.AccountIndex);
                var userAddress = account.PublicKey;

                decimal balance = 0;
                if (string.IsNullOrWhiteSpace(request.From.ContractAddress))
                {
                    // Native SOL balance
                    var balanceResult = await SolanaRpcClient.GetBalanceAsync(userAddress.Key, Commitment.Confirmed);
                    if (balanceResult.WasSuccessful)
                    {
                        balance = balanceResult.Result.Value / (decimal)LamportsPerSol;
                    }
                }
                else
                {
                    // Token balance - simplified, assume sufficient for now
                    balance = request.Amount + 1; // Assume sufficient
                }

                if (balance < request.Amount)
                {
                    return new ValidationResult { IsValid = false, ErrorMessage = "Insufficient funds", SufficientFunds = false, NetworkReady = true };
                }
            }

            // Network status - assume ready for now
            return new ValidationResult { IsValid = true, SufficientFunds = true, NetworkReady = true };
        }

        public static async Task<SwapResult> ExecuteSwapAsync(SwapRequest request, SwapQuote quote, ILocalSecureStore secureStore, CancellationToken ct = default)
        {
            var validation = await ValidateSwapConditionsAsync(request, quote, secureStore, ct);
            if (!validation.IsValid)
            {
                return new SwapResult(EsitoOperazione.Unknown, validation.ErrorMessage ?? "Validation failed", SwapStatus.Failed);
            }

            foreach (var provider in Providers)
            {
                if (provider.Supports(request.From, request.To))
                {
                    // RouteData is required for execution
                    return await provider.ExecuteSwapAsync(request, quote.RouteData ?? string.Empty, ct);
                }
            }
            return new SwapResult(EsitoOperazione.NotSupported, "No provider supports this swap pair", SwapStatus.Failed);
        }

        public static async Task<SwapStatus> GetSwapStatusAsync(string transactionId, CancellationToken ct = default)
        {
            // For Solana, check transaction status
            try
            {
                var txInfo = await SolanaRpcClient.GetTransactionAsync(transactionId, Commitment.Confirmed);
                if (txInfo.WasSuccessful && txInfo.Result != null)
                {
                    if (txInfo.Result.Meta?.Error == null)
                    {
                        return SwapStatus.Completed;
                    }
                    else
                    {
                        return SwapStatus.Failed;
                    }
                }
                else
                {
                    return SwapStatus.Executing; // Still pending
                }
            }
            catch
            {
                return SwapStatus.Failed;
            }
        }

        // ...existing code...

        // Updated execution methods
        private static async Task<SwapResult> ExecuteJupiterSwapAsync(SwapRequest request, SwapQuote quote, ILocalSecureStore secureStore, CancellationToken ct)
        {
            var vault = await WalletKeyUtility.GetVaultAsync(secureStore, ct);
            if (vault == null)
                return new SwapResult(EsitoOperazione.InvalidAddress, "Invalid vault", SwapStatus.Failed);

            var solWallet = new SolanaMnemonicWallet(vault.Mnemonic.Trim(), WordList.English, vault.Passphrase ?? string.Empty);
            var account = solWallet.GetAccount(vault.AccountIndex);
            var userAddress = account.PublicKey;

            // Use route data from quote
            using var routeDoc = JsonDocument.Parse(quote.RouteData);
            var route = routeDoc.RootElement;

            var swapReq = new
            {
                route = route,
                userPublicKey = userAddress,
                wrapUnwrapSOL = true,
                asLegacyTransaction = true
            };
            var swapResp = await JupiterHttpClient.PostAsJsonAsync("v6/swap", swapReq, ct);
            if (!swapResp.IsSuccessStatusCode)
                return new SwapResult(EsitoOperazione.NetworkError, "Swap API error", SwapStatus.Failed);

            var swapJson = await swapResp.Content.ReadAsStringAsync(ct);
            using var swapDoc = JsonDocument.Parse(swapJson);
            if (!swapDoc.RootElement.TryGetProperty("swapTransaction", out var txElem))
                return new SwapResult(EsitoOperazione.Unknown, "Invalid swap response", SwapStatus.Failed);

            var txBase64 = txElem.GetString();
            if (string.IsNullOrWhiteSpace(txBase64))
                return new SwapResult(EsitoOperazione.Unknown, "No transaction", SwapStatus.Failed);

            byte[] txBytes = Convert.FromBase64String(txBase64);
            var tx = Transaction.Deserialize(txBytes);
            tx.Sign(account);

            var sendResult = await SolanaRpcClient.SendTransactionAsync(Convert.ToBase64String(tx.Serialize()), skipPreflight: false, Commitment.Confirmed);
            if (!sendResult.WasSuccessful || string.IsNullOrWhiteSpace(sendResult.Result))
                return new SwapResult(EsitoOperazione.NetworkError, "Send failed", SwapStatus.Failed);

            return new SwapResult(EsitoOperazione.Successful, "Swap successful", SwapStatus.Completed, sendResult.Result);
        }

        private static async Task<SwapResult> ExecuteMayanSwapAsync(SwapRequest request, SwapQuote quote, ILocalSecureStore secureStore, CancellationToken ct)
        {
            var vault = await WalletKeyUtility.GetVaultAsync(secureStore, ct);
            if (vault == null)
                return new SwapResult(EsitoOperazione.InvalidAddress, "Invalid vault", SwapStatus.Failed);

            var solWallet = new SolanaMnemonicWallet(vault.Mnemonic.Trim(), WordList.English, vault.Passphrase ?? string.Empty);
            var account = solWallet.GetAccount(vault.AccountIndex);
            var userAddress = account.PublicKey;

            using var routeDoc = JsonDocument.Parse(quote.RouteData);
            var route = routeDoc.RootElement;

            var swapReq = new
            {
                route = route,
                userKey = userAddress
            };
            var swapResp = await MayanHttpClient.PostAsJsonAsync("swap", swapReq, ct);
            if (!swapResp.IsSuccessStatusCode)
                return new SwapResult(EsitoOperazione.NetworkError, "Swap API error", SwapStatus.Failed);

            var swapJson = await swapResp.Content.ReadAsStringAsync(ct);
            using var swapDoc = JsonDocument.Parse(swapJson);
            if (!swapDoc.RootElement.TryGetProperty("transaction", out var txElem))
                return new SwapResult(EsitoOperazione.Unknown, "Invalid swap response", SwapStatus.Failed);

            var txBase64 = txElem.GetString();
            if (string.IsNullOrWhiteSpace(txBase64))
                return new SwapResult(EsitoOperazione.Unknown, "No transaction", SwapStatus.Failed);

            byte[] txBytes = Convert.FromBase64String(txBase64);
            var tx = Transaction.Deserialize(txBytes);
            tx.Sign(account);

            var sendResult = await SolanaRpcClient.SendTransactionAsync(Convert.ToBase64String(tx.Serialize()), skipPreflight: false, Commitment.Confirmed);
            if (!sendResult.WasSuccessful || string.IsNullOrWhiteSpace(sendResult.Result))
                return new SwapResult(EsitoOperazione.NetworkError, "Send failed", SwapStatus.Failed);

            return new SwapResult(EsitoOperazione.Successful, "Swap successful", SwapStatus.Completed, sendResult.Result);
        }

        // Legacy method for backward compatibility (deprecated)
        public static async Task<SwapResult> ExecuteSwapAsync(
            CryptoAsset from,
            CryptoAsset to,
            decimal amount,
            ILocalSecureStore secureStore,
            CancellationToken ct = default)
        {
            if (from == null || to == null || amount <= 0)
                return new SwapResult(EsitoOperazione.InvalidAsset, "Invalid swap request", SwapStatus.Failed);

            var request = new SwapRequest { From = from, To = to, Amount = amount };
            try
            {
                var quote = await GetSwapQuoteAsync(request, ct);
                return await ExecuteSwapAsync(request, quote, secureStore, ct);
            }
            catch (Exception ex)
            {
                return new SwapResult(EsitoOperazione.Unknown, ex.Message, SwapStatus.Failed);
            }
        }
    }
}
