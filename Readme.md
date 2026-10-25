# Umami 3DS Theme Editor

A cross-platform theme editor for the **Nintendo 3DS HOME Menu** – now with **BCSTM audio conversion**.

Runs on **Windows, Linux and macOS** (x64 *and* ARM64) on **.NET 8** – no .NET Framework, no Windows required.

This is a fork of [usagirei/3DS-Theme-Editor](https://github.com/usagirei/3DS-Theme-Editor) (MIT). The
original WPF/.NET Framework 4.5 application was ported to [Avalonia](https://avaloniaui.net/) and
[SkiaSharp](https://github.com/mono/SkiaSharp); the theme format library is unchanged apart from the .NET 8 move.

![Editor](docs/editor.png) ![Preview](docs/preview.png)

## Download

Grab the package for your system from the [Releases](https://github.com/BakuretsuBaka/Umami-3DS-Theme-Editor/releases) page.
All packages are self-contained – nothing else has to be installed.

| System | File |
|---|---|
| Windows 10/11 (Intel/AMD) | `…-win-x64.zip` |
| Windows 11 on ARM | `…-win-arm64.zip` |
| Linux (x86-64) | `…-linux-x64.tar.gz` |
| Linux (ARM64, e.g. Raspberry Pi 4/5) | `…-linux-arm64.tar.gz` |
| macOS Intel | `…-osx-x64.dmg` |
| macOS Apple Silicon | `…-osx-arm64.dmg` |

* **macOS:** open the `.dmg` and drag the app to *Applications*. The app is ad-hoc signed but not notarized (no paid Apple developer account),
  so macOS may say it is "damaged" or from an unidentified developer. Fix it once with
  `xattr -dr com.apple.quarantine "/Applications/Umami 3DS Theme Editor.app"` or right-click the app → *Open*.
* **Linux:** extract, then run `./UmamiThemeEditor`. Needs a desktop with X11 or XWayland (and `libfontconfig`).
  For the *screen* eyedropper one of `grim`, `gnome-screenshot`, `spectacle`, `maim`, `scrot` or ImageMagick `import` should be installed
  (otherwise picking is limited to the editor window).

## Features

* **Image palette** – pick colours from the (up to) 20 most used colours of the top/bottom background images
* **Interactive live preview** – click an icon to move the cursor, click it again (or press *Open*) to open a folder or launch it, use the arrow button to flip
  wallpaper pages, *Close*/back to leave a folder. Buttons show their pressed state while you hold them, the cursor slides and folders open with a short
  transition, and the **theme's own sound effects** play (cursor, launch, folder, close, page flips, lid). Arrow keys, Enter and Esc work too, and there is a
  *Lid* button. Animated wallpapers are toggleable.
* **Theme ZIP** – *File → Export Theme ZIP* writes a Theme Plaza / Themezer ready package (theme, music, icon, a fresh preview and name);
  open or drop such a ZIP to unpack and load it
* **Exports preview.png** when saving
* Remembers the window size and the folder of the last file dialog
* **Image dithering / edge bleed** for textures, **SMDH** (`info.smdh`) generation
* All top/bottom render modes, frame types, folder & icon colours and textures
* **Theme sender** – push the theme to CHMM2 over the network
* **BGM / BCSTM conversion (new)** – *Music → Import Audio*: converts **WAV, MP3, OGG, BCSTM, BFSTM, BRSTM, BCWAV, DSP, IDSP, ADX, HPS** to a theme-ready
  `BGM.BCSTM` with DSP-ADPCM (or PCM16), mono mix-down, resampling and loop points. Checks the 3 MB HOME-menu limit. Export the music as WAV or BCSTM again.
* **CWAV sound-effect manager** – now converts WAV/MP3/OGG to CWAV itself (no external `ctr_WaveConverter`/`vgmstream` needed)
* **In-app audio playback** on every platform (OpenAL Soft)
* **Languages** – English, Deutsch, Español, Français, Italiano, Nederlands, Polski, Português (Brasil), Svenska, Türkçe, Русский, 日本語, 한국어, 简体中文, 繁體中文
  (*Language* menu; takes effect on the next start)

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build Umami3DSThemeEditor.sln
dotnet test  Umami3DSThemeEditor.sln
dotnet run --project src/ThemeEditor.App
# all platforms:
bash packaging/package.sh 2.0.0
```

Layout:

| Path | Contents |
|---|---|
| `src/ThemeEditor.Common` | Theme/SMDH/texture/LZ11 library, `Audio/BcstmConverter`, `Audio/CwavConverter` |
| `src/ThemeEditor.App` | Avalonia UI (previews are rendered with SkiaSharp in `Previews/`) |
| `tests/ThemeEditor.Tests` | xUnit tests incl. headless UI renders |
| `tools/` | `make_resx.py` / `gen_resources.py` – localization pipeline (`tools/i18n/*.txt`) |

### Adding or improving a translation

Edit/create `tools/i18n/<culture>.txt` (`Key=Value`, `\n` for a line break; missing keys fall back to English), then

```bash
python3 tools/make_resx.py && python3 tools/gen_resources.py
```

New languages must also be added to `MainWindow.Languages`. Most translations of this edition were machine-assisted –
corrections from native speakers are very welcome.

## Notes on the port

* WPF pixel shader (`WarpEffect`) and `Viewport3D` do not exist outside Windows, so the animated top-screen pattern is a CPU port of the shader
  and the 3D folder is drawn by a small software rasteriser.
* The original in-place self-updater was removed; the editor only *notifies* about new releases.
* Settings live in `~/.config/UmamiThemeEditor/settings.json` (Linux/macOS) or `%AppData%\UmamiThemeEditor\settings.json` (Windows).

## Credits

* **Usagirei** – original 3DS Theme Editor (MIT)
* **Reisyukaku** – [YATA] (LZ11, texture decoding) · **kwsch** – [pk3DS] · **Barubary** – [DSDecmp]
* **3dbrew** – [file format documentation](https://www.3dbrew.org/wiki/)
* **VGAudio** (Alex Barney, MIT) – BCSTM/DSP-ADPCM · **NLayer**, **NVorbis** – MP3/OGG decoding
* **Avalonia**, **SkiaSharp**, **OpenAL Soft** (via Silk.NET)

[YATA]:https://github.com/Reisyukaku/YATA
[DSDecmp]:https://github.com/Barubary/dsdecmp/tree/master/CSharp/DSDecmp
[pk3DS]:https://github.com/kwsch/pk3DS/blob/master/pk3DS/3DS/LZSS.cs

## License

MIT – see [LICENSE](LICENSE). Original © 2016 Usagirei; modifications © 2026 Umami contributors.
