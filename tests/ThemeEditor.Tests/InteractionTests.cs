using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThemeEditor.App;
using ThemeEditor.App.Controls;
using ThemeEditor.App.Experimental.CWAV;
using ThemeEditor.App.Previews;
using ThemeEditor.App.Properties;
using Xunit;

namespace ThemeEditor.Tests
{
    /// <summary>Puts the global preview settings back after a test that clicks around in the preview.</summary>
    internal sealed class SettingsGuard : IDisposable
    {
        private readonly Dictionary<PropertyInfo, object> _saved = new Dictionary<PropertyInfo, object>();
        private readonly List<CwavKind> _sounds = new List<CwavKind>();

        public SettingsGuard()
        {
            foreach (var p in typeof(Settings).GetProperties().Where(p => p.Name.StartsWith("Preview_") && p.CanWrite))
                _saved[p] = p.GetValue(Settings.Default);
            var s = Settings.Default;
            s.Preview_ModeToggle = false;
            s.Preview_PressedToggle = false;
            s.Preview_AnimationToggle = false;
            s.Preview_CloseToggle = true;
            s.Preview_Main_CursorX = 2;
            s.Preview_Main_CursorY = 2;
            s.Preview_Folder_CursorX = 2;
            s.Preview_Folder_CursorY = 1;
            s.Preview_ZoomRows = 3;
            PreviewInteraction.Shared.Page = 0;
            PreviewSounds.Requested += _sounds.Add;
        }

        public IReadOnlyList<CwavKind> Sounds => _sounds;

        public void Dispose()
        {
            PreviewSounds.Requested -= _sounds.Add;
            foreach (var kv in _saved)
                kv.Key.SetValue(Settings.Default, kv.Value);
        }
    }

    public class InteractionTests
    {
        [Fact]
        public void HitTestFindsControlsAndCells()
        {
            using (new SettingsGuard())
            {
                var s = Settings.Default;
                int c, r;
                Assert.Equal(PreviewTarget.Close, PreviewHit.Hit(s, 40, 230, out c, out r));
                Assert.Equal(PreviewTarget.Open, PreviewHit.Hit(s, 200, 230, out c, out r));
                s.Preview_CloseToggle = false;
                Assert.Equal(PreviewTarget.Open, PreviewHit.Hit(s, 40, 230, out c, out r));
                s.Preview_CloseToggle = true;

                Assert.Equal(PreviewTarget.Home, PreviewHit.Hit(s, 20, 12, out c, out r));
                Assert.Equal(PreviewTarget.CornerA, PreviewHit.Hit(s, 280, 12, out c, out r));
                Assert.Equal(PreviewTarget.CornerB, PreviewHit.Hit(s, 305, 12, out c, out r));
                Assert.Equal(PreviewTarget.Arrow, PreviewHit.Hit(s, 310, 123, out c, out r));
                Assert.Equal(PreviewTarget.None, PreviewHit.Hit(s, 400, 100, out c, out r));

                var cell = ItemRenderer_GridPosition(1, 2, false);
                Assert.Equal(PreviewTarget.Cell, PreviewHit.Hit(s, cell.X, cell.Y, out c, out r));
                Assert.Equal(1, c);
                Assert.Equal(2, r);

                // The back button exists only while a folder is open
                Assert.NotEqual(PreviewTarget.Back, PreviewHit.Hit(s, 60, 62, out c, out r));
                s.Preview_ModeToggle = true;
                Assert.Equal(PreviewTarget.Back, PreviewHit.Hit(s, 60, 62, out c, out r));
            }
        }

        private static SkiaSharp.SKPoint ItemRenderer_GridPosition(int col, int row, bool folder)
        {
            float[] cols = { 54, 107, 160, 213, 266 };
            float[] mainRows = { 176, 123, 70 };
            float[] folderRows = { 164, 110 };
            return new SkiaSharp.SKPoint(cols[col], (folder ? folderRows : mainRows)[row]);
        }

        [AvaloniaFact]
        public void ClickingMovesCursorOpensFolderAndGoesBack()
        {
            using (var g = new SettingsGuard())
            {
                var s = Settings.Default;
                var ia = new PreviewInteraction();
                var theme = NewTheme();

                // The folder sits at (0,1); the cursor starts at (2,2)
                PreviewActions.Click(theme, s, ia, PreviewTarget.Cell, 0, 1);
                Assert.Equal(0, s.Preview_Main_CursorX);
                Assert.Equal(1, s.Preview_Main_CursorY);
                Assert.False(s.Preview_ModeToggle);
                Assert.Equal(new[] { CwavKind.Cursor }, g.Sounds.ToArray());

                // Tapping the selected folder opens it
                PreviewActions.Click(theme, s, ia, PreviewTarget.Cell, 0, 1);
                Assert.True(s.Preview_ModeToggle);
                Assert.Equal(CwavKind.Folder, g.Sounds.Last());

                // Back leaves it again
                PreviewActions.Click(theme, s, ia, PreviewTarget.Back, -1, -1);
                Assert.False(s.Preview_ModeToggle);
                Assert.Equal(CwavKind.Close, g.Sounds.Last());
            }
        }

