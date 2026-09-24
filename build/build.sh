#!/bin/bash
set -euo pipefail

# NKit Unified Build Script
# Usage: build.sh --runtime <rid> [--config Release|Debug] [--apps console,gui,nkds,nkds-ui] [--version X.Y.Z] [--legacy] [--no-archive]
#
# Examples:
#   build.sh --runtime linux-x64 --apps console,nkds
#   build.sh --runtime linux-x64 --legacy --apps console,gui,nkds,nkds-ui
#   build.sh --runtime osx-arm64 --apps gui,nkds-ui
#   build.sh --runtime win-x64 --apps console,gui,nkds,nkds-ui
#   build.sh --runtime linux-x64 --version 2.0.0

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

# --- Defaults ---
RUNTIME=""
CONFIGURATION="Release"
TARGET_FRAMEWORK="net10.0"
APPS=""
VERSION=""
LEGACY=false
LEGACY_CONTAINER=false
NO_ARCHIVE=false
GRINDCORE_NATIVE_DIR=".grindcore-native"

# --- Parse arguments ---
while [[ $# -gt 0 ]]; do
    case "$1" in
        --runtime)    RUNTIME="$2"; shift 2 ;;
        --config)     CONFIGURATION="$2"; shift 2 ;;
        --apps)       APPS="$2"; shift 2 ;;
        --version)    VERSION="$2"; export VERSION; shift 2 ;;
        --legacy)     LEGACY=true; shift ;;
        --legacy-container) LEGACY_CONTAINER=true; shift ;;
        --no-archive) NO_ARCHIVE=true; shift ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

if [ -z "$RUNTIME" ]; then
    echo "Error: --runtime is required"
    echo "Valid runtimes: win-x64, win-arm64, linux-x64, linux-arm64, osx-x64, osx-arm64"
    exit 1
fi

if [ -z "$APPS" ]; then
    APPS="console,gui,nkds,nkds-ui"
fi

# --- Determine which apps to build ---
BUILD_CONSOLE=false
BUILD_GUI=false
BUILD_NKDS=false
BUILD_NKDS_UI=false

IFS=',' read -ra APP_ARRAY <<< "$APPS"
for app in "${APP_ARRAY[@]}"; do
    app=$(echo "$app" | xargs)
    case "$app" in
        console)  BUILD_CONSOLE=true ;;
        gui)      BUILD_GUI=true ;;
        nkds)     BUILD_NKDS=true ;;
        nkds-ui)  BUILD_NKDS_UI=true ;;
        *) echo "Unknown app: $app"; exit 1 ;;
    esac
done

# --- Project paths ---
CONSOLE_PROJECT="NKitApp/NKitApp.csproj"
GUI_PROJECT="NKit.UI/NKit.UI.csproj"
NKDS_PROJECT="NKDSApp/NKDSApp.csproj"
NKDS_UI_PROJECT="NKDS.UI/NKDS.UI.csproj"

# --- Resolve publish RID (legacy uses standard RID for dotnet but different artifact naming) ---
PUBLISH_RID="$RUNTIME"
ARTIFACT_NAME="$RUNTIME"
if [[ "$LEGACY" == true ]] || [[ "$LEGACY_CONTAINER" == true ]]; then
    ARTIFACT_NAME="linux-legacy-${RUNTIME#linux-}"
    export MATRIX_RUNTIME="$ARTIFACT_NAME"
elif [[ "$RUNTIME" == "osx-arm64" ]]; then
    ARTIFACT_NAME="macOS-AppleSilicon"
elif [[ "$RUNTIME" == "osx-x64" ]]; then
    ARTIFACT_NAME="macOS-Intel"
else
    export MATRIX_RUNTIME="$RUNTIME"
fi

echo "=== NKit Build ==="
echo "Runtime: $RUNTIME (publish RID: $PUBLISH_RID)"
echo "Configuration: $CONFIGURATION"
echo "Apps: $APPS"
echo "Legacy: $LEGACY"
echo "Artifact name: $ARTIFACT_NAME"
if [ -n "$VERSION" ]; then echo "Version: $VERSION"; fi
echo ""

# --- Legacy builds run inside Docker ---
if [[ "$LEGACY" == true ]]; then
    exec "$SCRIPT_DIR/build-legacy.sh" "$RUNTIME" "$CONFIGURATION" "$TARGET_FRAMEWORK" "$APPS" "$NO_ARCHIVE"
