using System.Threading.Tasks;
using ClickPay.Wallet.Core.Services;

namespace ClickPay.Wallet.Core.Preferences
{
    public static class UserPreferenceUtility
    {
        private const string ThemeKey = "settings_theme";
        private const string LanguageKey = "settings_language";

        public static async Task<string> GetThemeAsync(ILocalSecureStore store)
        {
            var theme = await store.GetAsync<string?>(ThemeKey);
            return string.IsNullOrWhiteSpace(theme) ? "system" : theme;
        }

        public static async Task SetThemeAsync(ILocalSecureStore store, string theme)
        {
            await store.SetAsync(ThemeKey, theme);
        }

        public static async Task<string> GetLanguageAsync(ILocalSecureStore store)
        {
            var lang = await store.GetAsync<string?>(LanguageKey);
            return string.IsNullOrWhiteSpace(lang) ? "en" : lang;
        }

        public static async Task SetLanguageAsync(ILocalSecureStore store, string lang)
        {
            await store.SetAsync(LanguageKey, lang);
        }
    }
}
