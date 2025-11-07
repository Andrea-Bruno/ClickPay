using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Shared.CryptoAssets;
using Microsoft.Extensions.Options;
using Solnet.Programs;
using Solnet.Rpc.Builders;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using Solnet.Wallet.Bip39;

namespace ClickPay.Shared.Services
{
    public sealed class SolanaWalletService
    {
        private const decimal LamportsPerSol = 1_000_000_000m;
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly SolanaWalletOptions _options;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<WalletAsset, PublicKey> _tokenMints;
        private readonly Dictionary<WalletAsset, byte> _tokenDecimals;

        public SolanaWalletService(IOptions<SolanaWalletOptions> optionsAccessor, HttpClient httpClient)
        {
            _options = optionsAccessor.Value ?? SolanaWalletOptions.Default;
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _tokenMints = new Dictionary<WalletAsset, PublicKey>();
            _tokenDecimals = new Dictionary<WalletAsset, byte>();

            RegisterTokenAsset(WalletAsset.Eurc);
            RegisterTokenAsset(WalletAsset.Xaut);
        }

        public SolanaWalletAccount DeriveAccount(string mnemonic, string? passphrase = null, int accountIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(mnemonic))
            {
                throw new ArgumentException("Mnemonic mancante.", nameof(mnemonic));
            }

            var sanitized = mnemonic.Trim();
            var wallet = new Wallet(sanitized, WordList.English, passphrase ?? string.Empty);
            var account = wallet.GetAccount(accountIndex);
            var derivationPath = $"m/{_options.Purpose}'/{_options.CoinType}'/{accountIndex}'/0'";
            return new SolanaWalletAccount(account, derivationPath, accountIndex);
        }

        public async Task<decimal> GetNativeSolBalanceAsync(SolanaWalletAccount account, CancellationToken cancellationToken = default)
        {
            var request = RpcRequest.Create("getBalance", account.Account.PublicKey.Key, new { commitment = _options.Commitment });
            var response = await SendRpcAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                return 0m;
            }

