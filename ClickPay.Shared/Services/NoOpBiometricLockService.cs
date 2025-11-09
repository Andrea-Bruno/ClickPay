using System.Threading;
using System.Threading.Tasks;
using ClickPay.Wallet.Core.Services;

namespace ClickPay.Shared.Services;

public sealed class NoOpBiometricLockService : IBiometricLockService
{
    public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);

    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);

    public ValueTask<BiometricPreferenceUpdateResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(BiometricPreferenceUpdateResult.Failed("La protezione biometrica non è supportata su questo dispositivo."));

    public ValueTask<BiometricUnlockResult> EnsureUnlockedAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(BiometricUnlockResult.Completed());
}