        [AvaloniaFact]
        public void OpenButtonOpensFoldersAndLaunchesTitles()
        {
            using (var g = new SettingsGuard())
            {
                var s = Settings.Default;
                var ia = new PreviewInteraction();
                var theme = NewTheme();

                // Cursor on the (empty) default cell: nothing happens
                PreviewActions.Click(theme, s, ia, PreviewTarget.Open, -1, -1);
                Assert.Empty(g.Sounds);

                // On the cartridge: launch
                s.Preview_Main_CursorX = s.Preview_Main_CartX;
                s.Preview_Main_CursorY = s.Preview_Main_CartY;
                PreviewActions.Click(theme, s, ia, PreviewTarget.Open, -1, -1);
                Assert.Equal(CwavKind.Launch, g.Sounds.Last());

                // On the folder: open it
                s.Preview_Main_CursorX = s.Preview_Main_FolderX;
                s.Preview_Main_CursorY = s.Preview_Main_FolderY;
                PreviewActions.Click(theme, s, ia, PreviewTarget.Open, -1, -1);
                Assert.True(s.Preview_ModeToggle);
                Assert.Equal(CwavKind.Folder, g.Sounds.Last());

                // Close button also leaves the folder
                PreviewActions.Click(theme, s, ia, PreviewTarget.Close, -1, -1);
                Assert.False(s.Preview_ModeToggle);
            }
        }

        [AvaloniaFact]
        public void ArrowPagesWallpaperAndStopsAnimation()
        {
            using (var g = new SettingsGuard())
            {
                var s = Settings.Default;
                var ia = new PreviewInteraction();
                var theme = NewTheme();
                // Switched on only now: with the animation running the headless dispatcher never goes idle
                s.Preview_AnimationToggle = true;
                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                PreviewActions.Click(theme, s, ia, PreviewTarget.Arrow, -1, -1);
                Assert.Equal(4, ia.Page);
                Assert.False(s.Preview_AnimationToggle);
                Assert.Equal(new[] { CwavKind.Frame0, CwavKind.Frame1, CwavKind.Frame2, CwavKind.Frame0 }, g.Sounds.ToArray());
            }
        }

        [AvaloniaFact]
        public void KeyboardMovesCursorWithinTheGrid()
        {
            using (var g = new SettingsGuard())
            {
                var s = Settings.Default;
                var theme = NewTheme();
                Assert.False(PreviewActions.MoveCursor(theme, s, 0, 1)); // already on the top row
                Assert.True(PreviewActions.MoveCursor(theme, s, -1, 0));
                Assert.Equal(1, s.Preview_Main_CursorX);
                for (var i = 0; i < 6; i++)
                    PreviewActions.MoveCursor(theme, s, -1, 0);
                Assert.Equal(0, s.Preview_Main_CursorX);
                for (var i = 0; i < 6; i++)
                    PreviewActions.MoveCursor(theme, s, 0, -1);
                Assert.Equal(0, s.Preview_Main_CursorY);

                // The folder view has two rows only
                s.Preview_ModeToggle = true;
                for (var i = 0; i < 4; i++)
                    PreviewActions.MoveCursor(theme, s, 0, 1);
                Assert.Equal(1, s.Preview_Folder_CursorY);
            }
        }

        [Fact]
        public void AnimationsRunToCompletion()
        {
            using (new SettingsGuard())
            {
                var s = Settings.Default;
                var ia = new PreviewInteraction();
                ia.Tick(0, s, null);
                s.Preview_ModeToggle = true;
                ia.LidOpenTarget = false;
                var midFolder = -1.0;
                for (var t = 0.0; t < 1.0; t += 0.02)
                {
                    ia.Tick(t + 0.02, s, null);
                    if (Math.Abs(t - 0.1) < 0.011)
                        midFolder = ia.FolderT;
                }
                Assert.InRange(midFolder, 0.05, 0.95);
                Assert.Equal(1, ia.FolderT);
                Assert.Equal(0, ia.Lid);
                Assert.False(ia.Busy);

                // Reopening the lid brings the screens back
                ia.LidOpenTarget = true;
                for (var t = 1.0; t < 2.0; t += 0.02)
                    ia.Tick(t + 0.02, s, null);
                Assert.Equal(1, ia.Lid);
            }
        }

