
using ClickPay.Wallet.Core.Wallet;
namespace ClickPay.Wallet.Core.Services;

// Abstraction used by components to store small protected values.
public interface ILocalSecureStore
{
    Task<T?> GetAsync<T>(string key);
    Task<bool> SetAsync<T>(string key, T value);
    Task<bool> DeleteAsync(string key);
}

/// <summary>
/// Placeholder implementation used on platforms where secure storage is not available.
/// Every call fails fast to avoid persisting sensitive data in an unsupported environment.
/// </summary>
public sealed class UnsupportedLocalSecureStore : ILocalSecureStore
{
    private static SecureStorageUnavailableException CreateException(string operation)
        => new($"Secure storage is not available in this hosting environment (operation: {operation}).");

    public Task<T?> GetAsync<T>(string key)
        => Task.FromException<T?>(CreateException($"get:{key}"));

    public Task<bool> SetAsync<T>(string key, T value)
        => Task.FromException<bool>(CreateException($"set:{key}"));

    public Task<bool> DeleteAsync(string key)
        => Task.FromException<bool>(CreateException($"delete:{key}"));
}