#!/bin/bash
set -e

# Reads platform selections from workflow inputs (booleans) or defaults to all platforms.
# Usage: setup-matrix.sh <event_name> <win-x64> <win-arm64> <linux-x64> <linux-arm64> <linux-legacy-x64> <linux-legacy-arm64> <osx-x64> <osx-arm64>

EVENT_NAME="$1"

# For non-dispatch events, build all platforms
if [ "$EVENT_NAME" != "workflow_dispatch" ]; then
    PLATFORMS="win-x64,win-arm64,linux-x64,linux-arm64,linux-legacy-x64,linux-legacy-arm64,osx-x64,osx-arm64"
else
    # Build comma-separated list from individual boolean inputs
    PLATFORMS=""
    [ "$2" = "true" ] && PLATFORMS="${PLATFORMS}win-x64,"
    [ "$3" = "true" ] && PLATFORMS="${PLATFORMS}win-arm64,"
    [ "$4" = "true" ] && PLATFORMS="${PLATFORMS}linux-x64,"
    [ "$5" = "true" ] && PLATFORMS="${PLATFORMS}linux-arm64,"
    [ "$6" = "true" ] && PLATFORMS="${PLATFORMS}linux-legacy-x64,"
    [ "$7" = "true" ] && PLATFORMS="${PLATFORMS}linux-legacy-arm64,"
    [ "$8" = "true" ] && PLATFORMS="${PLATFORMS}osx-x64,"
    [ "$9" = "true" ] && PLATFORMS="${PLATFORMS}osx-arm64,"
    # Remove trailing comma
    PLATFORMS="${PLATFORMS%,}"
fi

echo "Selected platforms: $PLATFORMS"

# Build matrix JSON
MATRIX_JSON='{"include":['
FIRST=true

IFS=',' read -ra PLATFORM_ARRAY <<< "$PLATFORMS"
for platform in "${PLATFORM_ARRAY[@]}"; do
    platform=$(echo "$platform" | xargs)

    if [ "$FIRST" = false ]; then
        MATRIX_JSON+=","
    fi
    FIRST=false

    case "$platform" in
        "win-x64")
            MATRIX_JSON+='{"runtime":"win-x64","runson":"windows-latest","legacy":false,"shell":"pwsh","artifact":"NKit-Windows-x64"}'
            ;;
        "win-arm64")
            MATRIX_JSON+='{"runtime":"win-arm64","runson":"windows-latest","legacy":false,"shell":"pwsh","artifact":"NKit-Windows-ARM64"}'
            ;;
        "linux-x64")
            MATRIX_JSON+='{"runtime":"linux-x64","runson":"ubuntu-latest","legacy":false,"shell":"bash","artifact":"NKit-Linux-x64"}'
            ;;
        "linux-arm64")
            MATRIX_JSON+='{"runtime":"linux-arm64","runson":"ubuntu-24.04-arm","legacy":false,"shell":"bash","artifact":"NKit-Linux-ARM64"}'
            ;;
        "linux-legacy-x64")
            MATRIX_JSON+='{"runtime":"linux-x64","runson":"ubuntu-latest","legacy":true,"shell":"bash","artifact":"NKit-Linux-legacy-x64"}'
            ;;
        "linux-legacy-arm64")
            MATRIX_JSON+='{"runtime":"linux-arm64","runson":"ubuntu-24.04-arm","legacy":true,"shell":"bash","artifact":"NKit-Linux-legacy-arm64"}'
            ;;
        "osx-x64")
            MATRIX_JSON+='{"runtime":"osx-x64","runson":"macos-latest","legacy":false,"shell":"bash","artifact":"NKit-macOS-Intel"}'
            ;;
        "osx-arm64")
            MATRIX_JSON+='{"runtime":"osx-arm64","runson":"macos-latest","legacy":false,"shell":"bash","artifact":"NKit-macOS-AppleSilicon"}'
            ;;
    esac
done

MATRIX_JSON+=']}'

echo "Generated matrix: $MATRIX_JSON"
echo "matrix=$MATRIX_JSON" >> $GITHUB_OUTPUT