        [Fact]
        public void CursorSlidesToItsNewCell()
        {
            using (new SettingsGuard())
            {
                var s = Settings.Default;
                var ia = new PreviewInteraction();
                ia.Tick(0, s, null);
                var start = ia.CursorPosition;
                s.Preview_Main_CursorX = 4;
                s.Preview_Main_CursorY = 0;
                ia.Tick(0.05, s, null);
                ia.Tick(0.09, s, null);
                var mid = ia.CursorPosition;
                Assert.True(mid.X > start.X && mid.X < 266);
                Assert.True(ia.Busy);
                ia.Tick(0.5, s, null);
                Assert.Equal(266, ia.CursorPosition.X, 1);
                Assert.Equal(176, ia.CursorPosition.Y, 1);
            }
        }

        [AvaloniaFact]
        public void PressedButtonsLookDifferentAndIdleStateMatchesStaticPreview()
        {
            using (new SettingsGuard())
            {
                var theme = NewTheme();
                var plain = PreviewExporter.RenderPng(theme, PreviewKind.Bottom);

                var idle = new PreviewInteraction();
                idle.Tick(0, Settings.Default, theme);
                idle.Tick(1, Settings.Default, theme);
                Assert.Equal(plain, PreviewExporter.RenderPng(theme, PreviewKind.Bottom, idle));

                foreach (var target in new[] { PreviewTarget.Open, PreviewTarget.Close, PreviewTarget.Arrow, PreviewTarget.Home, PreviewTarget.CornerA, PreviewTarget.CornerB })
                {
                    var ia = new PreviewInteraction();
                    ia.Tick(0, Settings.Default, theme);
                    ia.Tick(1, Settings.Default, theme);
                    ia.Press(target, -1, -1);
                    Assert.NotEqual(plain, PreviewExporter.RenderPng(theme, PreviewKind.Bottom, ia));
                }

                // Dragging off the button un-presses it
                var drag = new PreviewInteraction();
                drag.Tick(0, Settings.Default, theme);
                drag.Tick(1, Settings.Default, theme);
                drag.Press(PreviewTarget.Open, -1, -1);
                drag.SetOver(false);
                Assert.Equal(plain, PreviewExporter.RenderPng(theme, PreviewKind.Bottom, drag));
            }
        }

        [Fact]
        public void ThemeZipRoundTrips()
        {
            var body = new byte[] { 1, 2, 3, 4 };
            var smdh = new byte[] { 9, 8, 7 };
            var bgm = new byte[] { 5, 5, 5, 5, 5 };
            var zip = ThemeArchive.Create("My Theme", body, smdh, bgm, new byte[] { 1 }, new byte[] { 2 });
            var read = ThemeArchive.Read(zip);
            Assert.NotNull(read);
            Assert.Equal(body, read.Body);
            Assert.Equal(smdh, read.Smdh);
            Assert.Equal(bgm, read.Bgm);
            Assert.Equal("My Theme", read.Name);

            var dir = Path.Combine(Path.GetTempPath(), "umami-zip-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = ThemeArchive.ExtractTo(read, dir);
                Assert.Equal(body, File.ReadAllBytes(path));
                Assert.Equal(bgm, File.ReadAllBytes(Path.Combine(dir, "BGM.BCSTM")));
                Assert.True(File.Exists(Path.Combine(dir, "info.smdh")));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }

            // Without music the BGM entry is simply absent, and a ZIP with no theme is rejected
            Assert.Null(ThemeArchive.Read(ThemeArchive.Create("x", body, smdh, null, null, null)).Bgm);
            Assert.Null(ThemeArchive.Read(new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public void ThemeZipFindsFilesInsideAFolderAndAnyCase()
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    using (var w = z.CreateEntry("My Theme/Body_LZ.BIN").Open()) w.Write(new byte[] { 7 }, 0, 1);
                    using (var w = z.CreateEntry("My Theme/BGM.bcstm").Open()) w.Write(new byte[] { 8 }, 0, 1);
                    using (var w = z.CreateEntry("../evil/info.smdh").Open()) w.Write(new byte[] { 6 }, 0, 1);
                }
                var read = ThemeArchive.Read(ms.ToArray());
                Assert.Equal(new byte[] { 7 }, read.Body);
                Assert.Equal(new byte[] { 8 }, read.Bgm);
                // Path tricks never leave the target folder: only the file name is used
                var dir = Path.Combine(Path.GetTempPath(), "umami-zip-" + Guid.NewGuid().ToString("N"));
                try
                {
                    ThemeArchive.ExtractTo(read, dir);
                    Assert.All(Directory.GetFiles(dir), f => Assert.Equal(dir, Path.GetDirectoryName(f)));
                }
                finally
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
            }
        }

