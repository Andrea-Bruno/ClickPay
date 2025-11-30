#if ANDROID
using System;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Services;
using Microsoft.Maui.ApplicationModel;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace ClickPay.Services;

public sealed class AndroidBiometricLockService : IBiometricLockService
{
    private const string PreferenceKey = "settings_biometric_lock";
    private readonly ILocalSecureStore _secureStore;
    private static bool _resolverConfigured;

    public AndroidBiometricLockService(ILocalSecureStore secureStore)
    {
        _secureStore = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
    }

    public async ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        var availability = await EvaluateAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        return availability.IsSupported;
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
            return BiometricPreferenceUpdateResult.Succeeded(false);
        }

        var availability = await EvaluateAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.IsSupported)
        {
            await _secureStore.DeleteAsync(PreferenceKey).ConfigureAwait(false);
            return BiometricPreferenceUpdateResult.Failed(availability.Message ?? "Autenticazione biometrica non disponibile.");
        }

        if (!availability.IsReady)
        {
            return BiometricPreferenceUpdateResult.Failed(availability.Message ?? "Configura l'autenticazione biometrica nelle impostazioni di sistema.");
        }

        var stored = await _secureStore.SetAsync(PreferenceKey, true).ConfigureAwait(false);
        return stored
            ? BiometricPreferenceUpdateResult.Succeeded(true)
            : BiometricPreferenceUpdateResult.Failed("Impossibile salvare la preferenza.");
    }

    public async ValueTask<BiometricUnlockResult> EnsureUnlockedAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            return BiometricUnlockResult.Completed();
        }

        var availability = await EvaluateAvailabilityAsync(cancellationToken).ConfigureAwait(false);
        if (!availability.IsReady)
        {
            await _secureStore.DeleteAsync(PreferenceKey).ConfigureAwait(false);
            return BiometricUnlockResult.Failed(availability.Message ?? "Autenticazione biometrica non disponibile.");
        }

        try
        {
            return await AuthenticateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return BiometricUnlockResult.Canceled();
        }
        catch (Exception ex)
        {
            return BiometricUnlockResult.Failed(ex.Message);
        }
    }

    private async ValueTask<AvailabilitySnapshot> EvaluateAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            EnsureActivityResolver();
            var availability = await CrossFingerprint.Current.GetAvailabilityAsync().ConfigureAwait(false);
            return availability switch
            {
                FingerprintAvailability.Available => new AvailabilitySnapshot(true, true, null),
                FingerprintAvailability.NoSensor => new AvailabilitySnapshot(false, false, "Sensore biometrico non presente su questo dispositivo."),
                FingerprintAvailability.NoFingerprint => new AvailabilitySnapshot(true, false, "Registra un'impronta digitale nelle impostazioni di sistema."),
                FingerprintAvailability.Denied => new AvailabilitySnapshot(false, false, "Permessi biometria negati."),
                _ => new AvailabilitySnapshot(false, false, "Autenticazione biometrica non disponibile.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AvailabilitySnapshot(false, false, "Autenticazione biometrica non disponibile.");
        }
    }

    private async Task<BiometricUnlockResult> AuthenticateAsync(CancellationToken cancellationToken)
    {
        EnsureActivityResolver();
        var request = new AuthenticationRequestConfiguration("Unlock ClickPay", "Confirm your identity to continue.")
        {
            AllowAlternativeAuthentication = false,
            CancelTitle = "Annulla",
            FallbackTitle = string.Empty
        };

        var result = await CrossFingerprint.Current.AuthenticateAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Authenticated)
        {
            return BiometricUnlockResult.Completed();
        }

        if (result.Status == FingerprintAuthenticationResultStatus.Canceled)
        {
            return BiometricUnlockResult.Canceled();
        }

        return BiometricUnlockResult.Failed(result.ErrorMessage ?? "Autenticazione biometrica non riuscita.");
    }

    private readonly record struct AvailabilitySnapshot(bool IsSupported, bool IsReady, string? Message);

    private static void EnsureActivityResolver()
    {
        if (_resolverConfigured)
        {
            return;
        }

        CrossFingerprint.SetCurrentActivityResolver(() => Platform.CurrentActivity);
        _resolverConfigured = true;
    }

}
#endif
