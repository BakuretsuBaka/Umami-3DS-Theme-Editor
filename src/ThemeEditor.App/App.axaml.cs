// --------------------------------------------------
// Umami 3DS Theme Editor - App.axaml.cs
// --------------------------------------------------

using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ThemeEditor.App.Properties;

namespace ThemeEditor.App
{
    public class UmamiApplication : Application
    {
        public string[] LaunchArgs { get; private set; } = new string[0];

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                LaunchArgs = desktop.Args ?? new string[0];

                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(Settings.SettingsDirectory, "crash.log"), e.ExceptionObject.ToString());
                    }
                    catch (Exception)
                    {
                        // Nothing more can be done
                    }
                };

                var window = new MainWindow();
                desktop.MainWindow = window;
                desktop.Exit += (s, e) => Settings.Default.Save();

                // First non-option argument is a theme file to open
                string themeArg = null;
                for (var i = 0; i < LaunchArgs.Length; i++)
                {
                    if (LaunchArgs[i] == "--lang")
                    {
                        i++;
                        continue;
                    }
                    themeArg = LaunchArgs[i];
                    break;
                }
                window.StartupTheme = themeArg != null && File.Exists(themeArg) ? themeArg : null;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