        [AvaloniaFact]
        public void MouseClicksInTheWindowDriveThePreview()
        {
            using (var g = new SettingsGuard())
            {
                var win = new MainWindow();
                win.Show();
                win.NewThemeCommandWrapper.Command.Execute(null);
                for (var i = 0; i < 100 && win.ViewModel == null; i++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(20);
                }
                Assert.NotNull(win.ViewModel);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var bottom = win.FindControl<BottomScreenPreview>("BottomPreview");
                Assert.NotNull(bottom);
                Assert.NotNull(bottom.Interaction);

                Point At(float x, float y)
                {
                    var p = bottom.TranslatePoint(new Point(x, y), win);
                    Assert.NotNull(p);
                    return p.Value;
                }

                void Click(float x, float y)
                {
                    var p = At(x, y);
                    win.MouseMove(p);
                    win.MouseDown(p, MouseButton.Left);
                    win.MouseUp(p, MouseButton.Left);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }

                var s = Settings.Default;
                var folder = ItemRenderer_GridPosition(0, 1, false);
                Click(folder.X, folder.Y);
                Assert.Equal(0, s.Preview_Main_CursorX);
                Assert.Equal(1, s.Preview_Main_CursorY);
                Assert.False(s.Preview_ModeToggle);

                Click(folder.X, folder.Y);
                Assert.True(s.Preview_ModeToggle);

                Click(60, 62); // back button
                Assert.False(s.Preview_ModeToggle);

                // Pressing and dragging off a button cancels the click
                var open = At(200, 230);
                s.Preview_Main_CursorX = 0; s.Preview_Main_CursorY = 1;
                win.MouseDown(open, MouseButton.Left);
                Assert.Equal(PreviewTarget.Open, PreviewInteraction.Shared.Pressed);
                win.MouseMove(At(40, 100));
                win.MouseUp(At(40, 100), MouseButton.Left);
                Assert.False(s.Preview_ModeToggle);
                Assert.Equal(PreviewTarget.None, PreviewInteraction.Shared.Pressed);

                win.Close();
            }
        }

        [AvaloniaFact]
        public void InteractiveStatesRenderForScreenshots()
        {
            var dir = Environment.GetEnvironmentVariable("UMAMI_SHOT_DIR");
            if (string.IsNullOrEmpty(dir))
                return;
            Directory.CreateDirectory(dir);
            using (new SettingsGuard())
            {
                var s = Settings.Default;
                var theme = NewTheme();

                Action<string, PreviewInteraction> shot = (name, ia) =>
                    File.WriteAllBytes(Path.Combine(dir, "ia_" + name + ".png"), PreviewExporter.RenderPng(theme, PreviewKind.Both, ia));

                PreviewInteraction Fresh()
                {
                    var ia = new PreviewInteraction();
                    ia.Tick(0, s, theme);
                    ia.Tick(1, s, theme);
                    return ia;
                }

                shot("idle", Fresh());
                var open = Fresh(); open.Press(PreviewTarget.Open, -1, -1); shot("open_pressed", open);
                var close = Fresh(); close.Press(PreviewTarget.Close, -1, -1); shot("close_pressed", close);
                var arrow = Fresh(); arrow.Press(PreviewTarget.Arrow, -1, -1); shot("arrow_pressed", arrow);
                var cell = Fresh(); cell.Press(PreviewTarget.Cell, 2, 2); shot("cell_pressed", cell);

                var half = Fresh();
                s.Preview_ModeToggle = true;
                half.Tick(1.1, s, theme);
                shot("folder_half", half);
                half.Tick(2.0, s, theme);
                shot("folder_open", half);
                var back = half; back.Press(PreviewTarget.Back, -1, -1); shot("back_pressed", back);
                s.Preview_ModeToggle = false;

                var lid = Fresh(); lid.LidOpenTarget = false; lid.Tick(1.14, s, theme); shot("lid_half", lid);
                lid.Tick(2.0, s, theme); shot("lid_closed", lid);

                var slide = Fresh();
                s.Preview_Main_CursorX = 4; s.Preview_Main_CursorY = 0;
                slide.Tick(1.05, s, theme); slide.Tick(1.1, s, theme);
                shot("cursor_sliding", slide);
            }
        }

        [AvaloniaTheory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void WindowRendersInBothThemeVariants(string variant)
        {
            var dir = Environment.GetEnvironmentVariable("UMAMI_SHOT_DIR");
            Avalonia.Application.Current.RequestedThemeVariant = variant == "Light" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(20); }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = win.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); frame.Save(Path.Combine(dir, "variant_" + variant + ".png")); }
            win.Close();
            Avalonia.Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        }

        private static ThemeEditor.App.Themes.ThemeViewModel NewTheme()
        {
            var win = new MainWindow();
            win.Show();
            win.NewThemeCommandWrapper.Command.Execute(null);
            for (var i = 0; i < 100 && win.ViewModel == null; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            return win.ViewModel;
        }
    }
}
