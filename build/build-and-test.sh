#!/bin/bash
# ===================================================================================
# build/build-and-test.sh
#
# PURPOSE:
#   The primary developer entry point for Linux.
#   1. Checks for native artifacts (builds them if missing).
#   2. Builds the entire Managed Solution (CycloneDDS.NET.sln).
#   3. Runs all tests in the solution.
#
# USAGE:
#   ./build/build-and-test.sh [Release|Debug] [--skip-native]
# ===================================================================================

set -euo pipefail

CONFIGURATION="${1:-Release}"
SKIP_NATIVE="false"

# Parse arguments
for arg in "$@"; do
    case $arg in
        --skip-native)
            SKIP_NATIVE="true"
            ;;
        Release|Debug)
            CONFIGURATION="$arg"
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS_DIR="$REPO_ROOT/artifacts/native/linux-x64"

echo "============================================================"
echo "  CycloneDDS.NET: Build & Test ($CONFIGURATION) - Linux"
echo "============================================================"

# 0. Check for Native Artifacts
# Check for any libddsc.so* file (handles symlinks)
NATIVE_DLL=$(ls "$ARTIFACTS_DIR"/libddsc.so* 2>/dev/null | head -1)

if [ -z "$NATIVE_DLL" ]; then
    if [ "$SKIP_NATIVE" = "true" ]; then
        echo "WARNING: Native artifacts not found at $NATIVE_DLL. Tests may fail." >&2
    else
        echo ""
        echo "[0/3] Native artifacts missing. Building native..." >&2
        "$SCRIPT_DIR/native-linux.sh" "$CONFIGURATION"
    fi
elif [ "$SKIP_NATIVE" = "false" ]; then
    echo "Native artifacts found. Skipping native build." >&2
fi

# 1. Set LD_LIBRARY_PATH for runtime
export LD_LIBRARY_PATH="${ARTIFACTS_DIR}:${LD_LIBRARY_PATH:-}"
echo "LD_LIBRARY_PATH=$LD_LIBRARY_PATH" >&2

# 2. Build Solution (use Core.slnf to exclude examples)
echo ""
echo "[1/3] Building Solution (Managed)..." >&2
dotnet build "$REPO_ROOT/CycloneDDS.NET.Core.slnf" -c "$CONFIGURATION"

if [ $? -ne 0 ]; then
    echo "ERROR: Solution build failed." >&2
    exit 1
fi

# 3. Run Tests
echo ""
echo "[2/3] Executing Test Suite (All Projects)..." >&2

TEST_ARGS=(
    "test"
    "$REPO_ROOT/CycloneDDS.NET.Core.slnf"
    "-c" "$CONFIGURATION"
    "--no-build"
    "--logger"
    "console;verbosity=normal"
)

# Check if a filter was provided as last argument
if [ $# -gt 0 ] && [[ "$1" == *Filter* || "$1" == *"*:*"* ]]; then
    TEST_ARGS+=("--filter" "$1")
fi

dotnet "${TEST_ARGS[@]}"

if [ $? -ne 0 ]; then
    echo "ERROR: Tests FAILED." >&2
    exit 1
fi

echo ""
echo "============================================================"
echo "  All Tests Passed Successfully!"
echo "============================================================"
