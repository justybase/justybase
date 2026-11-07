#!/usr/bin/env bash
set -euo pipefail

VERSION=""
OUTPUT_ROOT="artifacts"
SLIM=0
for arg in "$@"; do
  case "$arg" in
    --slim) SLIM=1 ;;
    *)
      if [[ -z "$VERSION" ]]; then
        VERSION="$arg"
      else
        OUTPUT_ROOT="$arg"
      fi
      ;;
  esac
done
if [[ -z "$VERSION" ]]; then
  echo "Usage: ./publishLinux.sh <version> [output-root] [--slim]" >&2
  exit 1
fi
# Full: all database drivers + AI + animated splash.
# Slim: Netezza + SQLite + DB2 + Postgres + MySQL, no AI, static splash.
if [[ "$SLIM" == "1" ]]; then
  VARIANT="self-contained-slim"
  PLUGIN_FLAGS=(-p:EnableDb2Plugin=true -p:EnableAccessPlugin=false -p:EnablePostgresPlugin=true -p:EnableOraclePlugin=false -p:EnableMySqlPlugin=true -p:EnableDuckDbPlugin=false -p:EnableExcelPlugin=false -p:EnableAi=false -p:EnableLottie=false)
else
  VARIANT="self-contained"
  PLUGIN_FLAGS=(-p:EnableDb2Plugin=true -p:EnableAccessPlugin=true -p:EnablePostgresPlugin=true -p:EnableOraclePlugin=true -p:EnableMySqlPlugin=true -p:EnableDuckDbPlugin=true -p:EnableExcelPlugin=true -p:EnableAi=true -p:EnableLottie=true)
fi
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 1; }
command -v zip >/dev/null || { echo "zip is required" >&2; exit 1; }

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/source/JustyBase/JustyBase.csproj"
PUBLISH_DIR="$ROOT/$OUTPUT_ROOT/work/linux-x64-$VARIANT"
ZIP_PATH="$ROOT/$OUTPUT_ROOT/JustyBase-${VERSION}-linux-x64-$VARIANT.zip"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR" "$(dirname "$ZIP_PATH")"
dotnet publish "$PROJECT" -r linux-x64 -c Release -f net10.0 \
  "${PLUGIN_FLAGS[@]}" -p:PublishAot=false \
  -p:CopilotSkipCliDownload=true \
  -p:PublishReadyToRun=true -p:PublishTrimmed=false --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false \
  -p:UseSharedCompilation=false \
  -p:Version="$VERSION" -o "$PUBLISH_DIR"
RUNTIMES_DIR="$PUBLISH_DIR/runtimes"
if [[ -d "$RUNTIMES_DIR" ]]; then
  find "$RUNTIMES_DIR" -mindepth 1 -maxdepth 1 -type d ! -name linux-x64 -exec rm -rf {} +
fi
find "$PUBLISH_DIR" -type f \( -name '*.pdb' -o -name '*.dbg' \) -delete
(cd "$PUBLISH_DIR" && zip -q -r "$ZIP_PATH" .)
test -s "$ZIP_PATH"
echo "Created $ZIP_PATH"
