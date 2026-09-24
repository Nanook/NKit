#!/bin/bash
set -euo pipefail

# =============================================================================
# NKit local Linux AOT build launcher — NATIVE Docker in WSL (NOT Docker Desktop)
# =============================================================================
# Docker Desktop's engine drops dot-folders (.nuget/.git) on /mnt bind mounts,
# which breaks the build. This WSL distro also has a native docker-ce + dockerd
# whose daemon handles the filesystem correctly. This launcher pins the native
# daemon, starts it if needed, then runs build-linux-x64.sh.
#
# Run from Windows PowerShell:
#   wsl -d Ubuntu-22.04 -- bash -lc "cd /mnt/d/Src/NKitCode/NKit && ./build/wsl-docker-build.sh"
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Force the NATIVE docker daemon, never the Docker Desktop npipe contexts.
export DOCKER_HOST="unix:///var/run/docker.sock"
unset DOCKER_CONTEXT 2>/dev/null || true

echo "=== NKit WSL native-Docker build launcher ==="
echo "DOCKER_HOST: $DOCKER_HOST"

docker_up() { docker info >/dev/null 2>&1; }

if ! docker_up; then
    echo "Native dockerd not responding — starting it..."
    if command -v service >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
        sudo service docker start || true
    fi
    if ! docker_up; then
        echo "Launching dockerd (sudo; may prompt for password)..."
        sudo bash -c 'nohup dockerd >/var/log/dockerd-nkit.log 2>&1 &' || {
            echo "ERROR: could not start dockerd. Start it manually:"
            echo "  sudo dockerd >/var/log/dockerd-nkit.log 2>&1 &"
            exit 1
        }
        for _ in $(seq 1 30); do docker_up && break; sleep 1; done
    fi
fi
docker_up || { echo "ERROR: native dockerd not ready (see /var/log/dockerd-nkit.log)"; exit 1; }
echo "Native docker server: $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?')"
echo ""

export DOCKER_HOST
exec "$SCRIPT_DIR/build-linux-x64.sh" "$@"
