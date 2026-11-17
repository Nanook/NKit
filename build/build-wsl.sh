#!/bin/bash
set -euo pipefail

# =============================================================================
# NKit local Linux build via WSL + Docker.
#
# Handles the two things that differ between local WSL and GitHub CI:
#   1. Docker startup - CI runners have dockerd ready; WSL may need it started.
#   2. Native ext4 copy - the repo lives on a Windows 9p mount (/mnt/d/...).
#      NuGet restore fails on 9p ("NETSDK1064: Package GrindCore not found").
#      The container copies the source tree to native ext4 at /build first.
#
# After that it runs build.sh --legacy-container, which is exactly what
# the GitHub Actions build-legacy.sh runs inside its container.
#
# Usage (called by build-all.ps1, or directly in WSL):
#   build/build-wsl.sh --runtime linux-x64 --apps console,gui,nkds,nkds-ui --version 3.0.0
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

RUNTIME="linux-x64"
CONFIG="Release"
APPS="console,gui,nkds,nkds-ui"
VERSION=""
GC_ZIP_URL=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime)    RUNTIME="$2"; shift 2 ;;
        --config)     CONFIG="$2"; shift 2 ;;
        --apps)       APPS="$2"; shift 2 ;;
        --version)    VERSION="$2"; export VERSION; shift 2 ;;
        --gc-zip-url) GC_ZIP_URL="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

ARCH="${RUNTIME#linux-}"
if [ "$ARCH" != "x64" ] && [ "$ARCH" != "arm64" ]; then
    echo "Unsupported arch: $ARCH (expected x64 or arm64)"; exit 1
fi

IMAGE="nkit-legacy-${ARCH}-builder:latest"
DOCKERFILE="$SCRIPT_DIR/docker/Dockerfile.legacy-$ARCH"
GC_NATIVE_DIR="$REPO_ROOT/.grindcore-native"
OUT_HOST="$REPO_ROOT/build-output"

echo "=== NKit WSL build: $RUNTIME ==="
[ -n "$VERSION" ] && echo "Version:  $VERSION"
echo "Output:   $OUT_HOST"
echo ""

# --- 1. Native Docker daemon --------------------------------------------------
# Docker Desktop drops dot-folders (.nuget, .git) on /mnt bind mounts.
# Pin the native WSL dockerd via DOCKER_HOST to avoid that.
export DOCKER_HOST="unix:///var/run/docker.sock"
unset DOCKER_CONTEXT 2>/dev/null || true

docker_up() { docker info >/dev/null 2>&1; }

if ! docker_up; then
    echo "Native dockerd not responding - starting..."
    if command -v service >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
        sudo service docker start || true
    fi
    if ! docker_up; then
        echo "Launching dockerd (may prompt for sudo password)..."
        sudo bash -c 'nohup dockerd >/var/log/dockerd-nkit.log 2>&1 &' || {
            echo "ERROR: could not start dockerd."
            echo "  Run manually: sudo dockerd >/var/log/dockerd-nkit.log 2>&1 &"
            exit 1
        }
        for _ in $(seq 1 30); do docker_up && break; sleep 1; done
    fi
fi
docker_up || { echo "ERROR: native dockerd not ready (see /var/log/dockerd-nkit.log)"; exit 1; }
echo "Docker server: $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?')"
echo ""

# --- 2. Builder image ---------------------------------------------------------
if [ ! -f "$DOCKERFILE" ]; then
    echo "ERROR: Dockerfile not found: $DOCKERFILE"; exit 1
fi
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    echo "--- Building Docker image $IMAGE ---"
    docker build -t "$IMAGE" -f "$DOCKERFILE" "$SCRIPT_DIR/docker"
    echo ""
fi

# --- 3. GrindCore native libs -------------------------------------------------
# build.sh's inject_grindcore looks for the .a at .grindcore-native/$rid/native/.
# Download the full zip from GitHub so all platform variants are present.
GC_LEGACY="$GC_NATIVE_DIR/${RUNTIME}-legacy/native/libGrindCore.a"
GC_STANDARD="$GC_NATIVE_DIR/$RUNTIME/native/libGrindCore.a"
if [ ! -f "$GC_LEGACY" ] && [ ! -f "$GC_STANDARD" ]; then
    echo "--- Downloading GrindCore native libraries ---"
    if [ -z "$GC_ZIP_URL" ]; then
        GC_ZIP_URL=$(curl -sL "https://api.github.com/repos/Nanook/GrindCore/releases/latest" \
            | grep -o '"browser_download_url": *"[^"]*grindcore\.zip"' | cut -d'"' -f4)
    fi
    [ -z "$GC_ZIP_URL" ] && { echo "ERROR: could not resolve grindcore.zip download URL"; exit 1; }
    echo "Downloading: $GC_ZIP_URL"
    TMP_ZIP="$(mktemp --suffix=.zip)"
    curl -sL "$GC_ZIP_URL" -o "$TMP_ZIP"
    rm -rf "$GC_NATIVE_DIR"
    mkdir -p "$GC_NATIVE_DIR"
    unzip -oq "$TMP_ZIP" -d "$GC_NATIVE_DIR/"
    rm -f "$TMP_ZIP"
    echo "GrindCore ready."
