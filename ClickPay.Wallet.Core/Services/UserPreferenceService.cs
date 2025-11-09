using System.Threading.Tasks;

namespace ClickPay.Wallet.Core.Services
{
    public class UserPreferenceService
    {
        private const string ThemeKey = "settings_theme";
        private const string LanguageKey = "settings_language";
        private readonly ILocalSecureStore _store;

        public UserPreferenceService(ILocalSecureStore store)
        {
            _store = store;
        }

        public async Task<string> GetThemeAsync()
        {
            var theme = await _store.GetAsync<string?>(ThemeKey);
            return string.IsNullOrWhiteSpace(theme) ? "system" : theme;
        }

        public async Task SetThemeAsync(string theme)
        {
            await _store.SetAsync(ThemeKey, theme);
        }

        public async Task<string> GetLanguageAsync()
        {
            var lang = await _store.GetAsync<string?>(LanguageKey);
            return string.IsNullOrWhiteSpace(lang) ? "en" : lang;
        }

        public async Task SetLanguageAsync(string lang)
        {
            await _store.SetAsync(LanguageKey, lang);
        }
    }
}
