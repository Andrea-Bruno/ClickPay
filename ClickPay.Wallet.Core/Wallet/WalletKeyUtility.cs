using System;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Wallet;
using ClickPay.Wallet.Core.Services;

namespace ClickPay.Wallet.Core.Wallet
{
    public static class WalletKeyUtility
    {
        private const string VaultStorageKey = "wallet_vault";

        public static async Task<WalletVault?> GetVaultAsync(ILocalSecureStore secureStore, CancellationToken cancellationToken = default)
        {
            if (secureStore is null)
                throw new ArgumentNullException(nameof(secureStore));
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await secureStore.GetAsync<WalletVault>(VaultStorageKey).ConfigureAwait(false);
            }
            catch (SecureStorageUnavailableException)
            {
                throw;
            }
        }

        public static async Task SaveVaultAsync(ILocalSecureStore secureStore, WalletVault vault, CancellationToken cancellationToken = default)
        {
            if (secureStore is null)
                throw new ArgumentNullException(nameof(secureStore));
            if (vault is null)
                throw new ArgumentNullException(nameof(vault));
            try
            {
                var stored = await secureStore.SetAsync(VaultStorageKey, vault).ConfigureAwait(false);
                if (!stored)
                {
                    throw new SecureStorageUnavailableException("Secure storage rejected the wallet vault.");
                }
            }
            catch (SecureStorageUnavailableException)
            {
                throw;
            }
        }

        public static async Task RemoveVaultAsync(ILocalSecureStore secureStore, CancellationToken cancellationToken = default)
        {
            if (secureStore is null)
                throw new ArgumentNullException(nameof(secureStore));
            try
            {
                await secureStore.DeleteAsync(VaultStorageKey).ConfigureAwait(false);
            }
            catch (SecureStorageUnavailableException)
            {
                throw;
            }
        }

        public static async Task<bool> HasVaultAsync(ILocalSecureStore secureStore, CancellationToken cancellationToken = default)
        {
            return await GetVaultAsync(secureStore, cancellationToken).ConfigureAwait(false) is not null;
        }
    }

    public sealed class SecureStorageUnavailableException : InvalidOperationException
    {
        public SecureStorageUnavailableException(string message)
            : base(message)
        {
        }

        public SecureStorageUnavailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
