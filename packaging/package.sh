#!/usr/bin/env bash
# Builds self-contained, single-folder packages for every supported platform into ./dist
# Usage: packaging/package.sh [version]
set -euo pipefail
VERSION="${1:-2.0.0}"
RIDS="${2:-win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/ThemeEditor.App/ThemeEditor.App.csproj"
OUT="$ROOT/dist"
mkdir -p "$OUT"

# The resource library's ready-made presets are not part of the build: the editor downloads them on request
# (the "library-pack" release), which keeps every package small
rm -rf "$ROOT/library-pack"

for RID in $RIDS; do
  DIR="$ROOT/publish/$RID"
  dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
    -p:PublishSingleFile=false -p:Version="$VERSION" -o "$DIR"
  NAME="Umami-3DS-Theme-Editor-$VERSION-$RID"
  case "$RID" in
    osx-*)
      APP="$ROOT/publish/$RID-bundle/Umami 3DS Theme Editor.app"
      mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
      cp -R "$DIR"/. "$APP/Contents/MacOS/"
      # The library data pack is thousands of data files: as Resources they are only hashed, in Contents/MacOS
      # codesign and Gatekeeper treat each one as code and verification alone takes minutes (the app looks frozen)
      if [ -d "$APP/Contents/MacOS/Library" ]; then mv "$APP/Contents/MacOS/Library" "$APP/Contents/Resources/Library"; fi
      sed "s/@VERSION@/$VERSION/g" "$ROOT/packaging/Info.plist" > "$APP/Contents/Info.plist"
      [ -f "$ROOT/packaging/umami.icns" ] && cp "$ROOT/packaging/umami.icns" "$APP/Contents/Resources/umami.icns"
      chmod +x "$APP/Contents/MacOS/UmamiThemeEditor"
      # Apple Silicon refuses unsigned code, so sign ad-hoc (no developer account needed)
      if command -v codesign >/dev/null; then
        codesign --force --deep --sign - "$APP"
      fi
      if command -v hdiutil >/dev/null; then
        STAGE="$ROOT/publish/$RID-dmg"
        rm -rf "$STAGE"; mkdir -p "$STAGE"
        cp -R "$APP" "$STAGE/"
        ln -s /Applications "$STAGE/Applications"
        hdiutil create -volname "Umami 3DS Theme Editor" -srcfolder "$STAGE" -ov -format UDZO "$OUT/$NAME.dmg"
      else
        (cd "$ROOT/publish/$RID-bundle" && zip -qr "$OUT/$NAME.zip" "Umami 3DS Theme Editor.app")
      fi
      ;;
    win-*)
      # Portable zip, plus a setup.exe where Inno Setup is installed (the Windows release job)
      if command -v zip >/dev/null; then
        (cd "$DIR" && zip -qr "$OUT/$NAME-portable.zip" .)
      else
        (cd "$DIR" && 7z a -tzip -bd "$OUT/$NAME-portable.zip" . >/dev/null)
      fi
      ISCC="$(command -v iscc || command -v ISCC || true)"
      [ -z "$ISCC" ] && [ -x "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" ] && ISCC="/c/Program Files (x86)/Inno Setup 6/ISCC.exe"
      if [ -n "$ISCC" ]; then
        # Git Bash would rewrite the /switches into paths
        MSYS_NO_PATHCONV=1 "$ISCC" /Q "/DVersion=$VERSION" "/DArch=${RID#win-}" "/DAppFiles=$(cygpath -w "$DIR")" \
          "/DOutputDir=$(cygpath -w "$OUT")" "/DOutputName=$NAME-setup" "$(cygpath -w "$ROOT/packaging/umami.iss")"
      else
        echo "note: Inno Setup not found, building $RID without an installer"
      fi
      ;;
    *)
      chmod +x "$DIR/UmamiThemeEditor"
      tar -C "$DIR" -czf "$OUT/$NAME.tar.gz" .
      ;;
  esac
done
# A checksum next to every download, so the editor can verify an update without GitHub's rate-limited API
if command -v sha256sum >/dev/null; then SHA="sha256sum"; else SHA="shasum -a 256"; fi
(cd "$OUT" && for f in *; do case "$f" in *.sha256) ;; *) $SHA "$f" > "$f.sha256" ;; esac; done)
ls -lh "$OUT"
