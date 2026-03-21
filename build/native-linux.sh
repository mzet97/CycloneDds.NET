#!/bin/bash
# ===================================================================================
# build/native-linux.sh
#
# PURPOSE:
#   Compiles the native (C/C++) Cyclone DDS submodule and copies the resulting
#   binaries (shared libraries, tools, etc.) to the local 'artifacts' directory.
#   This is a prerequisite for running managed tests or packing the NuGet package.
#
# USAGE:
#   ./build/native-linux.sh [Release|Debug]
# ===================================================================================

set -euo pipefail

CONFIGURATION="${1:-Release}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# Use local cyclonedds submodule if it exists and has CMakeLists.txt
SOURCE_DIR="$REPO_ROOT/cyclonedds"
if [ ! -f "$SOURCE_DIR/CMakeLists.txt" ]; then
    # Try parent directory (for users who have cyclonedds in parent folder)
    SOURCE_DIR="$(cd "$REPO_ROOT/.." && pwd)/cyclonedds"
fi

if [ ! -f "$SOURCE_DIR/CMakeLists.txt" ]; then
    echo "ERROR: cyclonedds source directory not found." >&2
    echo "Expected at: $REPO_ROOT/cyclonedds or ../cyclonedds" >&2
    echo "Please clone Eclipse CycloneDDS to one of these locations:" >&2
    echo "  git clone --recurse-submodules https://github.com/eclipse-cyclonedds/cyclonedds.git" >&2
    exit 1
fi
BUILD_DIR="$REPO_ROOT/build/native-linux"
INSTALL_DIR="$REPO_ROOT/artifacts/native-install"
ARTIFACTS_DIR="$REPO_ROOT/artifacts/native/linux-x64"

echo "============================================================"
echo "  Building Native CycloneDDS ($CONFIGURATION) - Linux x64"
echo "============================================================"

# Ensure directories exist
mkdir -p "$BUILD_DIR" "$INSTALL_DIR" "$ARTIFACTS_DIR"

# Check for CMake
if ! command -v cmake &> /dev/null; then
    echo "ERROR: CMake is not installed or not in PATH."
    echo "Install with: sudo apt-get install build-essential cmake"
    exit 1
fi

# Check for compiler
if ! command -v gcc &> /dev/null && ! command -v clang &> /dev/null; then
    echo "ERROR: No C compiler found (gcc or clang)."
    echo "Install with: sudo apt-get install build-essential"
    exit 1
fi

# Configure CMake
echo ""
echo "[1/3] Configuring CMake..." >&2
echo "  Source: $SOURCE_DIR" >&2
echo "  Build:  $BUILD_DIR" >&2

# Common CMake arguments
CMAKE_ARGS=(
    -S "$SOURCE_DIR"
    -B "$BUILD_DIR"
    -DCMAKE_BUILD_TYPE="$CONFIGURATION"
    -DCMAKE_INSTALL_PREFIX="$INSTALL_DIR"
    -DBUILD_SHARED_LIBS=ON
    -DBUILD_IDLC=ON
    -DBUILD_TESTING=OFF
    -DBUILD_EXAMPLES=OFF
    -DENABLE_SSL=OFF
    -DENABLE_SHM=OFF
    -DENABLE_SECURITY=OFF
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON
)

cmake "${CMAKE_ARGS[@]}"

if [ $? -ne 0 ]; then
    echo "ERROR: CMake configuration failed." >&2
    exit 1
fi

echo "  [+] CMake configured successfully" >&2

# Build & Install
echo ""
echo "[2/3] Building & Installing..." >&2
cmake --build "$BUILD_DIR" --config "$CONFIGURATION" -j"$(nproc)"

if [ $? -ne 0 ]; then
    echo "ERROR: Build failed." >&2
    exit 1
fi

cmake --install "$BUILD_DIR" --config "$CONFIGURATION"

if [ $? -ne 0 ]; then
    echo "ERROR: Install failed." >&2
    exit 1
fi

echo "  [+] Build and install complete" >&2

# Copy Artifacts
echo ""
echo "[3/3] Copying artifacts to $ARTIFACTS_DIR..." >&2

BIN_DIR="$INSTALL_DIR/bin"
LIB_DIR="$INSTALL_DIR/lib"

# Required shared libraries (use .so* to match any version)
REQUIRED_LIBS=(
    "libddsc.so*"
    "libcycloneddsidl.so*"
    "libcycloneddsidlc.so*"
    "libcycloneddsidljson.so*"
)

# Copy shared libraries - copy ALL .so files
for pattern in "${REQUIRED_LIBS[@]}"; do
    for f in "$LIB_DIR"/$pattern; do
        if [ -f "$f" ]; then
            cp -P "$f" "$ARTIFACTS_DIR/"
            echo "  [+] Copied $(basename "$f")" >&2
        fi
    done
done

# Required binaries
REQUIRED_BINS=(
    "idlc"
)

for bin in "${REQUIRED_BINS[@]}"; do
    if [ -f "$BIN_DIR/$bin" ]; then
        cp "$BIN_DIR/$bin" "$ARTIFACTS_DIR/"
        chmod +x "$ARTIFACTS_DIR/$bin"
        echo "  [+] Copied $bin (executable)" >&2
    else
        echo "  [-] ERROR: $bin not found in $BIN_DIR" >&2
        exit 1
    fi
done

echo ""
echo "Native build complete. Artifacts in: $ARTIFACTS_DIR" >&2
echo "" >&2

# List artifacts
echo "Artifacts:" >&2
ls -la "$ARTIFACTS_DIR" >&2
