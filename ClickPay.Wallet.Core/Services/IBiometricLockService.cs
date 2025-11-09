using System.Threading;
using System.Threading.Tasks;

namespace ClickPay.Wallet.Core.Services;

public interface IBiometricLockService
{
    ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
    ValueTask<BiometricPreferenceUpdateResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    ValueTask<BiometricUnlockResult> EnsureUnlockedAsync(CancellationToken cancellationToken = default);
}

public sealed record BiometricPreferenceUpdateResult(bool Success, bool Enabled, string? ErrorMessage)
{
    public static BiometricPreferenceUpdateResult Succeeded(bool enabled) => new(true, enabled, null);
    public static BiometricPreferenceUpdateResult Failed(string message, bool enabled = false) => new(false, enabled, message);
}

public sealed record BiometricUnlockResult(bool Success, bool IsCanceled, string? ErrorMessage)
{
    public static BiometricUnlockResult Completed() => new(true, false, null);
    public static BiometricUnlockResult Canceled() => new(false, true, null);
    public static BiometricUnlockResult Failed(string? message) => new(false, false, message);
}
