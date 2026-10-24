// --------------------------------------------------
// Umami 3DS Theme Editor - ScreenGrab.cs
// --------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace ThemeEditor.App.Controls
{
    /// <summary>A captured screen image plus where its top-left pixel sits in screen coordinates.</summary>
    public sealed class ScreenShot
    {
        public PixelBuffer Pixels { get; set; }
        public PixelPoint Origin { get; set; }

        /// <summary>True when the capture covers the whole screen rather than just this application's window.</summary>
        public bool IsFullScreen { get; set; }

        public bool TryGetPixel(PixelPoint screen, out byte r, out byte g, out byte b)
        {
            var x = screen.X - Origin.X;
            var y = screen.Y - Origin.Y;
            r = g = b = 0;
            if (x < 0 || y < 0 || x >= Pixels.Width || y >= Pixels.Height)
                return false;
            var i = (y * Pixels.Width + x) * 4;
            b = Pixels.Bgra[i];
            g = Pixels.Bgra[i + 1];
            r = Pixels.Bgra[i + 2];
            return true;
        }

        public PixelBuffer Crop(PixelPoint topLeft, int width, int height)
        {
            var dst = new byte[width * height * 4];
            for (var row = 0; row < height; row++)
            for (var col = 0; col < width; col++)
            {
                var sx = topLeft.X - Origin.X + col;
                var sy = topLeft.Y - Origin.Y + row;
                if (sx < 0 || sy < 0 || sx >= Pixels.Width || sy >= Pixels.Height)
                    continue;
                Buffer.BlockCopy(Pixels.Bgra, (sy * Pixels.Width + sx) * 4, dst, (row * width + col) * 4, 4);
            }
            return new PixelBuffer(width, height, dst);
        }
    }

    /// <summary>
    ///     Screen capture without a platform UI toolkit: GDI on Windows, the built-in screencapture tool on macOS and
    ///     the common command line grabbers on Linux. Falls back to a snapshot of this application's own window.
    /// </summary>
    public static class ScreenGrab
    {
        public static async Task<ScreenShot> CaptureAsync(Window fallbackWindow)
        {
            try
            {
                ScreenShot shot = null;
                if (OperatingSystem.IsWindows())
                    shot = await Task.Run(() => CaptureWindows());
                else if (OperatingSystem.IsMacOS())
                    shot = await Task.Run(() => CaptureTool("/usr/sbin/screencapture", "-x -m -t png {0}"));
                else if (OperatingSystem.IsLinux())
                    shot = await Task.Run(() => CaptureLinux());
                if (shot != null)
                    return shot;
            }
            catch (Exception)
            {
                // Fall through to the in-window capture
            }
            return CaptureWindow(fallbackWindow);
        }

        public static ScreenShot CaptureWindow(Window window)
        {
            var scale = window.RenderScaling;
            var size = new PixelSize((int) Math.Ceiling(window.Bounds.Width * scale), (int) Math.Ceiling(window.Bounds.Height * scale));
            using (var rtb = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
            {
                rtb.Render(window);
                return new ScreenShot
                {
                    Pixels = PixelBuffer.FromBitmap(rtb),
                    Origin = window.PointToScreen(new Point(0, 0)),
                    IsFullScreen = false
                };
            }
        }

        // ---------------------------------------------------------------- Linux / macOS

        private static ScreenShot CaptureLinux()
        {
            string[] candidates =
            {
                "grim|{0}",
                "gnome-screenshot|-f {0}",
                "spectacle|-b -n -f -o {0}",
                "maim|{0}",
                "scrot|-o {0}",
                "import|-window root {0}"
            };
            foreach (var c in candidates)
            {
                var parts = c.Split('|');
                var shot = CaptureTool(parts[0], parts[1]);
                if (shot != null)
                    return shot;
            }
            return null;
        }

        private static ScreenShot CaptureTool(string tool, string argsFormat)
        {
            var file = Path.Combine(Path.GetTempPath(), "umami-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tool,
                    Arguments = string.Format(argsFormat, "\"" + file + "\""),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null || !p.WaitForExit(5000))
                        return null;
                    if (p.ExitCode != 0)
                        return null;
                }
                if (!File.Exists(file) || new FileInfo(file).Length == 0)
                    return null;
                return new ScreenShot { Pixels = PixelBuffer.Load(file), Origin = new PixelPoint(0, 0), IsFullScreen = true };
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Ignore
                }
            }
        }

        // ---------------------------------------------------------------- Windows (GDI)

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        private static ScreenShot CaptureWindows()
        {
            const int srcCopy = 0x00CC0020;
            const int captureBlt = 0x40000000;
            var x = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
            var y = GetSystemMetrics(77);
            var w = GetSystemMetrics(78);
            var h = GetSystemMetrics(79);
            if (w <= 0 || h <= 0)
                return null;

            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var bmp = CreateCompatibleBitmap(screenDc, w, h);
            var old = SelectObject(memDc, bmp);
            try
            {
                BitBlt(memDc, 0, 0, w, h, screenDc, x, y, srcCopy | captureBlt);
                var info = new BitmapInfo
                {
                    Size = Marshal.SizeOf(typeof(BitmapInfo)),
                    Width = w,
                    Height = -h, // top-down
                    Planes = 1,
                    BitCount = 32
                };
                var bits = new byte[w * h * 4];
                SelectObject(memDc, old);
                GetDIBits(memDc, bmp, 0, (uint) h, bits, ref info, 0);
                for (var i = 3; i < bits.Length; i += 4)
                    bits[i] = 255;
                return new ScreenShot { Pixels = new PixelBuffer(w, h, bits), Origin = new PixelPoint(x, y), IsFullScreen = true };
            }
            finally
            {
                DeleteObject(bmp);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }
}
