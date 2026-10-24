#!/usr/bin/env bash
# Builds self-contained, single-folder packages for every supported platform into ./dist
# Usage: packaging/package.sh [version]
set -euo pipefail
VERSION="${1:-2.0.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/ThemeEditor.App/ThemeEditor.App.csproj"
OUT="$ROOT/dist"
rm -rf "$OUT" "$ROOT/publish"
mkdir -p "$OUT"

for RID in win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
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
      (cd "$ROOT/publish/$RID-bundle" && zip -qr "$OUT/$NAME.zip" "Umami 3DS Theme Editor.app")
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
