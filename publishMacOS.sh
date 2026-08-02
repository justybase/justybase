#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-}"
RID="${2:-}"
OUTPUT_ROOT="${3:-artifacts}"
if [[ -z "$VERSION" || -z "$RID" ]]; then
  echo "Usage: ./publishMacOS.sh <version> <osx-arm64|osx-x64> [output-root]" >&2
  exit 1
fi
case "$RID" in
  osx-arm64) ARCH="arm64" ;;
  osx-x64) ARCH="x64" ;;
  *) echo "Unsupported macOS RID: $RID" >&2; exit 1 ;;
esac
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 1; }
command -v zip >/dev/null || { echo "zip is required" >&2; exit 1; }

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/source/JustyBase/JustyBase.csproj"
PUBLISH_DIR="$ROOT/$OUTPUT_ROOT/work/$RID"
ZIP_PATH="$ROOT/$OUTPUT_ROOT/JustyBase-${VERSION}-macos-${ARCH}.zip"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR" "$(dirname "$ZIP_PATH")"
dotnet publish "$PROJECT" -r "$RID" -c Release -f net10.0 \
  -p:EnableAOT=true --self-contained true -p:DebugType=None -p:DebugSymbols=false \
  -p:Version="$VERSION" -o "$PUBLISH_DIR"
find "$PUBLISH_DIR" -type f \( -name '*.pdb' -o -name '*.dbg' \) -delete
(cd "$PUBLISH_DIR" && zip -q -r "$ZIP_PATH" .)
test -s "$ZIP_PATH"
echo "Created $ZIP_PATH"