fi

# --- Standard build (runs directly) ---

# Restore
echo "--- Restoring dependencies ---"
for project in "$CONSOLE_PROJECT:$BUILD_CONSOLE" "$GUI_PROJECT:$BUILD_GUI" "$NKDS_PROJECT:$BUILD_NKDS" "$NKDS_UI_PROJECT:$BUILD_NKDS_UI"; do
    proj="${project%%:*}"
    enabled="${project##*:}"
    if [[ "$enabled" == "true" ]]; then
        dotnet restore "$proj" --runtime "$PUBLISH_RID"
    fi
done

# Inject GrindCore static library
inject_grindcore() {
    local static_lib_name
    if [[ "$PUBLISH_RID" == linux-arm64 ]]; then
        static_lib_name="libGrindCore.a"
    elif [[ "$PUBLISH_RID" == linux-* ]]; then
        static_lib_name="libGrindCore.a"
    elif [[ "$PUBLISH_RID" == osx-* ]]; then
        static_lib_name="libGrindCore.a"
    elif [[ "$PUBLISH_RID" == win-* ]]; then
        static_lib_name="GrindCore.lib"
    else
        echo "Unknown platform for GrindCore injection: $PUBLISH_RID"
        return 0
    fi

    local source_rid="$PUBLISH_RID"
    if [[ "$LEGACY" == true ]] || [[ "$LEGACY_CONTAINER" == true ]]; then
        # Legacy builds use the legacy-compiled static lib
        if [[ "$PUBLISH_RID" == "linux-x64" ]]; then
            source_rid="linux-x64-legacy"
        elif [[ "$PUBLISH_RID" == "linux-arm64" ]]; then
            source_rid="linux-arm64-legacy"
        fi
    fi

    local source_file="$GRINDCORE_NATIVE_DIR/$source_rid/native/$static_lib_name"
    if [ ! -f "$source_file" ]; then
        echo "GrindCore static lib not found at $source_file (will use dynamic linking)"
        return 0
    fi

    local nuget_packages="${NUGET_PACKAGES:-.nuget/packages}"
    local dest_dir="$nuget_packages/grindcore/0.9.0/runtimes/$PUBLISH_RID/native"
    if [ ! -d "$dest_dir" ]; then
        mkdir -p "$dest_dir"
    fi

    echo "Injecting GrindCore static library: $source_file -> $dest_dir/$static_lib_name"
    cp "$source_file" "$dest_dir/$static_lib_name"
}
inject_grindcore

# For ARM64 linux: copy libatomic.a to a known location so the linker can find it
if [[ "$PUBLISH_RID" == "linux-arm64" ]]; then
    LIBATOMIC=$(find /usr/lib -name "libatomic.a" 2>/dev/null | head -1)
    if [ -n "$LIBATOMIC" ]; then
        cp "$LIBATOMIC" "$GRINDCORE_NATIVE_DIR/libatomic.a"
        echo "Copied $LIBATOMIC to $GRINDCORE_NATIVE_DIR/libatomic.a"
    else
        echo "WARNING: libatomic.a not found on system"
    fi
fi

# Determine extra publish properties
EXTRA_PUBLISH_PROPS=""
if [ -n "$VERSION" ]; then
    EXTRA_PUBLISH_PROPS="--property:Version=$VERSION"
fi

# Publish
echo ""
echo "--- Publishing ---"
publish_app() {
    local project="$1"
    local app_type="$2"

    echo "Publishing $app_type for $PUBLISH_RID..."
    dotnet publish "$project" \
        --framework "$TARGET_FRAMEWORK" \
        --runtime "$PUBLISH_RID" \
        --configuration "$CONFIGURATION" \
        --self-contained true \
        --property:PublishSingleFile=true \
        --property:StripSymbols=true \
        --property:AllowUnsafeBlocks=true \
        --property:PlatformName="$PUBLISH_RID" \
        --output "publish/$app_type/$PUBLISH_RID" \
        $EXTRA_PUBLISH_PROPS
    echo "Published $app_type -> publish/$app_type/$PUBLISH_RID/"
}

if [[ "$BUILD_CONSOLE" == true ]]; then publish_app "$CONSOLE_PROJECT" "console"; fi
if [[ "$BUILD_GUI" == true ]]; then publish_app "$GUI_PROJECT" "gui"; fi
if [[ "$BUILD_NKDS" == true ]]; then publish_app "$NKDS_PROJECT" "nkds"; fi
if [[ "$BUILD_NKDS_UI" == true ]]; then publish_app "$NKDS_UI_PROJECT" "nkds-ui"; fi

