using ClickPay.Wallet.Core.Utility;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Blockchain;
using ClickPay.Wallet.Core.CryptoAssets;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Logging;
using ClickPay.Wallet.Core.Utility;
using ClickPay.Wallet.Core.Services;

namespace ClickPay.Wallet.Core.Wallet
{
    public static class MultiChainWalletUtility
    {
        private const string OverviewSuffix = "overview";
        private const string TransactionsSuffix = "transactions";

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Task> _refreshOperations = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _refreshGate = new();
        private static readonly string _cacheDirectory = EnsureCacheDirectory();

        public static async Task<WalletOverview?> GetOverviewAsync(
            Func<CancellationToken, Task<WalletVault?>> getVaultAsync,
            WalletProviderRegistry providers,
            ILogger? logger,
            string assetCode,
            Action? refresh = null,
            CancellationToken cancellationToken = default)
        {
            var context = await ResolveContextAsync(getVaultAsync, providers, assetCode, cancellationToken).ConfigureAwait(false);
            var assetSegment = SanitizeSegment(context.Asset.Code);
            var path = GetCachePath(assetSegment, OverviewSuffix);
            var refreshKey = GetRefreshKey(assetSegment, OverviewSuffix);

            var cached = await ReadCacheAsync<WalletOverview>(logger, path, cancellationToken).ConfigureAwait(false);
            if (cached?.Payload is { } payload)
            {
                if (IsExpired(cached.TimestampUtc))
                {
                    QueueRefresh(logger, refreshKey, () => RefreshOverviewAsync(logger, context, path, refresh));
                }
                return payload;
            }

            var placeholder = CreatePlaceholderOverview(context.Asset);
            await WriteCacheAsync(logger, path, placeholder, cancellationToken).ConfigureAwait(false);

            QueueRefresh(logger, refreshKey, () => RefreshOverviewAsync(logger, context, path, refresh));

            logger?.LogDebug("Cache miss for {Asset}; returning placeholder overview.", context.Asset.Code);
            return placeholder;
        }

