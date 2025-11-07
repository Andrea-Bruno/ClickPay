using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.ProtectedBrowserStorage;

namespace ClickPay.Shared.Services
{
    public class WalletKeyService
    {
        private const string VaultStorageKey = "wallet_vault";
        private readonly ProtectedLocalStorage _storage;
        private readonly JsonSerializerOptions _serializerOptions;

        public WalletKeyService(ProtectedLocalStorage storage)
        {
            _storage = storage;
            _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = false
            };
        }

        public async Task<WalletVault?> GetVaultAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _storage.GetAsync<string>(VaultStorageKey).ConfigureAwait(false);
                if (!result.Success || string.IsNullOrWhiteSpace(result.Value))
                {
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<WalletVault>(result.Value, _serializerOptions);
                }
                catch (JsonException)
                {
                    return null;
                }
            }
            catch (InvalidOperationException ex) when (IsInteropUnavailable(ex))
            {
                throw new BrowserStorageUnavailableException("Protected storage is not available during static rendering.", ex);
            }
        }

        public async Task SaveVaultAsync(WalletVault vault, CancellationToken cancellationToken = default)
        {
            if (vault is null)
            {
                throw new ArgumentNullException(nameof(vault));
            }

            var payload = JsonSerializer.Serialize(vault, _serializerOptions);

            try
            {
                await _storage.SetAsync(VaultStorageKey, payload).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsInteropUnavailable(ex))
            {
                throw new BrowserStorageUnavailableException("Protected storage is not available during static rendering.", ex);
            }
        }

        public async Task RemoveVaultAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _storage.DeleteAsync(VaultStorageKey).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsInteropUnavailable(ex))
            {
                throw new BrowserStorageUnavailableException("Protected storage is not available during static rendering.", ex);
            }
        }

        public async Task<bool> HasVaultAsync(CancellationToken cancellationToken = default)
        {
            return await GetVaultAsync(cancellationToken).ConfigureAwait(false) is not null;
        }
        private static bool IsInteropUnavailable(InvalidOperationException ex)
            => ex.Message.IndexOf("JavaScript interop calls cannot be issued at this time", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class BrowserStorageUnavailableException : InvalidOperationException
    {
        public BrowserStorageUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
