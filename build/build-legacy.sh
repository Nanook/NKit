#!/bin/bash
set -euo pipefail

# Legacy Linux build wrapper - runs the standard build inside an Ubuntu 18.04 Docker container
# Called by build.sh when --legacy is specified. Do not call directly.

RUNTIME="$1"
CONFIGURATION="$2"
TARGET_FRAMEWORK="$3"
APPS="$4"
NO_ARCHIVE="${5:-false}"
# VERSION is inherited from the parent build.sh environment

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Determine architecture for Dockerfile selection
ARCH="${RUNTIME#linux-}"  # x64 or arm64
DOCKERFILE="$SCRIPT_DIR/docker/Dockerfile.legacy-$ARCH"

if [ ! -f "$DOCKERFILE" ]; then
    echo "Error: Dockerfile not found: $DOCKERFILE"
    exit 1
fi

IMAGE_NAME="nkit-legacy-$ARCH-builder:latest"

echo "=== Legacy Linux Build (Ubuntu 18.04, glibc 2.27) ==="
echo "Runtime: $RUNTIME, Arch: $ARCH"
echo "Dockerfile: $DOCKERFILE"
echo ""

# Build the Docker image
echo "Building Docker image: $IMAGE_NAME"
docker build -t "$IMAGE_NAME" -f "$DOCKERFILE" "$SCRIPT_DIR/docker"

# Ensure local directories exist for mounting
mkdir -p "$REPO_ROOT/.nuget/packages"
mkdir -p "$REPO_ROOT/.dotnet"

# Determine MATRIX_RUNTIME for artifact naming
MATRIX_RUNTIME="linux-legacy-$ARCH"

# Run build inside container
# - Mount entire repo at /workspace
# - Run as host user to avoid root-owned files
# - Build uses the same build.sh but without --legacy (we're already in the legacy container)
echo ""
echo "Running build inside container..."
docker run --rm \
    -u "$(id -u):$(id -g)" \
    -v "$REPO_ROOT:/workspace" \
    -w /workspace \
    -e NUGET_PACKAGES="/workspace/.nuget/packages" \
    -e HOME="/workspace" \
    -e DOTNET_CLI_HOME="/workspace/.dotnet" \
    -e DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    -e MATRIX_RUNTIME="$MATRIX_RUNTIME" \
    -e VERSION="${VERSION:-}" \
    "$IMAGE_NAME" \
    bash -c "cd /workspace && build/build.sh --runtime $RUNTIME --config $CONFIGURATION --apps $APPS --legacy-container ${VERSION:+--version $VERSION} $([ '$NO_ARCHIVE' = 'true' ] && echo '--no-archive')"

echo ""
echo "=== Legacy build complete ==="
