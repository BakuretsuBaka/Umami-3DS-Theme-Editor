// --------------------------------------------------
// Umami 3DS Theme Editor - Program.cs
// --------------------------------------------------

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Fonts.Inter;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            ApplyLanguage(Settings.Default.Language, args);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<UmamiApplication>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }

        /// <summary>
        ///     Picks the UI culture: "--lang xx" on the command line wins, then the saved setting, then the OS.
        /// </summary>
        private static void ApplyLanguage(string saved, string[] args)
        {
            var lang = saved;
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == "--lang")
                    lang = args[i + 1];

            if (string.IsNullOrEmpty(lang))
                return;
            try
            {
                var culture = new CultureInfo(lang);
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // Unknown culture: keep the OS language
            }
        }
    }
}
