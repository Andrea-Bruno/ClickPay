using System;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ClickPay.Web.Services;

public sealed class WebBiometricLockService : IBiometricLockService, IAsyncDisposable
{
    private const string PreferenceKey = "settings_biometric_lock";
    private const string CredentialKey = "settings_biometric_credential";
    private readonly ILocalSecureStore _secureStore;
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    private readonly ILogger<WebBiometricLockService>? _logger;

    public WebBiometricLockService(ILocalSecureStore secureStore, IJSRuntime jsRuntime, ILogger<WebBiometricLockService>? logger = null)
    {
        _secureStore = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
        if (jsRuntime is null)
        {
            throw new ArgumentNullException(nameof(jsRuntime));
        }

        _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/ClickPay.Shared/js/biometric-lock.js").AsTask());
        _logger = logger;
    }

    public async ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            return await module.InvokeAsync<bool>("isBiometricAvailable").ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (InteropNotReady(ex))
        {
            return false;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
        catch (JSException ex)
        {
            _logger?.LogDebug(ex, "Biometric availability check failed");
            return false;
        }
    }

    public async ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _secureStore.GetAsync<bool?>(PreferenceKey).ConfigureAwait(false);
        return stored ?? false;
    }

    public async ValueTask<BiometricPreferenceUpdateResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (!enabled)
        {
            await _secureStore.DeleteAsync(PreferenceKey).ConfigureAwait(false);
            await _secureStore.DeleteAsync(CredentialKey).ConfigureAwait(false);
            return BiometricPreferenceUpdateResult.Succeeded(false);
        }

        if (!await IsSupportedAsync(cancellationToken).ConfigureAwait(false))
        {
            await _secureStore.DeleteAsync(PreferenceKey).ConfigureAwait(false);
            await _secureStore.DeleteAsync(CredentialKey).ConfigureAwait(false);
            return BiometricPreferenceUpdateResult.Failed("Il browser non supporta l'autenticazione biometrica.");
        }

        var credentialId = await _secureStore.GetAsync<string?>(CredentialKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            var created = await RegisterCredentialAsync().ConfigureAwait(false);
            if (!created.Success)
            {
                if (!string.IsNullOrWhiteSpace(created.ErrorMessage))
                {
                    return BiometricPreferenceUpdateResult.Failed(created.ErrorMessage);
                }

                return created.Canceled
                    ? BiometricPreferenceUpdateResult.Failed("Registrazione biometrica annullata dall'utente.")
                    : BiometricPreferenceUpdateResult.Failed("Registrazione biometrica non riuscita.");
            }

            if (!await _secureStore.SetAsync(CredentialKey, created.CredentialId).ConfigureAwait(false))
            {
                return BiometricPreferenceUpdateResult.Failed("Impossibile salvare la credenziale biometrica.");
            }
        }

        if (!await _secureStore.SetAsync(PreferenceKey, true).ConfigureAwait(false))
        {
            return BiometricPreferenceUpdateResult.Failed("Impossibile salvare la preferenza.");
        }

        return BiometricPreferenceUpdateResult.Succeeded(true);
    }

    public async ValueTask<BiometricUnlockResult> EnsureUnlockedAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return BiometricUnlockResult.Completed();
        }

        var credentialId = await _secureStore.GetAsync<string?>(CredentialKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credentialId))
        {
            await _secureStore.DeleteAsync(PreferenceKey).ConfigureAwait(false);
            return BiometricUnlockResult.Failed("Credenziale biometrica non trovata.");
        }

        var result = await AuthenticateAsync(credentialId).ConfigureAwait(false);
        if (result.Success)
        {
            return BiometricUnlockResult.Completed();
        }

        if (result.Canceled)
        {
            return BiometricUnlockResult.Canceled();
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return BiometricUnlockResult.Failed(result.ErrorMessage);
        }

        return BiometricUnlockResult.Failed("Autenticazione biometrica non riuscita.");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_moduleTask.IsValueCreated)
        {
            return;
        }

        try
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (InteropNotReady(ex))
        {
            // Static rendering, JS interop not yet available; nothing to dispose client-side.
        }
        catch (JSDisconnectedException)
        {
            // Circuit terminated, nothing to dispose on client.
        }
    }

    private async Task<RegistrationResult> RegisterCredentialAsync()
    {
        try
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            var result = await module.InvokeAsync<RegistrationResultDto>("registerBiometricCredential").ConfigureAwait(false);
            if (result is null)
            {
                return new RegistrationResult(false, null, false, "Registrazione biometrica non riuscita.");
            }

            return result.Success
                ? new RegistrationResult(true, result.CredentialId, false, null)
                : new RegistrationResult(false, null, result.Canceled, result.Message);
        }
        catch (InvalidOperationException ex) when (InteropNotReady(ex))
        {
            return new RegistrationResult(false, null, false, "La pagina non è pronta per l'interazione biometrica.");
        }
        catch (JSDisconnectedException)
        {
            return new RegistrationResult(false, null, false, "Connessione al browser interrotta.");
        }
        catch (JSException ex)
        {
            _logger?.LogDebug(ex, "Biometric credential registration failed");
            return new RegistrationResult(false, null, false, ex.Message);
        }
    }

    private async Task<AuthenticationResult> AuthenticateAsync(string credentialId)
    {
        try
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            var result = await module.InvokeAsync<AuthenticationResultDto>("authenticateBiometricCredential", credentialId).ConfigureAwait(false);
            if (result is null)
            {
                return new AuthenticationResult(false, false, "Autenticazione biometrica non riuscita.");
            }

            return result.Success
                ? new AuthenticationResult(true, false, null)
                : new AuthenticationResult(false, result.Canceled, result.Message);
        }
        catch (InvalidOperationException ex) when (InteropNotReady(ex))
        {
            return new AuthenticationResult(false, false, "La pagina non è pronta per l'autenticazione biometrica.");
        }
        catch (JSDisconnectedException)
        {
            return new AuthenticationResult(false, false, "Connessione al browser interrotta.");
        }
        catch (JSException ex)
        {
            _logger?.LogDebug(ex, "Biometric authentication failed");
            return new AuthenticationResult(false, false, ex.Message);
        }
    }

    private static bool InteropNotReady(InvalidOperationException ex)
        => ex.Message.Contains("JavaScript interop calls cannot be issued", StringComparison.OrdinalIgnoreCase);

    private sealed record RegistrationResult(bool Success, string? CredentialId, bool Canceled, string? ErrorMessage);
    private sealed record AuthenticationResult(bool Success, bool Canceled, string? ErrorMessage);

    private sealed record RegistrationResultDto(bool Success, string? CredentialId, bool Canceled, string? Message)
    {
        public string? ErrorMessage => Message;
    }

    private sealed record AuthenticationResultDto(bool Success, bool Canceled, string? Message)
    {
        public string? ErrorMessage => Message;
    }
}