else
    echo "GrindCore native libs present."
fi
echo ""

# --- 4. NuGet volume + output dir --------------------------------------------
docker volume create nkit_ngc >/dev/null
mkdir -p "$OUT_HOST"

# --- 5. Build inside container -----------------------------------------------
# Source is on a 9p WSL mount (/mnt/d/...) - NuGet restore fails there.
# We copy only what the build needs to native ext4 at /build, skipping the
# large directories that would make the 9p transfer impractical:
#   .nuget/        - 6+ GB NuGet cache, replaced by the /ngc volume
#   bin/ obj/      - stale Windows build artefacts
#   build-output/  - output destination, not a source
#   .vs/           - Visual Studio metadata
#   publish/       - previous publish outputs
# GrindCore is copied only for the target runtime (not all 8 platform variants).
# After the copy, build.sh --legacy-container runs exactly as on GitHub Actions.
echo "--- Running build inside container ---"
echo ""

# Build the inner container script as a variable (single-quoted heredoc so the
# outer shell does not expand $BUILD_* - those are env vars passed via -e and
# expanded by the container's bash at runtime).
INNER_SCRIPT=$(cat << 'INNER'
set -euo pipefail

echo "=== Copying source to native ext4 ==="
mkdir -p /build

# Top-level build files
for f in Directory.Build.props NKit.slnx .editorconfig; do
    [ -f "/src/$f" ] && cp "/src/$f" "/build/$f" 2>/dev/null || true
done

# Build scripts (strip CRLF immediately)
cp -r /src/build /build/
find /build/build -name "*.sh" -exec sed -i "s/\r//" {} + 2>/dev/null || true
chmod +x /build/build/build.sh /build/build/archive.sh 2>/dev/null || true

# Source projects: copy all files, skip bin/ and obj/
copy_tree() {
    local src="/src/$1" dst="/build/$1"
    [ -d "$src" ] || return 0
    echo "  $1..."
    mkdir -p "$dst"
    find "$src" \( -path "$src/bin" -o -path "$src/obj" \) -prune \
        -o -type f -print | while IFS= read -r f; do
            rel="${f#$src/}"
            mkdir -p "$dst/$(dirname "$rel")"
            cp "$f" "$dst/$rel" 2>/dev/null || true
        done
}

for p in NKit NKitApp NKDSApp NKDS NKitDataStore NKitCore NKit.Pipeline \
          NKit.Primitives NKitStream NKit.UI NKDS.UI Shared Tmds.Fuse; do
    copy_tree "$p"
done

# GrindCore: only the runtimes needed for this build (not all 8 platform variants)
for rid in "${BUILD_RUNTIME}-legacy" "$BUILD_RUNTIME"; do
    if [ -d "/src/.grindcore-native/$rid" ]; then
        mkdir -p "/build/.grindcore-native/$rid"
        cp -r "/src/.grindcore-native/$rid/." "/build/.grindcore-native/$rid/"
        echo "  grindcore: $rid"
    fi
done

echo "Copy done."
echo ""

echo "=== Running build.sh (identical to CI) ==="
cd /build
VERSION_FLAG=""
[ -n "$BUILD_VERSION" ] && VERSION_FLAG="--version $BUILD_VERSION"
NUGET_PACKAGES=/ngc build/build.sh \
    --runtime "$BUILD_RUNTIME" \
    --config  "$BUILD_CONFIG"  \
    --apps    "$BUILD_APPS"    \
    --legacy-container         \
    $VERSION_FLAG

echo ""
echo "=== Copying archives to output ==="
cp /build/NKit_*.zip /out/ 2>/dev/null && ls -lh /out/*.zip || echo "WARNING: no archives found in /build"
INNER
)

docker run --rm \
    -v "$REPO_ROOT:/src:ro" \
    -v "$OUT_HOST:/out" \
    -v nkit_ngc:/ngc \
    -e HOME=/tmp/home \
    -e DOTNET_CLI_HOME=/tmp/home \
    -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e NUGET_PACKAGES=/ngc \
    -e BUILD_RUNTIME="$RUNTIME" \
    -e BUILD_CONFIG="$CONFIG" \
    -e BUILD_APPS="$APPS" \
    -e BUILD_VERSION="$VERSION" \
    "$IMAGE" bash -c "$INNER_SCRIPT"

echo ""
echo "=== Done. Output in $OUT_HOST ==="
ls -lh "$OUT_HOST"/*.zip 2>/dev/null | sed "s|^|  |" || echo "  (no archives found)"
