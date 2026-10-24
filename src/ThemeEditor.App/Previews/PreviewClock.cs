// --------------------------------------------------
// Umami 3DS Theme Editor - PreviewClock.cs
// --------------------------------------------------

using System.Diagnostics;

namespace ThemeEditor.App.Previews
{
    /// <summary>Monotonic animation clock shared by every preview surface.</summary>
    public static class PreviewClock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();

        public static double Seconds => Watch.Elapsed.TotalSeconds;
    }
}
