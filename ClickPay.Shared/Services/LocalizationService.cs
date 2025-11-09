using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Shared.Resources;
using ClickPay.Wallet.Core.Services;

namespace ClickPay.Shared.Services
{
    public class LocalizationService
    {
    private readonly System.Resources.ResourceManager _resourceManager = ClickPay.Shared.Resources.Resources.ResourceManager;
        private string _currentLanguage;
        private readonly UserPreferenceService? _userPreferences;

        public event Action? LanguageChanged;

        // Costruttore legacy (senza UserPreferenceService)
        public LocalizationService()
        {
            _currentLanguage = NormalizeLanguageCode(CultureInfo.CurrentUICulture.Name);
        }

        // Costruttore con UserPreferenceService per persistenza centralizzata
        public LocalizationService(UserPreferenceService userPreferences)
        {
            _userPreferences = userPreferences;
            _currentLanguage = "en";
        }

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                var normalized = NormalizeLanguageCode(value);
                if (_currentLanguage == normalized)
                {
                    LanguageChanged?.Invoke();
                    return;
                }
                _currentLanguage = normalized;
                // Se disponibile, salva anche in UserPreferenceService (sincrono per retrocompatibilità)
                _userPreferences?.SetLanguageAsync(normalized);
                LanguageChanged?.Invoke();
            }
        }

        // Versione asincrona per chi vuole solo async
        public async Task<string> GetCurrentLanguageAsync()
        {
            if (_userPreferences != null)
            {
                _currentLanguage = await _userPreferences.GetLanguageAsync();
            }
            return _currentLanguage;
        }

        public async Task SetCurrentLanguageAsync(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            var normalized = NormalizeLanguageCode(value);
            if (_currentLanguage == normalized)
            {
                LanguageChanged?.Invoke();
                return;
            }
            _currentLanguage = normalized;
            if (_userPreferences != null)
                await _userPreferences.SetLanguageAsync(normalized);
            LanguageChanged?.Invoke();
        }

        public string T(string key)
        {
            var culture = new CultureInfo(_currentLanguage);
            var value = _resourceManager.GetString(key, culture);
            return value ?? key;
        }

        public string FormatWalletLabel(string assetLabel)
        {
            var format = T("WalletLabelFormat");
            return string.Format(CultureInfo.CurrentCulture, format, assetLabel);
        }

		private string NormalizeLanguageCode(string language)
		{
			if (string.IsNullOrWhiteSpace(language))
			{
				return "en";
			}

			var normalized = language.Trim().ToLowerInvariant();
			var dashIndex = normalized.IndexOf('-');
			if (dashIndex >= 0)
			{
				normalized = normalized[..dashIndex];
			}

			// Supporta solo le lingue per cui esiste una .resx satellite
			return normalized switch
			{
				"it" => "it",
				"fr" => "fr",
				"es" => "es",
				"de" => "de",
				_ => "en"
			};
		}
    }
}
