using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace BmsConfig
{
    public enum ThemeMode
    {
        System,
        Light,
        Dark,
    }

    /// <summary>
    /// Swaps the palette dictionary (Themes/Dark.xaml or Themes/Light.xaml) at runtime.
    /// </summary>
    public static class ThemeManager
    {
        const string SettingsKey = @"Software\BmsConfig";

        static ResourceDictionary s_current;

        public static ThemeMode Mode { get; private set; }
        public static bool IsDark { get; private set; }
        public static event EventHandler ThemeChanged;

        public static void Initialize()
        {
            Mode = LoadSetting();
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (Mode == ThemeMode.System && e.Category == UserPreferenceCategory.General)
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => Apply(ThemeMode.System, save: false)));
            };
            Apply(Mode, save: false);
        }

        public static void Apply(ThemeMode mode, bool save = true)
        {
            Mode = mode;
            IsDark = (mode == ThemeMode.Dark) || (mode == ThemeMode.System && !SystemUsesLightTheme());

            ResourceDictionary palette = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Config;component/Themes/" + (IsDark ? "Dark.xaml" : "Light.xaml"), UriKind.Absolute)
            };

            var merged = Application.Current.Resources.MergedDictionaries;
            if (s_current != null)
                merged.Remove(s_current);
            merged.Insert(0, palette);
            s_current = palette;

            foreach (Window w in Application.Current.Windows)
                ApplyTitleBar(w);

            if (save)
                SaveSetting(mode);

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        static bool SystemUsesLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key?.GetValue("AppsUseLightTheme");
                    if (value is int)
                        return (int)value != 0;
                }
            }
            catch (Exception)
            {
            }
            return false; // dark by default (eg. on Wine, where the value usually doesn't exist)
        }

        static ThemeMode LoadSetting()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    ThemeMode mode;
                    if (key != null && Enum.TryParse(key.GetValue("Theme") as string, out mode))
                        return mode;
                }
            }
            catch (Exception)
            {
            }
            return ThemeMode.System;
        }

        static void SaveSetting(ThemeMode mode)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                    key.SetValue("Theme", mode.ToString());
            }
            catch (Exception)
            {
            }
        }

        // ---- dark title bar (Win10 20H1+ / Win11); harmless no-op elsewhere --------------------

        [DllImport("dwmapi.dll", PreserveSig = true)]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void ApplyTitleBar(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int dark = IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch (Exception)
            {
            }
        }
    }
}
