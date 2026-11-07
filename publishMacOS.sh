#!/usr/bin/env bash
set -euo pipefail

VERSION=""
RID=""
OUTPUT_ROOT="artifacts"
SLIM=0
for arg in "$@"; do
  case "$arg" in
    --slim) SLIM=1 ;;
    *)
      if [[ -z "$VERSION" ]]; then
        VERSION="$arg"
      elif [[ -z "$RID" ]]; then
        RID="$arg"
      else
        OUTPUT_ROOT="$arg"
      fi
      ;;
  esac
done
if [[ -z "$VERSION" || -z "$RID" ]]; then
  echo "Usage: ./publishMacOS.sh <version> <osx-arm64|osx-x64> [output-root] [--slim]" >&2
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
# Full: all database drivers + AI + animated splash.
# Slim: Netezza + SQLite + DB2 only, no AI, static splash.
if [[ "$SLIM" == "1" ]]; then
  SELF_CONTAINED_VARIANT="self-contained-slim"
  PLUGIN_FLAGS=(-p:EnableDb2Plugin=true -p:EnableAccessPlugin=false -p:EnablePostgresPlugin=false -p:EnableOraclePlugin=false -p:EnableMySqlPlugin=false -p:EnableDuckDbPlugin=false -p:EnableExcelPlugin=false -p:EnableAi=false -p:EnableLottie=false)
else
  SELF_CONTAINED_VARIANT="self-contained"
  PLUGIN_FLAGS=(-p:EnableDb2Plugin=true -p:EnableAccessPlugin=true -p:EnablePostgresPlugin=true -p:EnableOraclePlugin=true -p:EnableMySqlPlugin=true -p:EnableDuckDbPlugin=true -p:EnableExcelPlugin=true -p:EnableAi=true -p:EnableLottie=true)
fi
PUBLISH_DIR="$ROOT/$OUTPUT_ROOT/work/$RID"
ZIP_PATH="$ROOT/$OUTPUT_ROOT/JustyBase-${VERSION}-macos-${ARCH}-${SELF_CONTAINED_VARIANT}.zip"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR" "$(dirname "$ZIP_PATH")"
dotnet publish "$PROJECT" -r "$RID" -c Release -f net10.0 \
  "${PLUGIN_FLAGS[@]}" -p:PublishAot=false \
  -p:CopilotSkipCliDownload=true \
  -p:PublishReadyToRun=true -p:PublishTrimmed=false \
  --self-contained true -p:DebugType=None -p:DebugSymbols=false \
  -p:UseSharedCompilation=false \
  -p:Version="$VERSION" -o "$PUBLISH_DIR"
RUNTIMES_DIR="$PUBLISH_DIR/runtimes"
if [[ -d "$RUNTIMES_DIR" ]]; then
  find "$RUNTIMES_DIR" -mindepth 1 -maxdepth 1 -type d ! -name "$RID" -exec rm -rf {} +
fi
find "$PUBLISH_DIR" -type f \( -name '*.pdb' -o -name '*.dbg' \) -delete
(cd "$PUBLISH_DIR" && zip -q -r "$ZIP_PATH" .)
test -s "$ZIP_PATH"
echo "Created $ZIP_PATH"