        public static async Task<WalletReceiveInfo> GetReceiveInfoAsync(
            Func<CancellationToken, Task<WalletVault?>> getVaultAsync,
            WalletProviderRegistry providers,
            string assetCode,
            CancellationToken cancellationToken = default)
        {
            var (asset, provider, vault) = await ResolveContextAsync(getVaultAsync, providers, assetCode, cancellationToken).ConfigureAwait(false);
            return await provider.GetReceiveInfoAsync(asset, vault, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<IReadOnlyList<WalletTransaction>> GetTransactionsAsync(
            Func<CancellationToken, Task<WalletVault?>> getVaultAsync,
            WalletProviderRegistry providers,
            ILogger? logger,
            string assetCode,
            Action? refresh = null,
            CancellationToken cancellationToken = default)
        {
            var context = await ResolveContextAsync(getVaultAsync, providers, assetCode, cancellationToken).ConfigureAwait(false);
            var assetSegment = SanitizeSegment(context.Asset.Code);
            var path = GetCachePath(assetSegment, TransactionsSuffix);

            var cached = await ReadCacheAsync<List<WalletTransaction>>(logger, path, cancellationToken).ConfigureAwait(false);
            if (cached?.Payload is { } payload)
            {
                if (IsExpired(cached.TimestampUtc))
                {
                    QueueRefresh(logger, GetRefreshKey(assetSegment, TransactionsSuffix), () => RefreshTransactionsAsync(logger, context, path, refresh));
                }
                return payload;
            }

            QueueRefresh(logger, GetRefreshKey(assetSegment, TransactionsSuffix), () => RefreshTransactionsAsync(logger, context, path, refresh));

            return Array.Empty<WalletTransaction>();
        }

        public static async Task<WalletSendResult> SendAsync(
            Func<CancellationToken, Task<WalletVault?>> getVaultAsync,
            WalletProviderRegistry providers,
            string assetCode,
            string destination,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            var (asset, provider, vault) = await ResolveContextAsync(getVaultAsync, providers, assetCode, cancellationToken).ConfigureAwait(false);
            var request = new WalletSendRequest(destination, amount);
            return await provider.SendAsync(asset, vault, request, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<(CryptoAsset Asset, IWalletProvider Provider, WalletVault Vault)> ResolveContextAsync(
            Func<CancellationToken, Task<WalletVault?>> getVaultAsync,
            WalletProviderRegistry providers,
            string assetCode,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(assetCode))
            {
                throw WalletError.AssetNotSupported("Asset code is required.");
            }

            var asset = WalletAssetHelper.GetDefinition(assetCode);
            var provider = providers.Resolve(asset);
            var vault = await getVaultAsync(cancellationToken).ConfigureAwait(false);
            if (vault is null)
            {
                throw WalletError.VaultUnavailable("Nessun wallet configurato. Completa l'onboarding.");
            }
            return (asset, provider, vault);
        }

        private static Task<WalletOverview> FetchOverviewAsync((CryptoAsset Asset, IWalletProvider Provider, WalletVault Vault) context, CancellationToken cancellationToken)
        {
            return context.Provider.GetOverviewAsync(context.Asset, context.Vault, cancellationToken);
        }

        private static async Task<List<WalletTransaction>> FetchTransactionsAsync((CryptoAsset Asset, IWalletProvider Provider, WalletVault Vault) context, CancellationToken cancellationToken)
        {
            var transactions = await context.Provider.GetTransactionsAsync(context.Asset, context.Vault, cancellationToken).ConfigureAwait(false);
            return transactions is List<WalletTransaction> list ? list : new List<WalletTransaction>(transactions);
        }

        private static async Task RefreshOverviewAsync(ILogger? logger, (CryptoAsset Asset, IWalletProvider Provider, WalletVault Vault) context, string path, Action? refresh)
        {
            try
            {
                var updated = await FetchOverviewAsync(context, CancellationToken.None).ConfigureAwait(false);
                await WriteCacheAsync(logger, path, updated, CancellationToken.None).ConfigureAwait(false);
                refresh?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Aggiornamento cache overview fallito per {Asset}.", context.Asset.Code);
            }
        }

        private static async Task RefreshTransactionsAsync(ILogger? logger, (CryptoAsset Asset, IWalletProvider Provider, WalletVault Vault) context, string path, Action? refresh)
        {
            try
            {
                var updated = await FetchTransactionsAsync(context, CancellationToken.None).ConfigureAwait(false);
                await WriteCacheAsync(logger, path, updated, CancellationToken.None).ConfigureAwait(false);
                refresh?.Invoke();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Aggiornamento cache transazioni fallito per {Asset}.", context.Asset.Code);
            }
        }

        private static bool IsExpired(DateTime timestampUtc)
        {
            return DateTime.UtcNow - timestampUtc >= ServiceCacheDefaults.Lifetime;
        }

        private static string GetCachePath(string assetSegment, string suffix)
        {
            var fileName = $"{assetSegment}-{suffix}.json";
            return Path.Combine(_cacheDirectory, fileName);
        }

        private static SemaphoreSlim GetFileLock(string path)
        {
            return _fileLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        }

        private static async Task<CacheDocument<T>?> ReadCacheAsync<T>(ILogger? logger, string path, CancellationToken cancellationToken)
        {
            var gate = GetFileLock(path);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return await JsonSerializer.DeserializeAsync<CacheDocument<T>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Impossibile leggere il file cache {Path}.", path);
                return null;
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task WriteCacheAsync<T>(ILogger? logger, string path, T payload, CancellationToken cancellationToken)
        {
            var document = new CacheDocument<T>(DateTime.UtcNow, payload);
            var tempPath = path + ".tmp";
            var gate = GetFileLock(path);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Impossibile scrivere il file cache {Path}.", path);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    TryDelete(tempPath);
                }

                gate.Release();
            }
        }

        private static void QueueRefresh(ILogger? logger, string key, Func<Task> refreshOperation)
        {
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
                        await refreshOperation().ConfigureAwait(false);
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

        private static string GetRefreshKey(string assetSegment, string suffix)
        {
            return $"{assetSegment}:{suffix}";
        }

        private static string SanitizeSegment(string value)
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

        private static string EnsureCacheDirectory()
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = AppContext.BaseDirectory;
            }

            var directory = Path.Combine(basePath, "ClickPay", "wallet-cache");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignored
            }
        }

        private static WalletOverview CreatePlaceholderOverview(CryptoAsset asset)
        {
            if (asset is null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            var symbol = string.IsNullOrWhiteSpace(asset.Symbol) ? asset.Code : asset.Symbol;
            return new WalletOverview(
                asset.Code,
                symbol,
                0m,
                Array.Empty<WalletTransaction>(),
                FiatEstimate: null,
                NativeBalanceDescriptor: null);
        }

        private sealed record CacheDocument<T>(DateTime TimestampUtc, T? Payload);
    }
}
