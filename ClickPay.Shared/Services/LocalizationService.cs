using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClickPay.Shared.Resources;


namespace ClickPay.Shared.Services
{
    public class LocalizationService
    {
    private readonly System.Resources.ResourceManager _resourceManager = ClickPay.Shared.Resources.Resources.ResourceManager;
        private string _currentLanguage;


        public event Action? LanguageChanged;


        public LocalizationService()
        {
            _currentLanguage = NormalizeLanguageCode(CultureInfo.CurrentUICulture.Name);
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
                LanguageChanged?.Invoke();
            }
        }

        // Versione asincrona per chi vuole solo async
        public Task<string> GetCurrentLanguageAsync()
        {
            return Task.FromResult(_currentLanguage);
        }

        public Task SetCurrentLanguageAsync(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var normalized = NormalizeLanguageCode(value);
                if (_currentLanguage != normalized)
                {
                    _currentLanguage = normalized;
                    LanguageChanged?.Invoke();
                }
            }
            return Task.CompletedTask;
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
