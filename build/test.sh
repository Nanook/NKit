#!/bin/bash
set -euo pipefail

# =============================================================================
# NKit Unit-Test Gate — runs inside the same Docker container as local builds
# =============================================================================
#
# Runs the fast unit-test gate (same exclusions as GitHub Actions CI) inside
# the nkit-legacy-x64-builder container so .NET 10 SDK is always available.
#
# Excluded (matching CI):
#   Area=Full   — WipedImageTests / ProcessingTests (need real disc images)
#   Speed=Slow  — Real-image decode classes
#
# Usage (from Windows PowerShell):
#   wsl -d Ubuntu-22.04 -- bash -lc "cd /mnt/d/Src/NKitCode/NKit && ./build/test.sh"
#   wsl -d Ubuntu-22.04 -- bash -lc "cd /mnt/d/Src/NKitCode/NKit && ./build/test.sh --verbose"
#   wsl -d Ubuntu-22.04 -- bash -lc "cd /mnt/d/Src/NKitCode/NKit && ./build/test.sh --project NKit.Tests"
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

VERBOSE=false
SINGLE_PROJECT=""
FULL_SUITE=false
SINGLE_TRAIT=""
# Paths to external test assets on the Windows host (via WSL /mnt).
# These are mounted read-only into the container at the exact locations the tests
# expect after resolving ../../../../../ from the test binary output directory.
WIPED_IMAGES_HOST="${WIPED_IMAGES_HOST:-/mnt/d/Src/NKitCode/WipedImages}"
EXTERNAL_FILES_HOST="${EXTERNAL_FILES_HOST:-/mnt/d/Src/NKitCode/NKitExternalTestFiles}"
NKIT_FILES_HOST="${NKIT_FILES_HOST:-/mnt/d/NKitFiles}"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --verbose|-v)   VERBOSE=true; shift ;;
        --project|-p)   SINGLE_PROJECT="$2"; shift 2 ;;
        --full)         FULL_SUITE=true; shift ;;
        --trait)        SINGLE_TRAIT="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

# --- Docker setup (mirrors wsl-docker-build.sh) ---
export DOCKER_HOST="${DOCKER_HOST:-unix:///var/run/docker.sock}"
IMAGE="nkit-legacy-x64-builder:latest"
DOCKERFILE="$SCRIPT_DIR/docker/Dockerfile.legacy-x64"

docker_up() { docker info >/dev/null 2>&1; }
if ! docker_up; then
    echo "Native dockerd not responding — starting it..."
    if command -v service >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
        sudo service docker start || true
    fi
    for _ in $(seq 1 15); do docker_up && break; sleep 1; done
    docker_up || { echo "ERROR: native dockerd not ready"; exit 1; }
fi

if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    echo "--- Building builder image $IMAGE ---"
    docker build -t "$IMAGE" -f "$DOCKERFILE" "$SCRIPT_DIR/docker"
fi

docker volume create nkit_ngc >/dev/null

echo "=== NKit Unit-Test Gate (Docker: $IMAGE) ==="
[[ -n "$SINGLE_PROJECT" ]] && echo "Project filter: $SINGLE_PROJECT"

# Optionally mount the external test asset directories if they exist on the host.
# These give the full test suite (Area=Full, Speed=Slow) access to the real image
# files without copying them into the container. The tests resolve paths via
# ../../../../../ from the binary output dir, landing at /WipedImages and
# /NKitExternalTestFiles inside the container.
EXTRA_MOUNTS=""
if [ -d "$WIPED_IMAGES_HOST" ]; then
    EXTRA_MOUNTS="$EXTRA_MOUNTS -v $WIPED_IMAGES_HOST:/WipedImages:ro"
    echo "WipedImages:       $WIPED_IMAGES_HOST -> /WipedImages"
else
    echo "WipedImages:       not found at $WIPED_IMAGES_HOST (wiped-image tests will be skipped)"
fi
if [ -d "$EXTERNAL_FILES_HOST" ]; then
    EXTRA_MOUNTS="$EXTRA_MOUNTS -v $EXTERNAL_FILES_HOST:/NKitExternalTestFiles:ro"
    echo "ExternalTestFiles: $EXTERNAL_FILES_HOST -> /NKitExternalTestFiles"
else
    echo "ExternalTestFiles: not found at $EXTERNAL_FILES_HOST (ImageTests.DetectTest will fail)"
fi
if [ -d "$NKIT_FILES_HOST" ]; then
    EXTRA_MOUNTS="$EXTRA_MOUNTS -v $NKIT_FILES_HOST:/NKitFiles:ro"
    echo "NKitFiles:         $NKIT_FILES_HOST -> /NKitFiles"
else
    echo "NKitFiles:         not found at $NKIT_FILES_HOST (NKitAsIso/NkitGcz/NkitWii tests will be skipped)"
fi

# Pass flags to the container script via environment variables
docker run --rm \
    -v "$REPO_ROOT:/src:ro" \
    -v nkit_ngc:/ngc \
    $EXTRA_MOUNTS \
    -e HOME=/tmp/home \
    -e DOTNET_CLI_HOME=/tmp/home \
    -e NUGET_PACKAGES=/ngc \
    -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e TEST_VERBOSE="$VERBOSE" \
    -e TEST_PROJECT="$SINGLE_PROJECT" \
    -e TEST_FULL="$FULL_SUITE" \
    -e TEST_TRAIT="$SINGLE_TRAIT" \
    -e NKIT_FILES_CONTAINER_PATH=/NKitFiles \
    "$IMAGE" bash -c "sed 's/\r//' /src/build/container-test.sh | bash"
