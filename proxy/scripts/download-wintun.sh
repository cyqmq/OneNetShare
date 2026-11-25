#!/bin/sh
set -eu

URL="https://www.wintun.net/builds/wintun-0.14.1.zip"
ZIP_SHA256="07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51"
DLL_SHA256="d694fa46ab4cfebcb2632d094c7aa97278eef2f8052438621766d863ae98a931"

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
BUILD_DIR="$SCRIPT_DIR/../build"
ZIP_PATH="$BUILD_DIR/wintun-0.14.1.zip"
OUT="${1:-$BUILD_DIR/wintun.dll}"

mkdir -p "$BUILD_DIR"
curl -fsSL "$URL" -o "$ZIP_PATH"
echo "$ZIP_SHA256  $ZIP_PATH" | sha256sum -c -
unzip -p "$ZIP_PATH" wintun/bin/x86/wintun.dll > "$OUT"
echo "$DLL_SHA256  $OUT" | sha256sum -c -
echo "Wintun x86 DLL: $OUT"
