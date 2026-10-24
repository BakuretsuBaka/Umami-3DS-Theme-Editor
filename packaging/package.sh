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
      (cd "$DIR" && zip -qr "$OUT/$NAME.zip" .)
      ;;
    *)
      chmod +x "$DIR/UmamiThemeEditor"
      tar -C "$DIR" -czf "$OUT/$NAME.tar.gz" .
      ;;
  esac
done
ls -lh "$OUT"
