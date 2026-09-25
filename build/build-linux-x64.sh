#!/bin/bash
set -euo pipefail

# =============================================================================
# NKit Linux x64 AOT build (console + nkds + GUI) using NATIVE Docker in WSL.
# =============================================================================
# Produces: build-output/NKit_linux-x64_CLI_VERSION.zip
#           build-output/NKit_linux-x64_UI_VERSION.zip
#
# Run via build-all.ps1, or directly from Windows PowerShell:
#   wsl -d Ubuntu-22.04 -- bash -lc "cd /mnt/d/Src/NKitCode/NKit && VERSION=3.0.0-alpha.3 ./build/wsl-docker-build.sh"
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

RUNTIME="${RUNTIME:-linux-x64}"
CONFIG="${CONFIG:-Release}"
VERSION="${VERSION:-}"
IMAGE="${IMAGE:-nkit-legacy-x64-builder:latest}"
GC_NATIVE_DIR="$REPO_ROOT/.grindcore-native"
# Output goes directly to build-output/ at the repo root (next to build/)
OUT_HOST="$REPO_ROOT/build-output"

export DOCKER_HOST="${DOCKER_HOST:-unix:///var/run/docker.sock}"

echo "=== NKit $RUNTIME build (native Docker, ext4) ==="
echo "DOCKER_HOST: $DOCKER_HOST"
[ -n "$VERSION" ] && echo "Version: $VERSION"
docker info >/dev/null 2>&1 || { echo "native dockerd not reachable — run build/wsl-docker-build.sh"; exit 1; }

# --- 1. Builder image (Ubuntu 18.04 + .NET 10 SDK) ---------------------------
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    echo "--- Building builder image $IMAGE ---"
    docker build -t "$IMAGE" -f "$SCRIPT_DIR/docker/Dockerfile.legacy-x64" "$SCRIPT_DIR/docker"
fi

# --- 2. Stage GrindCore static lib (.a) --------------------------------------
if [ ! -f "$GC_NATIVE_DIR/${RUNTIME}-legacy/native/libGrindCore.a" ] && \
   [ ! -f "$GC_NATIVE_DIR/$RUNTIME/native/libGrindCore.a" ]; then
    echo "--- Downloading GrindCore native libraries ---"
    GC_ZIP_URL="${GC_ZIP_URL:-}"
    [ -z "$GC_ZIP_URL" ] && GC_ZIP_URL=$(curl -sL "https://api.github.com/repos/Nanook/GrindCore/releases/latest" \
        | grep -o '"browser_download_url": *"[^"]*grindcore\.zip"' | cut -d'"' -f4)
    [ -z "$GC_ZIP_URL" ] && { echo "Could not resolve grindcore.zip URL"; exit 1; }
    TMP_ZIP="$(mktemp --suffix=.zip)"
    curl -sL "$GC_ZIP_URL" -o "$TMP_ZIP"
    rm -rf "$GC_NATIVE_DIR"; mkdir -p "$GC_NATIVE_DIR"
    unzip -oq "$TMP_ZIP" -d "$GC_NATIVE_DIR/"
    rm -f "$TMP_ZIP"
fi
GC_A=""
for cand in "$GC_NATIVE_DIR/${RUNTIME}-legacy/native/libGrindCore.a" \
            "$GC_NATIVE_DIR/$RUNTIME/native/libGrindCore.a"; do
    [ -f "$cand" ] && { GC_A="$cand"; break; }
done
[ -z "$GC_A" ] && { echo "No libGrindCore.a for $RUNTIME under $GC_NATIVE_DIR"; exit 1; }
echo "GrindCore static lib: $GC_A"

# --- 3. Persistent NuGet cache volume ----------------------------------------
docker volume create nkit_ngc >/dev/null

mkdir -p "$OUT_HOST"

# --- 4. Run the container build script on ext4 -------------------------------
# The container-build.sh script is read from /src (repo mount) and run
# directly — no inline script quoting issues, sed/tr work normally.
docker run --rm \
  -v "$REPO_ROOT:/src:ro" \
  -v "$GC_A:/gc/libGrindCore.a:ro" \
  -v "$OUT_HOST:/out" \
  -v nkit_ngc:/ngc \
  -e HOME=/tmp/home -e DOTNET_CLI_HOME=/tmp/home \
  -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  -e VERSION="$VERSION" -e RUNTIME="$RUNTIME" -e CONFIG="$CONFIG" \
  "$IMAGE" bash -c "sed 's/\r//' /src/build/container-build.sh | bash"

echo ""
echo "=== Done. Output in $OUT_HOST ==="
ls -la "$OUT_HOST" 2>/dev/null | sed "s|^|  |" || true