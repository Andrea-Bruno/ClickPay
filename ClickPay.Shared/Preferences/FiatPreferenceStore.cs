using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClickPay.Shared.Services;
using ClickPay.Wallet.Core.Services;

namespace ClickPay.Shared.Preferences;

/// <summary>
/// Persists and exposes the fiat currency preference selected by the end user.
/// </summary>
public sealed class FiatPreferenceStore
{
    private const string PreferenceKey = "settings_fiat_currency";
    public const string DefaultCurrencyCode = "EUR";

    private static readonly FiatCurrencyOption[] SupportedCurrenciesInternal =
    {
        new("EUR", "FiatCurrency_EUR", "€"),
        new("USD", "FiatCurrency_USD", "$"),
        new("JPY", "FiatCurrency_JPY", "¥"),
        new("GBP", "FiatCurrency_GBP", "£"),
        new("CNY", "FiatCurrency_CNY", "¥"),
        new("AUD", "FiatCurrency_AUD", "A$"),
        new("CAD", "FiatCurrency_CAD", "C$"),
        new("CHF", "FiatCurrency_CHF", "CHF"),
        new("HKD", "FiatCurrency_HKD", "HK$"),
        new("SGD", "FiatCurrency_SGD", "S$"),
        new("INR", "FiatCurrency_INR", "₹")
    };

    private static readonly IReadOnlyDictionary<string, FiatCurrencyOption> SupportedByCode =
        new ReadOnlyDictionary<string, FiatCurrencyOption>(SupportedCurrenciesInternal.ToDictionary(
            option => option.Code,
            option => option,
            StringComparer.OrdinalIgnoreCase));

    private readonly ILocalSecureStore _secureStore;

    public FiatPreferenceStore(ILocalSecureStore secureStore)
    {
        _secureStore = secureStore ?? throw new ArgumentNullException(nameof(secureStore));
    }

    public IReadOnlyList<FiatCurrencyOption> GetSupportedCurrencies() => SupportedCurrenciesInternal;

    public FiatCurrencyOption GetDefaultOption() => SupportedByCode[DefaultCurrencyCode];

    public async Task<FiatCurrencyOption> GetPreferredOptionAsync()
    {
        var stored = await _secureStore.GetAsync<string?>(PreferenceKey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stored) && SupportedByCode.TryGetValue(stored, out var option))
        {
            return option;
        }

        return GetDefaultOption();
    }

    public async Task<FiatCurrencyOption> GetPreferredOptionAsync(string? fallbackCode)
    {
        if (!string.IsNullOrWhiteSpace(fallbackCode) && SupportedByCode.TryGetValue(fallbackCode, out var option))
        {
            return option;
        }

        return await GetPreferredOptionAsync().ConfigureAwait(false);
    }

    public bool IsSupported(string? code) => !string.IsNullOrWhiteSpace(code) && SupportedByCode.ContainsKey(code);

    public FiatCurrencyOption GetOptionOrDefault(string? code)
        => !string.IsNullOrWhiteSpace(code) && SupportedByCode.TryGetValue(code, out var option)
            ? option
            : GetDefaultOption();

    public async Task<FiatPreferenceUpdateResult> SetPreferredCurrencyAsync(string? code)
    {
        if (!IsSupported(code))
        {
            return new FiatPreferenceUpdateResult(false, GetDefaultOption(), "Valuta non supportata.");
        }

        var option = SupportedByCode[code!];
        var stored = await _secureStore.SetAsync(PreferenceKey, option.Code).ConfigureAwait(false);
        return stored
            ? new FiatPreferenceUpdateResult(true, option, null)
            : new FiatPreferenceUpdateResult(false, option, "Impossibile salvare la preferenza selezionata.");
    }

    public sealed record FiatCurrencyOption(string Code, string NameResourceKey, string? Symbol)
    {
    public string GetDisplayLabel(ClickPay.Shared.Services.LocalizationService localization)
        {
            if (localization is null)
            {
                throw new ArgumentNullException(nameof(localization));
            }

            var name = localization.T(NameResourceKey);
            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, NameResourceKey, StringComparison.Ordinal))
            {
                name = Code;
            }

            return string.IsNullOrWhiteSpace(Symbol)
                ? $"{name} ({Code})"
                : $"{name} ({Code}) {Symbol}";
        }
    }

    public sealed record FiatPreferenceUpdateResult(bool Success, FiatCurrencyOption Option, string? ErrorMessage);
}
