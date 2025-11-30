using System;
using System.Globalization;
using System.Resources;
using ClickPay.Shared.Resources;

namespace ClickPay.Shared.Services
{
    public class LocalizationService
    {
        private string _currentLanguage;
        public event Action? LanguageChanged;

        public LocalizationService()
        {
            _currentLanguage = NormalizeLanguageCode(CultureInfo.CurrentUICulture.Name);
            CultureInfo.CurrentUICulture = new CultureInfo(_currentLanguage);
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
                CultureInfo.CurrentUICulture = new CultureInfo(_currentLanguage);
                LanguageChanged?.Invoke();
            }
        }

        public string T(string key)
        {
            var culture = new CultureInfo(_currentLanguage);
            return global::ClickPay.Shared.Resources.Resources.ResourceManager.GetString(key, culture) ?? key;
        }

        public string FormatWalletLabel(string assetLabel)
        {
            var format = T("WalletLabelFormat");
            return string.Format(CultureInfo.CurrentCulture, format, assetLabel);
        }

        public bool IsResourceKey(string key)
        {
            return global::ClickPay.Shared.Resources.Resources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) != null;
        }

        private string NormalizeLanguageCode(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return "en";
            }

            var normalized = language.Trim().ToLowerInvariant();
            var dashIndex = normalized.IndexOf('-');
            if (dashIndex >=0)
            {
                normalized = normalized[..dashIndex];
            }

            // Support only languages for which a .resx satellite exists
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

