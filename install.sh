#!/bin/bash
# 샤샤룽 다운로더 — macOS curl 설치 스크립트
#
#   curl -fsSL https://raw.githubusercontent.com/Yuhyeon0516/Multiplatform.Downloader/main/install.sh | bash
#
# curl 경로는 quarantine 속성이 붙지 않아 Gatekeeper 경고 없이 실행된다.
# (브라우저로 .dmg를 받은 경우엔 시스템 설정 → 개인정보 보호 및 보안 → "그래도 열기" 필요)
set -euo pipefail

REPO="Yuhyeon0516/Multiplatform.Downloader"
APP_NAME="샤샤룽 다운로더.app"

# 1) 아키텍처 자동 감지
case "$(uname -m)" in
  arm64)  ASSET="ShyshyroongDownloader-macos-arm64.tar.gz" ;;
  x86_64) ASSET="ShyshyroongDownloader-macos-x64.tar.gz" ;;
  *) echo "지원하지 않는 아키텍처: $(uname -m)" >&2; exit 1 ;;
esac

# 2) 설치 위치 (/Applications 쓰기 불가 시 ~/Applications 폴백)
DEST="/Applications"
if [ ! -w "$DEST" ]; then
  DEST="$HOME/Applications"
  mkdir -p "$DEST"
fi

echo "▶ 다운로드: $ASSET"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
curl -fL --progress-bar "https://github.com/$REPO/releases/latest/download/$ASSET" -o "$TMP/app.tar.gz"

# 3) 기존 버전 제거 후 설치 (덮어쓰기로 인한 파일 혼합 방지)
if [ -d "$DEST/$APP_NAME" ]; then
  echo "▶ 기존 버전 제거"
  rm -rf "$DEST/$APP_NAME"
fi
tar -xzf "$TMP/app.tar.gz" -C "$DEST"

echo "✅ 설치 완료: $DEST/$APP_NAME"
open "$DEST/$APP_NAME"
