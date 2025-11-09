using System;
using System.Text.Json;
using ClickPay.Wallet.Core.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace ClickPay.Services;

/// <summary>
/// Implementation of <see cref="ILocalSecureStore"/> that uses <see cref="SecureStorage"/>
/// provided by .NET MAUI to persist encrypted secrets per device.
/// </summary>
public sealed class SecureStorageLocalSecureStore : ILocalSecureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        try
        {
            var payload = await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                // Corrupted payload – treat as missing value.
                return default;
            }
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            throw new SecureStorageUnavailableException("Secure storage is not available on this device.", ex);
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        var payload = JsonSerializer.Serialize(value, JsonOptions);

        try
        {
            await SecureStorage.Default.SetAsync(key, payload).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            throw new SecureStorageUnavailableException("Secure storage is not available on this device.", ex);
        }
    }

    public Task<bool> DeleteAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        try
        {
            var removed = SecureStorage.Default.Remove(key);
            return Task.FromResult(removed);
        }
        catch (Exception ex) when (IsStorageUnavailable(ex))
        {
            throw new SecureStorageUnavailableException("Secure storage is not available on this device.", ex);
        }
    }

    private static bool IsStorageUnavailable(Exception ex)
        => ex is NotSupportedException
           || ex is FeatureNotSupportedException
           || ex is InvalidOperationException
           || ex is UnauthorizedAccessException;
}