# --- Post-processing ---
echo ""
echo "--- Post-processing ---"

# Set executable permissions (Unix only)
if [[ "$PUBLISH_RID" != win-* ]]; then
    chmod +x "publish/console/$PUBLISH_RID/nkit" 2>/dev/null || true
    chmod +x "publish/gui/$PUBLISH_RID/nkit-ui" 2>/dev/null || true
    chmod +x "publish/nkds/$PUBLISH_RID/nkds" 2>/dev/null || true
    chmod +x "publish/nkds-ui/$PUBLISH_RID/nkds-ui" 2>/dev/null || true
fi

# Clean statically-linked native libs from output (they're baked into the binary)
# Exception: legacy ARM64 uses dynamic linking, keep .so
for dir in "publish/console/$PUBLISH_RID" "publish/gui/$PUBLISH_RID" "publish/nkds/$PUBLISH_RID" "publish/nkds-ui/$PUBLISH_RID"; do
    if [ -d "$dir" ]; then
        rm -f "$dir"/*.pdb "$dir"/*.dbg "$dir"/*.deps.json "$dir"/*.crproj "$dir"/rd.xml 2>/dev/null || true
        rm -rf "$dir"/*.dSYM 2>/dev/null || true
        # AOT builds are statically linked — remove native shared libs from output
        rm -f "$dir"/libGrindCore.so "$dir"/libGrindCore.dylib "$dir"/GrindCore.dll 2>/dev/null || true
    fi
done

# Add portable config files
add_config_files() {
    local rid="$1"

    # Console: nkit.yaml + defaults directory
    if [[ "$BUILD_CONSOLE" == true ]] && [ -d "publish/console/$rid" ]; then
        if [ -f "NKitApp/nkit.yaml" ]; then
            cp "NKitApp/nkit.yaml" "publish/console/$rid/"
        fi
        if [ -d "NKit/defaults" ]; then
            cp -r "NKit/defaults" "publish/console/$rid/"
        fi
        if [ -f "NKitApp/ConfigInfo.txt" ]; then
            cp "NKitApp/ConfigInfo.txt" "publish/console/$rid/"
        fi
    fi

    # GUI: nkit-ui.yaml + defaults directory
    if [[ "$BUILD_GUI" == true ]] && [ -d "publish/gui/$rid" ]; then
        if [ -f "NKit.UI/nkit-ui.yaml" ]; then
            cp "NKit.UI/nkit-ui.yaml" "publish/gui/$rid/"
        fi
        if [ -d "NKit/defaults" ]; then
            cp -r "NKit/defaults" "publish/gui/$rid/"
        fi
        if [ -f "NKit.UI/ConfigInfo.txt" ]; then
            cp "NKit.UI/ConfigInfo.txt" "publish/gui/$rid/"
        fi
    fi
}
add_config_files "$PUBLISH_RID"

# macOS bundles
if [[ "$PUBLISH_RID" == osx-* ]]; then
    source "$SCRIPT_DIR/macos-bundle.sh"
    if [[ "$BUILD_GUI" == true ]]; then
        create_nkit_macos_bundle "$PUBLISH_RID"
    fi
    if [[ "$BUILD_NKDS_UI" == true ]]; then
        create_nkds_ui_macos_bundle "$PUBLISH_RID"
    fi

    # Clear quarantine attributes
    if command -v xattr >/dev/null 2>&1; then
        xattr -rc publish/bundle/$PUBLISH_RID 2>/dev/null || true
        xattr -rc publish/gui/$PUBLISH_RID 2>/dev/null || true
        xattr -rc publish/console/$PUBLISH_RID 2>/dev/null || true
    fi
fi

# --- Archive ---
if [[ "$NO_ARCHIVE" == true ]]; then
    echo ""
    echo "Skipping archive creation (--no-archive)"
else
    echo ""
    echo "--- Creating archives ---"
    source "$SCRIPT_DIR/archive.sh"
    create_archives "$PUBLISH_RID" "$ARTIFACT_NAME" "$BUILD_CONSOLE" "$BUILD_GUI" "$BUILD_NKDS" "$BUILD_NKDS_UI"
fi

echo ""
echo "=== Build complete ==="
