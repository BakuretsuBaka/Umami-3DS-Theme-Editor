using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace ThemeEditor.Tests
{
    /// <summary>
    ///     Gives the whole test run its own settings folder. Without it the tests would read (and overwrite) the
    ///     settings of the editor installed on the machine, e.g. a saved "Animate" toggle that keeps the previews
    ///     redrawing nonstop and makes the headless dispatcher never go idle.
    /// </summary>
    internal static class TestEnvironment
    {
        [ModuleInitializer]
        internal static void Init()
        {
            var dir = Path.Combine(Path.GetTempPath(), "umami-tests-" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("UMAMI_THEME_EDITOR_DATA", dir);
        }
    }
}
