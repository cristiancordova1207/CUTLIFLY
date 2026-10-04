using System;
using System.Windows;
using Microsoft.Win32;

namespace Cutlifly.UI
{
    public static class ThemeManager
    {
        private static string _mode = "System";

        public static bool IsDark { get; private set; }

        public static void Apply(string mode)
        {
            _mode = mode ?? "System";
            bool dark = _mode == "Dark" || (_mode == "System" && !SystemUsesLightTheme());
            IsDark = dark;
            var dicts = Application.Current.Resources.MergedDictionaries;
            var uri = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
            dicts[0] = new ResourceDictionary { Source = uri };
        }

        public static void WatchSystem()
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _mode == "System")
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => Apply(_mode)));
            };
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v ? v != 0 : true;
            }
            catch { return true; }
        }
    }
}
