window.ThemeManager = {
    applyTheme: function (theme) {
        let t = theme;
        if (t === 'system') {
            t = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
        document.body.classList.remove('theme-light', 'theme-dark');
        document.body.classList.add('theme-' + t);
        document.body.setAttribute('data-theme', t);
    },
    setTheme: function (theme) {
        localStorage.setItem('theme', theme);
        this.applyTheme(theme);
    },
    getTheme: function () {
        return localStorage.getItem('theme') || 'system';
    },
    init: function () {
        const theme = this.getTheme();
        this.applyTheme(theme);
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
            if (this.getTheme() === 'system') {
                this.applyTheme('system');
            }
        });
    }
};

document.addEventListener('DOMContentLoaded', function () {
    window.ThemeManager.init();
});
