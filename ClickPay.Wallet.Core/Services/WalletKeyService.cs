using System;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Wallet;

namespace ClickPay.Wallet.Core.Services
{
    public class WalletKeyService
    {
        private const string VaultStorageKey = "wallet_vault";
        private readonly ILocalSecureStore _secureStore;

        public WalletKeyService(ILocalSecureStore secureStore)
        {
            _secureStore = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
        }

        public async Task<WalletVault?> GetVaultAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _secureStore.GetAsync<WalletVault>(VaultStorageKey).ConfigureAwait(false);
            }
            catch (SecureStorageUnavailableException)
            {
                throw;
            }
        }

        public async Task SaveVaultAsync(WalletVault vault, CancellationToken cancellationToken = default)
        {
            if (vault is null)
            {
                throw new ArgumentNullException(nameof(vault));
            }

            try
            {
                var stored = await _secureStore.SetAsync(VaultStorageKey, vault).ConfigureAwait(false);
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

        public async Task RemoveVaultAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _secureStore.DeleteAsync(VaultStorageKey).ConfigureAwait(false);
            }
            catch (SecureStorageUnavailableException)
            {
                throw;
            }
        }

        public async Task<bool> HasVaultAsync(CancellationToken cancellationToken = default)
        {
            return await GetVaultAsync(cancellationToken).ConfigureAwait(false) is not null;
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
