#!/bin/bash
# 샤샤룽 다운로더 — macOS .app 번들 + .tar.gz + .dmg + .sha256 빌드
#
# 사용법: ./make-app.sh <osx-arm64|osx-x64>
# 전제:  Multiplatform-Downloader.Avalonia/tools/ 에 해당 아키텍처의
#        yt-dlp/ffmpeg/ffprobe/deno 바이너리가 배치되어 있어야 한다(CI가 수행).
# 산출:  dist/ShyshyroongDownloader-macos-{arm64|x64}.tar.gz (+.sha256), 동명 .dmg
#
# 서명: ad-hoc(codesign -s -) — Apple Silicon 실행 필수 요건. Developer ID가 생기면
#       CODESIGN_IDENTITY 환경변수로 교체하고 notarytool 단계를 추가한다.
set -euo pipefail

RID="${1:?사용법: make-app.sh <osx-arm64|osx-x64>}"
case "$RID" in
  osx-arm64) ARCH="arm64" ;;
  osx-x64)   ARCH="x64" ;;
  *) echo "[ERROR] 지원하지 않는 RID: $RID" >&2; exit 1 ;;
esac

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="$REPO_ROOT/Multiplatform-Downloader.Avalonia/Multiplatform-Downloader.Avalonia.csproj"
APP_NAME="샤샤룽 다운로더.app"
BUNDLE_ID_SIGN="${CODESIGN_IDENTITY:--}"   # 기본 ad-hoc

VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$PROJECT")
[ -n "$VERSION" ] || { echo "[ERROR] csproj에서 버전을 읽지 못했습니다" >&2; exit 1; }

PUBLISH="$SCRIPT_DIR/publish/$RID"
DIST="$SCRIPT_DIR/dist"
STAGE="$DIST/$RID"
APP="$STAGE/$APP_NAME"

echo "[1/6] dotnet publish ($RID, self-contained, v$VERSION)"
rm -rf "$PUBLISH"
dotnet publish "$PROJECT" -c Release -r "$RID" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false -o "$PUBLISH"

echo "[2/6] .app 번들 구성"
rm -rf "$STAGE"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"
sed "s/__VERSION__/$VERSION/g" "$SCRIPT_DIR/Info.plist.template" > "$APP/Contents/Info.plist"

echo "[3/6] 아이콘(.icns) 생성"
ICONSET="$STAGE/app.iconset"
mkdir -p "$ICONSET"
SRC_PNG="$REPO_ROOT/Multiplatform-Downloader.Avalonia/Assets/app.png"
for size in 16 32 64 128 256 512; do
  sips -z $size $size "$SRC_PNG" --out "$ICONSET/icon_${size}x${size}.png" > /dev/null
  sips -z $((size*2)) $((size*2)) "$SRC_PNG" --out "$ICONSET/icon_${size}x${size}@2x.png" > /dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/app.icns"
rm -rf "$ICONSET"

echo "[4/6] 서명 (identity: $BUNDLE_ID_SIGN)"
# 번들 내 모든 실행물(tools의 yt-dlp/ffmpeg 등 포함)까지 — Apple Silicon 실행 필수
codesign --force --deep -s "$BUNDLE_ID_SIGN" "$APP"
codesign --verify --deep "$APP"

echo "[5/6] tar.gz + sha256"
mkdir -p "$DIST"
TAR="$DIST/ShyshyroongDownloader-macos-$ARCH.tar.gz"
rm -f "$TAR" "$TAR.sha256"
tar -C "$STAGE" -czf "$TAR" "$APP_NAME"
( cd "$DIST" && shasum -a 256 "$(basename "$TAR")" > "$(basename "$TAR").sha256" )

echo "[6/6] dmg"
DMG="$DIST/ShyshyroongDownloader-macos-$ARCH.dmg"
DMG_STAGE="$STAGE/dmg"
rm -f "$DMG"
mkdir -p "$DMG_STAGE"
cp -R "$APP" "$DMG_STAGE/"
ln -sf /Applications "$DMG_STAGE/Applications"
hdiutil create -quiet -volname "샤샤룽 다운로더" -srcfolder "$DMG_STAGE" -ov -format UDZO "$DMG"
rm -rf "$DMG_STAGE"

echo
echo "=== 완료 ==="
ls -lh "$TAR" "$TAR.sha256" "$DMG"
