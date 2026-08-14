#!/usr/bin/env bash
# Publish ArtSync as three Devart-compatible names for Linux/macOS.
# Usage:
#   ./scripts/publish.sh [runtime] [configuration] [output-root]
# Examples:
#   ./scripts/publish.sh                          # linux-x64, Release
#   ./scripts/publish.sh osx-arm64               # macOS Apple Silicon
#   ./scripts/publish.sh linux-x64 Debug         # debug build

set -euo pipefail

RUNTIME="${1:-linux-x64}"
CONFIGURATION="${2:-Release}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_ROOT="${3:-$REPO_ROOT/publish}"
OUT_DIR="$OUTPUT_ROOT/artsync-$RUNTIME"

CLI_PROJECT="$REPO_ROOT/src/ArtSync.Cli/ArtSync.Cli.csproj"

echo "Publishing ArtSync.Cli → $OUT_DIR"

dotnet publish "$CLI_PROJECT" \
    -r "$RUNTIME" \
    -c "$CONFIGURATION" \
    -o "$OUT_DIR" \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:IncludeNativeLibrariesForSelfExtract=true

# Locate the built binary (no extension on Linux/macOS)
SRC_BIN=""
for candidate in "$OUT_DIR/ArtSync.Cli" "$OUT_DIR/ArtSync.Cli.exe"; do
    if [[ -f "$candidate" ]]; then
        SRC_BIN="$candidate"
        break
    fi
done
if [[ -z "$SRC_BIN" ]]; then
    echo "ERROR: Cannot find ArtSync.Cli binary in $OUT_DIR" >&2
    exit 1
fi

EXT=""
[[ "$SRC_BIN" == *.exe ]] && EXT=".exe"

for NAME in schemacompare datacompare dbforgesql; do
    DEST="$OUT_DIR/$NAME$EXT"
    cp "$SRC_BIN" "$DEST"
    chmod +x "$DEST"
    echo "  Wrote $NAME$EXT"
done

echo ""
echo "Published to: $OUT_DIR"
ls -lh "$OUT_DIR"/{schemacompare,datacompare,dbforgesql}$EXT 2>/dev/null || ls -lh "$OUT_DIR"
