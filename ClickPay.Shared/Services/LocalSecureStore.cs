using System.Text.Json;
using Microsoft.AspNetCore.Components.ProtectedBrowserStorage;

namespace ClickPay.Shared.Services;

// Abstraction used by components to store small protected values.
public interface ILocalSecureStore
{
    Task<T?> GetAsync<T>(string key);
    Task<bool> SetAsync<T>(string key, T value);
    Task<bool> DeleteAsync(string key);
}

// Concrete implementation based on ProtectedLocalStorage.
public sealed class LocalSecureStore : ILocalSecureStore
{
    private readonly ProtectedLocalStorage _pls;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LocalSecureStore(ProtectedLocalStorage pls) => _pls = pls;

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var result = await _pls.GetAsync<string>(key).ConfigureAwait(false);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Value))
                return default;
            return JsonSerializer.Deserialize<T>(result.Value, JsonOptions);
        }
        catch (InvalidOperationException ex) when (IsInteropNotReady(ex))
        {
            // JS interop not ready yet (first static render) - caller may retry later.
            return default;
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value)
    {
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        try
        {
            await _pls.SetAsync(key, payload).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException ex) when (IsInteropNotReady(ex))
        {
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        try
        {
            await _pls.DeleteAsync(key).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException ex) when (IsInteropNotReady(ex))
        {
            return false;
        }
    }

    private static bool IsInteropNotReady(InvalidOperationException ex)
        => ex.Message.Contains("JavaScript interop calls cannot be issued", StringComparison.OrdinalIgnoreCase);
}