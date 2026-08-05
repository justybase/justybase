#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-}"
OUTPUT_ROOT="${2:-artifacts}"
VARIANT="${3:-aot-netezza}"
if [[ -z "$VERSION" ]]; then
  echo "Usage: ./publishLinux.sh <version> [output-root] [aot-netezza|self-contained-netezza-db2]" >&2
  exit 1
fi
case "$VARIANT" in
  aot-netezza) ENABLE_AOT=true; ENABLE_DB2=false ;;
  self-contained-netezza-db2) ENABLE_AOT=false; ENABLE_DB2=true ;;
  *) echo "Unsupported publish variant: $VARIANT" >&2; exit 1 ;;
esac
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 1; }
command -v zip >/dev/null || { echo "zip is required" >&2; exit 1; }

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/source/JustyBase/JustyBase.csproj"
PUBLISH_DIR="$ROOT/$OUTPUT_ROOT/work/linux-x64-$VARIANT"
ZIP_PATH="$ROOT/$OUTPUT_ROOT/JustyBase-${VERSION}-linux-x64-$VARIANT.zip"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR" "$(dirname "$ZIP_PATH")"
dotnet publish "$PROJECT" -r linux-x64 -c Release -f net10.0 \
  -p:EnableAOT="$ENABLE_AOT" -p:EnableDb2Plugin="$ENABLE_DB2" \
  -p:PublishAot="$ENABLE_AOT" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false \
  -p:UseSharedCompilation=false \
  -p:UseLocalJustyBaseLibraries=false \
  -p:Version="$VERSION" -o "$PUBLISH_DIR"
RUNTIMES_DIR="$PUBLISH_DIR/runtimes"
if [[ -d "$RUNTIMES_DIR" ]]; then
  find "$RUNTIMES_DIR" -mindepth 1 -maxdepth 1 -type d ! -name linux-x64 -exec rm -rf {} +
fi
find "$PUBLISH_DIR" -type f \( -name '*.pdb' -o -name '*.dbg' \) -delete
(cd "$PUBLISH_DIR" && zip -q -r "$ZIP_PATH" .)
test -s "$ZIP_PATH"
echo "Created $ZIP_PATH"
