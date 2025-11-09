using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.CryptoAssets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClickPay.Wallet.Core.Services
{
    public sealed class CoinGeckoExchangeRateService : IExchangeRateService
    {
        private const string RateSuffix = ".rate.json";

        private readonly HttpClient _httpClient;
        private readonly ILogger<CoinGeckoExchangeRateService>? _logger;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _cacheDirectory;
        private readonly Dictionary<string, Task> _refreshOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _refreshGate = new();

        public CoinGeckoExchangeRateService(HttpClient httpClient, IOptions<MarketDataCacheOptions>? options = null, ILogger<CoinGeckoExchangeRateService>? logger = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger;

            var configured = options?.Value ?? new MarketDataCacheOptions();

            var baseDirectory = !string.IsNullOrWhiteSpace(configured.CacheDirectory)
                ? configured.CacheDirectory
                : GetDefaultCacheDirectory();

            _cacheDirectory = EnsureDirectory(baseDirectory);

            _serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        public async Task<CachedExchangeRate?> GetRateAsync(CryptoAsset asset, string fiatCode, Action? refresh = null, CancellationToken cancellationToken = default)
        {
            if (asset is null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (string.IsNullOrWhiteSpace(fiatCode))
            {
                throw new ArgumentException("Fiat code is required.", nameof(fiatCode));
            }

            var path = GetCacheFilePath(asset.Code, fiatCode);
            var gate = GetFileLock(path);
            CachedExchangeRate? cached = null;
            var shouldRefresh = false;
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cached = await TryReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (cached is not null)
                {
                    shouldRefresh = IsExpired(cached);
                }
            }
            finally
            {
                gate.Release();
            }

            if (cached is null)
            {
                QueueRefresh(asset, fiatCode, path, refresh);
                return null;
            }

            if (shouldRefresh)
            {
                QueueRefresh(asset, fiatCode, path, refresh);
            }

            return cached;
        }

        private async Task<CachedExchangeRate?> RefreshInternalAsync(CryptoAsset asset, string fiatCode, string path, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(asset.CoingeckoId))
            {
                _logger?.LogDebug("No CoinGecko id configured for asset {Asset}.", asset.Code);
                return null;
            }

            var refreshed = await FetchRateAsync(asset.CoingeckoId, fiatCode, cancellationToken).ConfigureAwait(false);
            if (refreshed is null)
            {
                return null;
            }

            var entry = new CachedExchangeRate(refreshed.Value, DateTime.UtcNow);
            await WriteAsync(path, new RateCacheDocument(asset.Code, fiatCode, entry.Value, entry.TimestampUtc), cancellationToken).ConfigureAwait(false);
            return entry;
        }

        private static bool IsExpired(CachedExchangeRate entry)
        {
            if (entry is null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            return DateTime.UtcNow - entry.TimestampUtc >= ServiceCacheDefaults.Lifetime;
        }

        private SemaphoreSlim GetFileLock(string path)
        {
            return _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        }

        private static string EnsureDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Cache directory cannot be empty.", nameof(directory));
            }

            Directory.CreateDirectory(directory);
            return directory;
        }

        private static string GetDefaultCacheDirectory()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = AppContext.BaseDirectory;
            }

            return Path.Combine(basePath, "ClickPay", "cache");
        }

        private string GetCacheFilePath(string assetCode, string fiatCode)
        {
            var safeAsset = SanitizeSegment(assetCode);
            var safeFiat = SanitizeSegment(fiatCode);
            var fileName = $"{safeAsset}-{safeFiat}{RateSuffix}";
            return Path.Combine(_cacheDirectory, fileName);
        }

        private static string SanitizeSegment(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var lower = value.ToLowerInvariant();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                lower = lower.Replace(invalid, '-');
            }

            return lower;
        }

        private async Task<CachedExchangeRate?> TryReadAsync(string path, CancellationToken cancellationToken)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var document = await JsonSerializer.DeserializeAsync<RateCacheDocument>(stream, _serializerOptions, cancellationToken).ConfigureAwait(false);
                if (document is null)
                {
                    return null;
                }

                return new CachedExchangeRate(document.Value, DateTime.SpecifyKind(document.TimestampUtc, DateTimeKind.Utc));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to read cached rate file {Path}.", path);
                return null;
            }
        }

        private async Task WriteAsync(string path, RateCacheDocument document, CancellationToken cancellationToken)
        {
            try
            {
                var tempPath = path + ".tmp";
                await using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, document, _serializerOptions, cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unable to persist rate cache to {Path}.", path);
            }
        }

        private void QueueRefresh(CryptoAsset asset, string fiatCode, string path, Action? refresh)
        {
            var key = $"{SanitizeSegment(asset.Code)}:{SanitizeSegment(fiatCode)}";

            lock (_refreshGate)
            {
                if (_refreshOperations.TryGetValue(key, out var existing) && !existing.IsCompleted)
                {
                    return;
                }

                var task = Task.Run(async () =>
                {
                    try
                    {
                        var entry = await RefreshInternalAsync(asset, fiatCode, path, CancellationToken.None).ConfigureAwait(false);
                        if (entry is not null)
                        {
                            refresh?.Invoke();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Unable to refresh CoinGecko rate for {Asset} -> {Fiat}.", asset.Code, fiatCode);
                    }
                    finally
                    {
                        lock (_refreshGate)
                        {
                            _refreshOperations.Remove(key);
                        }
                    }
                });

                _refreshOperations[key] = task;
            }
        }

        private async Task<decimal?> FetchRateAsync(string coinGeckoId, string fiatCode, CancellationToken cancellationToken)
        {
            try
            {
                var vsCurrency = fiatCode.ToLowerInvariant();
                var url = $"https://api.coingecko.com/api/v3/simple/price?ids={coinGeckoId}&vs_currencies={vsCurrency}";
                var payload = await _httpClient.GetFromJsonAsync<Dictionary<string, Dictionary<string, decimal>>>(url, cancellationToken).ConfigureAwait(false);
                if (payload is not null &&
                    payload.TryGetValue(coinGeckoId, out var perAsset) &&
                    perAsset.TryGetValue(vsCurrency, out var value))
                {
                    return value;
                }

                _logger?.LogWarning("CoinGecko response missing expected data for asset {AssetId} and currency {Currency}.", coinGeckoId, fiatCode);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to fetch CoinGecko rate for {AssetId} -> {Currency}.", coinGeckoId, fiatCode);
            }

            return null;
        }

        private sealed record RateCacheDocument(string AssetCode, string FiatCode, decimal Value, DateTime TimestampUtc);
    }
}