            var value = response.Json.RootElement.GetProperty("result").GetProperty("value").GetInt64();
            return value / LamportsPerSol;
        }

        public async Task<SolanaTokenBalance> GetTokenBalanceAsync(SolanaWalletAccount account, WalletAsset asset, CancellationToken cancellationToken = default)
        {
            var associatedAccount = GetAssociatedTokenAccount(account, asset);
            var request = RpcRequest.Create("getTokenAccountBalance", associatedAccount.ToString(), new { commitment = _options.Commitment });
            var response = await SendRpcAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.Success)
            {
                return SolanaTokenBalance.Empty;
            }

            var value = response.Json.RootElement.GetProperty("result").GetProperty("value");
            var amount = ParseDecimal(value.GetProperty("uiAmountString").GetString());
            var decimals = (byte)value.GetProperty("decimals").GetInt32();
            var mint = ResolveMint(asset);
            return new SolanaTokenBalance(account.Account.PublicKey.Key, mint.Key, associatedAccount.Key, amount, decimals);
        }

        public async Task<IReadOnlyList<WalletTransaction>> GetRecentTransactionsAsync(SolanaWalletAccount account, WalletAsset asset, int? limit = null, CancellationToken cancellationToken = default)
        {
            var historyLimit = limit ?? _options.TransactionHistoryLimit;
            string address = asset == WalletAsset.Sol
                ? account.Account.PublicKey.Key
                : GetAssociatedTokenAccount(account, asset).ToString();

            var signatureRequest = RpcRequest.Create("getSignaturesForAddress", address, new { limit = historyLimit, commitment = _options.Commitment });
            var signatureResponse = await SendRpcAsync(signatureRequest, cancellationToken).ConfigureAwait(false);
            if (!signatureResponse.Success)
            {
                return Array.Empty<WalletTransaction>();
            }

            var result = new List<WalletTransaction>();
            foreach (var entry in signatureResponse.Json.RootElement.GetProperty("result").EnumerateArray())
            {
                var signature = entry.GetProperty("signature").GetString();
                if (string.IsNullOrWhiteSpace(signature))
                {
                    continue;
                }

                var txRequest = RpcRequest.Create("getTransaction", signature, new { encoding = "jsonParsed", commitment = _options.Commitment });
                var txResponse = await SendRpcAsync(txRequest, cancellationToken).ConfigureAwait(false);
                if (!txResponse.Success || !txResponse.Json.RootElement.TryGetProperty("result", out var txResult) || txResult.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                WalletTransaction? parsed = asset == WalletAsset.Sol
                    ? ParseNativeTransaction(txResult, account.Account.PublicKey.Key, signature)
                    : ParseTokenTransaction(txResult, address, signature);
                if (parsed is not null)
                {
                    result.Add(parsed);
                }
            }

            return result.OrderByDescending(tx => tx.Timestamp).ToArray();
        }

        public async Task<WalletSendResult> SendAsync(SolanaWalletAccount account, WalletAsset asset, string destinationAddress, decimal amount, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destinationAddress))
            {
                throw new ArgumentException("Indirizzo di destinazione mancante.", nameof(destinationAddress));
            }

            if (amount <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "L'importo deve essere positivo.");
            }

            var destination = new PublicKey(destinationAddress.Trim());
            var instructions = new List<TransactionInstruction>();
            string symbol;

            if (asset == WalletAsset.Sol)
            {
                var lamports = ConvertToLamports(amount);
                instructions.Add(SystemProgram.Transfer(account.Account.PublicKey, destination, lamports));
                symbol = "SOL";
            }
            else
            {
                var mint = ResolveMint(asset);
                var decimals = ResolveTokenDecimals(asset);
                var sourceTokenAccount = GetAssociatedTokenAccount(account, asset);
                var destinationTokenAccount = AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(destination, mint);

                if (!await AssociatedAccountExistsAsync(destinationTokenAccount, cancellationToken).ConfigureAwait(false))
                {
                    instructions.Add(AssociatedTokenAccountProgram.CreateAssociatedTokenAccount(account.Account.PublicKey, destination, mint));
                }

                var rawAmount = ConvertToRawTokenAmount(amount, decimals);
                instructions.Add(TokenProgram.Transfer(
                    sourceTokenAccount,
                    destinationTokenAccount,
                    rawAmount,
                    account.Account.PublicKey));

                symbol = ResolveSymbol(asset);
            }

            var blockhash = await GetLatestBlockhashAsync(cancellationToken).ConfigureAwait(false);

            var builder = new TransactionBuilder()
                .SetRecentBlockHash(blockhash)
                .SetFeePayer(account.Account.PublicKey);

            foreach (var instruction in instructions)
            {
                builder.AddInstruction(instruction);
            }

            var transaction = builder.Build(account.Account);
            var payload = Convert.ToBase64String(transaction);

            var sendRequest = RpcRequest.Create("sendTransaction", payload, new { skipPreflight = false, encoding = "base64", commitment = _options.Commitment });
            var response = await SendRpcAsync(sendRequest, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                throw new InvalidOperationException($"Invio della transazione Solana fallito: {response.ErrorMessage}");
            }

            var txId = response.Json.RootElement.GetProperty("result").GetString() ?? string.Empty;
            return new WalletSendResult(asset, symbol, txId, DateTimeOffset.UtcNow);
        }

        private async Task<bool> AssociatedAccountExistsAsync(PublicKey tokenAccount, CancellationToken cancellationToken)
        {
            var request = RpcRequest.Create("getAccountInfo", tokenAccount.ToString(), new { commitment = _options.Commitment });
            var response = await SendRpcAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                return false;
            }

            return response.Json.RootElement.GetProperty("result").GetProperty("value").ValueKind != JsonValueKind.Null;
        }

        private async Task<string> GetLatestBlockhashAsync(CancellationToken cancellationToken)
        {
            var request = RpcRequest.Create("getLatestBlockhash", new { commitment = _options.Commitment });
            var response = await SendRpcAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
            {
                throw new InvalidOperationException("Impossibile recuperare il blockhash più recente dalla rete Solana.");
            }

            return response.Json.RootElement.GetProperty("result").GetProperty("value").GetProperty("blockhash").GetString()
                ?? throw new InvalidOperationException("Blockhash non presente nella risposta RPC.");
        }

        private async Task<RpcResponse> SendRpcAsync(RpcRequest request, CancellationToken cancellationToken)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.RpcEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(request, SerializerOptions), Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var message = ExtractErrorMessage(document) ?? response.ReasonPhrase ?? "RPC error";
                return RpcResponse.Failed(document, message);
            }

            if (document.RootElement.TryGetProperty("error", out var rpcError))
            {
                var message = rpcError.TryGetProperty("message", out var msgElement) ? msgElement.GetString() : "RPC error";
                return RpcResponse.Failed(document, message ?? "RPC error");
            }

            return RpcResponse.Successful(document);
        }

        private static string? ExtractErrorMessage(JsonDocument document)
        {
            if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }

            return null;
        }

    private static WalletTransaction? ParseTokenTransaction(JsonElement transactionRoot, string associatedAccount, string signature)
        {
            if (!transactionRoot.TryGetProperty("meta", out var meta) || meta.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (!transactionRoot.TryGetProperty("transaction", out var transactionElement))
            {
                return null;
            }

            var accountKeys = transactionElement.GetProperty("message").GetProperty("accountKeys")
                .EnumerateArray()
                .Select(k => k.GetProperty("pubkey").GetString())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();

            var tokenAccountIndex = accountKeys.FindIndex(k => string.Equals(k, associatedAccount, StringComparison.OrdinalIgnoreCase));
            if (tokenAccountIndex < 0)
            {
                return null;
            }

            var postBalance = FindTokenBalance(meta, "postTokenBalances", tokenAccountIndex);
            var preBalance = FindTokenBalance(meta, "preTokenBalances", tokenAccountIndex);
            if (postBalance is null || preBalance is null)
            {
                return null;
            }

            var delta = postBalance.Value - preBalance.Value;
            var isIncoming = delta >= 0m;
            var absolute = Math.Abs(delta);

            var timestamp = transactionRoot.TryGetProperty("blockTime", out var blockTimeElement) && blockTimeElement.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(blockTimeElement.GetInt64())
                : DateTimeOffset.UtcNow;

            var counterparty = accountKeys.FirstOrDefault(k => !string.Equals(k, associatedAccount, StringComparison.OrdinalIgnoreCase));

            return new WalletTransaction(signature, timestamp, absolute, isIncoming, counterparty, null);
        }

        private static WalletTransaction? ParseNativeTransaction(JsonElement transactionRoot, string accountAddress, string signature)
        {
            if (!transactionRoot.TryGetProperty("meta", out var meta) || meta.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (!transactionRoot.TryGetProperty("transaction", out var transactionElement))
            {
                return null;
            }

            var accountKeys = transactionElement.GetProperty("message").GetProperty("accountKeys")
                .EnumerateArray()
                .Select(k => k.GetProperty("pubkey").GetString())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();

            var accountIndex = accountKeys.FindIndex(k => string.Equals(k, accountAddress, StringComparison.OrdinalIgnoreCase));
            if (accountIndex < 0)
            {
                return null;
            }

            if (!meta.TryGetProperty("preBalances", out var preBalances) || preBalances.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (!meta.TryGetProperty("postBalances", out var postBalances) || postBalances.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            if (accountIndex >= preBalances.GetArrayLength() || accountIndex >= postBalances.GetArrayLength())
            {
                return null;
            }

            var preLamports = preBalances[accountIndex].GetInt64();
            var postLamports = postBalances[accountIndex].GetInt64();

            var deltaLamports = postLamports - preLamports;
            if (deltaLamports == 0)
            {
                return null;
            }

            var delta = deltaLamports / LamportsPerSol;
            var isIncoming = delta >= 0m;
            var absolute = Math.Abs(delta);

            var timestamp = transactionRoot.TryGetProperty("blockTime", out var blockTimeElement) && blockTimeElement.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(blockTimeElement.GetInt64())
                : DateTimeOffset.UtcNow;

            var counterparty = accountKeys.FirstOrDefault(k => !string.Equals(k, accountAddress, StringComparison.OrdinalIgnoreCase));

            return new WalletTransaction(signature, timestamp, absolute, isIncoming, counterparty, null);
        }

        private static decimal? FindTokenBalance(JsonElement meta, string propertyName, int accountIndex)
        {
            if (!meta.TryGetProperty(propertyName, out var balances) || balances.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var element in balances.EnumerateArray())
            {
                if (element.TryGetProperty("accountIndex", out var idxElement) && idxElement.GetInt32() == accountIndex)
                {
                    var amountString = element.GetProperty("uiTokenAmount").GetProperty("uiAmountString").GetString();
                    return ParseDecimal(amountString);
                }
            }

            return null;
        }

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0m;
            }

            return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;
        }

        private static ulong ConvertToRawTokenAmount(decimal amount, byte decimals)
        {
            var multiplier = (decimal)Math.Pow(10, decimals);
            return (ulong)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
        }

        private static ulong ConvertToLamports(decimal amount) => (ulong)Math.Round(amount * LamportsPerSol, MidpointRounding.AwayFromZero);

        private void RegisterTokenAsset(WalletAsset asset)
        {
            var definition = WalletAssetHelper.GetDefinition(asset);

            if (definition.Network != BlockchainNetwork.Solana)
            {
                throw new InvalidOperationException($"L'asset {definition.Code} non è configurato per la rete Solana.");
            }

            if (string.IsNullOrWhiteSpace(definition.ContractAddress))
            {
                throw new InvalidOperationException($"L'asset {definition.Code} non specifica un contractAddress valido nel file di configurazione.");
            }

            if (definition.Decimals < 0 || definition.Decimals > byte.MaxValue)
            {
                throw new InvalidOperationException($"Il valore decimals per l'asset {definition.Code} deve essere compreso tra 0 e {byte.MaxValue}.");
            }

            _tokenMints[asset] = new PublicKey(definition.ContractAddress);
            _tokenDecimals[asset] = (byte)definition.Decimals;
        }

        private PublicKey GetAssociatedTokenAccount(SolanaWalletAccount account, WalletAsset asset)
        {
            var mint = ResolveMint(asset);
            return AssociatedTokenAccountProgram.DeriveAssociatedTokenAccount(account.Account.PublicKey, mint);
        }

        private PublicKey ResolveMint(WalletAsset asset)
        {
            if (_tokenMints.TryGetValue(asset, out var mint))
            {
                return mint;
            }

            throw new NotSupportedException($"Mint non disponibile per l'asset {asset}.");
        }

        private byte ResolveTokenDecimals(WalletAsset asset)
        {
            if (_tokenDecimals.TryGetValue(asset, out var decimals))
            {
                return decimals;
            }

            throw new NotSupportedException($"Decimali non disponibili per l'asset {asset}.");
        }

    private static string ResolveSymbol(WalletAsset asset) => WalletAssetHelper.GetDefinition(asset).Code;
    }

    public sealed record SolanaWalletAccount(Account Account, string DerivationPath, int AccountIndex)
    {
        public string PublicKey => Account.PublicKey.Key;
    }

    public sealed record SolanaTokenBalance(string Owner, string Mint, string TokenAccount, decimal Amount, byte Decimals)
    {
        public static SolanaTokenBalance Empty { get; } = new(string.Empty, string.Empty, string.Empty, 0m, 0);
    }

    public sealed class SolanaWalletOptions
    {
        public const string MainnetRpcEndpoint = "https://api.mainnet-beta.solana.com";
        public const string DevnetRpcEndpoint = "https://api.devnet.solana.com";
        public const string DefaultCommitment = "confirmed";
        public const string ProductionCommitment = "finalized";
        public const string MainnetEurcMint = "8XSigVnK9VwYq9FmkYdNvKcJsteVEh9UpAJZKkdYwGKu";
        public const string DevnetEurcMint = "CNG3NofM8JrjVNewc19hXtF87mpy4VbQJu1WDxhypoxg";
        public const string MainnetXautMint = "3y2QeZQ2y1k4EYBLETymFcpnSUsctNk6heAQ7VZ8qVtU";
        public const string DevnetXautMint = "6o9jVWeNseyX2VVaoduiRU8n6Pvx93w9u5VnXNucwC1X";

        public static SolanaWalletOptions Default => new();

        public string RpcEndpoint { get; set; } = MainnetRpcEndpoint;
        public string Commitment { get; set; } = DefaultCommitment;
        public string EurcMintAddress { get; set; } = MainnetEurcMint;
        public string XautMintAddress { get; set; } = MainnetXautMint;
        public byte EurcDecimals { get; set; } = 6;
        public byte XautDecimals { get; set; } = 6;
        public int Purpose { get; set; } = 44;
        public int CoinType { get; set; } = 501;
        public int TransactionHistoryLimit { get; set; } = 25;
    }

    internal sealed record RpcRequest(string Jsonrpc, string Method, object?[] Params, string Id)
    {
        public static RpcRequest Create(string method, params object?[] parameters) => new("2.0", method, parameters, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
    }

    internal sealed record RpcResponse(JsonDocument Json, bool Success, string? ErrorMessage)
    {
        public static RpcResponse Successful(JsonDocument json) => new(json, true, null);
        public static RpcResponse Failed(JsonDocument json, string? message) => new(json, false, message);
    }
}
