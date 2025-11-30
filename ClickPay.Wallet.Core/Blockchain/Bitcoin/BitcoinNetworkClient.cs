using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace ClickPay.Wallet.Core.Blockchain.Bitcoin
{
    internal sealed class BitcoinNetworkClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public BitcoinNetworkClient(HttpClient httpClient, Network network)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = ResolveBaseUrl(network);
        }

        public async Task<IReadOnlyList<BlockstreamUtxo>> GetUtxosAsync(BitcoinAddress address, CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}address/{address}/utxo";
            var result = await _httpClient.GetFromJsonAsync<List<BlockstreamUtxo>>(url, cancellationToken).ConfigureAwait(false);
            return result?.ToArray() ?? Array.Empty<BlockstreamUtxo>();
        }

        public async Task<BlockstreamBalance?> GetBalanceAsync(BitcoinAddress address, CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}address/{address}";
            return await _httpClient.GetFromJsonAsync<BlockstreamBalance>(url, cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<BlockstreamTransaction>> GetTransactionsAsync(BitcoinAddress address, int limit = 25, CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}address/{address}/txs";
            var result = await _httpClient.GetFromJsonAsync<List<BlockstreamTransaction>>(url, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                return Array.Empty<BlockstreamTransaction>();
            }

            return limit > 0 ? result.GetRange(0, Math.Min(limit, result.Count)) : result;
        }

        public async Task<FeeRate> GetRecommendedFeeRateAsync(CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}fee-estimates";
            var response = await _httpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            var estimates = JsonSerializer.Deserialize<Dictionary<string, decimal>>(response);
            if (estimates is null || !estimates.TryGetValue("6", out var feeRate))
            {
                feeRate = 15m;
            }

            return new FeeRate(Money.Satoshis((long)Math.Max(1, Math.Round(feeRate, MidpointRounding.AwayFromZero))));
        }

        public async Task<string> BroadcastAsync(Transaction transaction, CancellationToken cancellationToken = default)
        {
            if (transaction is null)
            {
                throw new ArgumentNullException(nameof(transaction));
            }

            var raw = transaction.ToHex();
            var url = $"{_baseUrl}tx";
            var content = new StringContent(raw, Encoding.UTF8, "text/plain");
            var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException($"BTC transaction broadcast failed: {response.StatusCode} - {body}");
            }

            var txId = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return txId.Trim();
        }

        private static string ResolveBaseUrl(Network network)
        {
            if (network == Network.Main)
            {
                return "https://blockstream.info/api/";
            }

            if (network == Network.TestNet)
            {
                return "https://blockstream.info/testnet/api/";
            }

            if (network == Network.RegTest)
            {
                return "http://localhost:3002/";
            }

            return "https://blockstream.info/api/";
        }
    }

    internal sealed record BlockstreamUtxo(
        [property: System.Text.Json.Serialization.JsonPropertyName("txid")] string TxId,
        [property: System.Text.Json.Serialization.JsonPropertyName("vout")] int Vout,
        [property: System.Text.Json.Serialization.JsonPropertyName("value")] long Value,
        [property: System.Text.Json.Serialization.JsonPropertyName("scriptpubkey")] string ScriptPubKey,
        [property: System.Text.Json.Serialization.JsonPropertyName("scriptpubkey_address")] string Address,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] BlockstreamUtxoStatus Status)
    {
        public Money Amount => Money.Satoshis(Value);
    }

    internal sealed record BlockstreamUtxoStatus(
        [property: System.Text.Json.Serialization.JsonPropertyName("confirmed")] bool Confirmed,
        [property: System.Text.Json.Serialization.JsonPropertyName("block_height")] int? BlockHeight,
        [property: System.Text.Json.Serialization.JsonPropertyName("block_hash")] string? BlockHash
    );

    internal sealed record BlockstreamBalance(
        [property: System.Text.Json.Serialization.JsonPropertyName("chain_stats")] BlockstreamChainStats Chain,
        [property: System.Text.Json.Serialization.JsonPropertyName("mempool_stats")] BlockstreamMempoolStats Mempool)
    {
        public Money TotalConfirmed
        {
            get
            {
                var chainFunded = Chain?.Funded ?? 0;
                var chainSpent = Chain?.Spent ?? 0;
                return Money.Satoshis(chainFunded - chainSpent);
            }
        }
    }

    internal sealed record BlockstreamChainStats(
        [property: System.Text.Json.Serialization.JsonPropertyName("funded_txo_sum")] long Funded,
        [property: System.Text.Json.Serialization.JsonPropertyName("spent_txo_sum")] long Spent,
        [property: System.Text.Json.Serialization.JsonPropertyName("tx_count")] long TransactionCount
    );

    internal sealed record BlockstreamMempoolStats(
        [property: System.Text.Json.Serialization.JsonPropertyName("funded_txo_sum")] long Funded,
        [property: System.Text.Json.Serialization.JsonPropertyName("spent_txo_sum")] long Spent,
        [property: System.Text.Json.Serialization.JsonPropertyName("tx_count")] long TransactionCount
    );

    internal sealed record BlockstreamTransaction(
        [property: System.Text.Json.Serialization.JsonPropertyName("txid")] string TxId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] BlockstreamTransactionStatus Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("fee")] long Fee,
        [property: System.Text.Json.Serialization.JsonPropertyName("vin")] List<BlockstreamTransactionInput> Vin,
        [property: System.Text.Json.Serialization.JsonPropertyName("vout")] List<BlockstreamTransactionOutput> Vout
    );

    internal sealed record BlockstreamTransactionStatus(
        [property: System.Text.Json.Serialization.JsonPropertyName("confirmed")] bool Confirmed,
        [property: System.Text.Json.Serialization.JsonPropertyName("block_time")] long? BlockTime,
        [property: System.Text.Json.Serialization.JsonPropertyName("block_height")] long? BlockHeight
    );

    internal sealed record BlockstreamTransactionInput(
        [property: System.Text.Json.Serialization.JsonPropertyName("prevout")] BlockstreamTransactionOutput Prevout
    );

    internal sealed record BlockstreamTransactionOutput(
        [property: System.Text.Json.Serialization.JsonPropertyName("scriptpubkey_address")] string Address,
        [property: System.Text.Json.Serialization.JsonPropertyName("value")] long Value
    )
    {
        public decimal AsBtc => Value / 100_000_000m;
    }
}
