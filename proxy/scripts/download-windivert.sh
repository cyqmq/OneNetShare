#!/bin/sh
set -eu

URL="https://github.com/basil00/WinDivert/releases/download/v2.2.2/WinDivert-2.2.2-A.zip"
ZIP_SHA256="63cb41763bb4b20f600b6de04e991a9c2be73279e317d4d82f237b150c5f3f15"
DLL_SHA256="a321649090c21aaa7529ce5d019d242b1d5f2a2aff04bc3224db409641604a83"
SYS32_SHA256="2f43f4251be4d72dd56c91bf6cce475d379eb9ba6c4dda2be3022ea633d5e807"
SYS64_SHA256="8da085332782708d8767bcace5327a6ec7283c17cfb85e40b03cd2323a90ddc2"

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
BUILD_DIR="$SCRIPT_DIR/../build"
ZIP_PATH="$BUILD_DIR/WinDivert-2.2.2-A.zip"
OUT_DIR="${1:-$BUILD_DIR/windivert}"

mkdir -p "$OUT_DIR"
curl -fsSL "$URL" -o "$ZIP_PATH"
echo "$ZIP_SHA256  $ZIP_PATH" | sha256sum -c -
unzip -o -p "$ZIP_PATH" WinDivert-2.2.2-A/x86/WinDivert.dll > "$OUT_DIR/WinDivert.dll"
unzip -o -p "$ZIP_PATH" WinDivert-2.2.2-A/x86/WinDivert32.sys > "$OUT_DIR/WinDivert32.sys"
unzip -o -p "$ZIP_PATH" WinDivert-2.2.2-A/x86/WinDivert64.sys > "$OUT_DIR/WinDivert64.sys"
unzip -o -p "$ZIP_PATH" WinDivert-2.2.2-A/LICENSE > "$OUT_DIR/LICENSE"
echo "$DLL_SHA256  $OUT_DIR/WinDivert.dll" | sha256sum -c -
echo "$SYS32_SHA256  $OUT_DIR/WinDivert32.sys" | sha256sum -c -
echo "$SYS64_SHA256  $OUT_DIR/WinDivert64.sys" | sha256sum -c -
echo "WinDivert 2.2.2 x86 DLL 与双架构驱动: $OUT_DIR"
