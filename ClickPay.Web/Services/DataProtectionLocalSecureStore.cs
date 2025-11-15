using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Services;
using ClickPay.Wallet.Core.Wallet;
using Microsoft.Extensions.Logging;

namespace ClickPay.Web.Services;

/// <summary>
/// Secure storage implementation for the web host that keeps values on the server file system
/// encrypted with Windows Data Protection API (DPAPI).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DataProtectionLocalSecureStore : ILocalSecureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string _baseDirectory;
    private readonly ILogger<DataProtectionLocalSecureStore>? _logger;

    public DataProtectionLocalSecureStore(ILogger<DataProtectionLocalSecureStore>? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new SecureStorageUnavailableException("Secure storage is not available on this device.");
        }

        _baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClickPay",
            "SecureStore");

        Directory.CreateDirectory(_baseDirectory);
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        ValidateKey(key);
        var path = GetPathForKey(key);

        if (!File.Exists(path))
        {
            return default;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var encrypted = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var protectedPayload = ProtectedData.Unprotect(encrypted, GetEntropy(key), DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(protectedPayload, JsonOptions);
        }
        catch (CryptographicException ex)
        {
            _logger?.LogWarning(ex, "Secure store payload invalid for key {Key}. Removing entry.", key);
            await TryDeleteInternalAsync(path).ConfigureAwait(false);
            return default;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Secure store payload unreadable for key {Key}. Removing entry.", key);
            await TryDeleteInternalAsync(path).ConfigureAwait(false);
            return default;
        }
        catch (Exception ex) when (IsUnavailableException(ex))
        {
            throw new SecureStorageUnavailableException("Secure storage is not available on this device.", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value)
    {
        ValidateKey(key);
        var path = GetPathForKey(key);

        if (value is null)
        {
            return await DeleteAsync(key).ConfigureAwait(false);
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            var encrypted = ProtectedData.Protect(payload, GetEntropy(key), DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(path, encrypted).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsUnavailableException(ex))
        {
            throw new SecureStorageUnavailableException("Secure storage is not available on this device.", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        ValidateKey(key);
        var path = GetPathForKey(key);

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return true;
            }

            await TryDeleteInternalAsync(path).ConfigureAwait(false);
            return !File.Exists(path);
        }
        catch (Exception ex) when (IsUnavailableException(ex))
        {
            throw new SecureStorageUnavailableException("Secure storage is not available on this device.", ex);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }
    }

    private string GetPathForKey(string key)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        var fileName = Convert.ToHexString(hash) + ".bin";
        return Path.Combine(_baseDirectory, fileName);
    }

    private static byte[] GetEntropy(string key)
        => Encoding.UTF8.GetBytes(key);

    private static bool IsUnavailableException(Exception ex)
        => ex is UnauthorizedAccessException
           || ex is IOException
           || ex is CryptographicException
           || ex is PlatformNotSupportedException;

    private static Task TryDeleteInternalAsync(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort cleanup
        }

        return Task.CompletedTask;
    }
}
