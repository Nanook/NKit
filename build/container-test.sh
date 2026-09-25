#!/bin/bash
set -euo pipefail

# =============================================================================
# Container test script — runs INSIDE the Docker container.
# Called by test.sh via:
#   docker run ... bash -c "sed 's/\r//' /src/build/container-test.sh | bash"
# All paths are container-internal: /src (repo, read-only), /ngc (NuGet cache).
# =============================================================================

FRAMEWORK="net10.0"
VERBOSE="${TEST_VERBOSE:-false}"
SINGLE_PROJECT="${TEST_PROJECT:-}"
FULL_SUITE="${TEST_FULL:-false}"
TRAIT_FILTER="${TEST_TRAIT:-}"

ALL_PROJECTS=(
    "NKit.Tests/NKit.Tests.csproj"
    "NKitDataStore.Tests/NKitDataStore.Tests.csproj"
    "NKDS.Tests/NKDS.Tests.csproj"
    "NKitCore.Tests/NKitCore.Tests.csproj"
    "NKitStream.Tests/NKitStream.Tests.csproj"
)

if [[ -n "$SINGLE_PROJECT" ]]; then
    PROJECTS=()
    for p in "${ALL_PROJECTS[@]}"; do
        [[ "$p" == *"$SINGLE_PROJECT"* ]] && PROJECTS+=("$p")
    done
    if [[ ${#PROJECTS[@]} -eq 0 ]]; then
        echo "No project matching '$SINGLE_PROJECT'. Available:"
        for p in "${ALL_PROJECTS[@]}"; do echo "  $p"; done
        exit 1
    fi
else
    PROJECTS=("${ALL_PROJECTS[@]}")
fi

echo "SDK: $(dotnet --version)"
echo "Projects: ${PROJECTS[*]}"
echo ""

# Copy source tree to /build (ext4, not 9p) for reliable dotnet performance.
# Same approach as container-build.sh.
echo "### copying source tree ###"
mkdir -p /build

copy_project() {
    local p="$1"
    [ -d "/src/$p" ] || return 0
    mkdir -p "/build/$p"
    find "/src/$p" \
        \( -path "/src/$p/bin" -o -path "/src/$p/obj" \) -prune \
        -o -type f \( \
          -name "*.cs" -o -name "*.csproj" -o -name "*.json" \
          -o -name "*.yaml" -o -name "*.props" -o -name "*.targets" \
          -o -name "*.txt" -o -name "*.axaml" -o -name "*.svg" \
          -o -name "*.png"  -o -name "*.ico"  -o -name "*.otf" \
          -o -name "*.ttf"  -o -name "*.resx" -o -name "*.manifest" \
          -o -name "*.xml"  -o -name "*.editorconfig" \
        \) -print 2>/dev/null \
      | while IFS= read -r f; do
          rel="${f#/src/$p/}"
          mkdir -p "/build/$p/$(dirname "$rel")"
          cp "$f" "/build/$p/$rel" 2>/dev/null || true
        done
}

for p in \
    NKit.Tests NKitDataStore.Tests NKDS.Tests NKitCore.Tests NKitStream.Tests \
    NKit NKit.UI NKitDataStore NKitApp NKDSApp \
    NKitCore NKit.Pipeline NKit.Primitives NKitStream \
    NKDS NKDS.UI \
    Shared Tmds.Fuse; do
    copy_project "$p"
done

# Top-level build files
for f in Directory.Build.props NKit.slnx .editorconfig; do
    [ -f "/src/$f" ] && cp "/src/$f" "/build/$f" || true
done

# Strip CRLF
find /build \( -name "*.cs" -o -name "*.csproj" -o -name "*.props" -o -name "*.targets" \) \
     -exec sed -i 's/\x0d//' {} +

# NuGet config
cat > /build/NuGet.Config <<'NUGETEOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="globalPackagesFolder" value="/ngc" /></config>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
NUGETEOF

cd /build

# Inject GrindCore legacy .so for the test runner (Debug builds use dynamic linking,
# unlike AOT builds which statically link .a). The NuGet-provided linux-x64 .so
# requires glibc 2.32+ but this Ubuntu 18.04 container has glibc 2.27.
# Copy the legacy .so from the host repo's .grindcore-native into the NuGet cache
# so dotnet loads the glibc-2.27-compatible version at test runtime.
GC_VER=$(grep -oP '(?<=Include="GrindCore" Version=")[^"]+' /src/NKit/NKit.csproj | head -1)
GC_VER="${GC_VER:-0.9.0}"
GC_LEGACY_SO="/src/.grindcore-native/linux-x64-legacy/native/libGrindCore.so"
if [ -f "$GC_LEGACY_SO" ]; then
    GC_DEST="/ngc/grindcore/$GC_VER/runtimes/linux-x64/native"
    mkdir -p "$GC_DEST"
    cp "$GC_LEGACY_SO" "$GC_DEST/libGrindCore.so"
    echo "Injected GrindCore legacy .so ($GC_VER) -> $GC_DEST"
else
    echo "WARNING: legacy GrindCore .so not found at $GC_LEGACY_SO — tests requiring GrindCore will fail"
fi

echo ""

echo "### restore ###"
for PROJECT in "${PROJECTS[@]}"; do
    [ -f "$PROJECT" ] || continue
    dotnet restore "$PROJECT" >/dev/null
done

echo "### build ###"
for PROJECT in "${PROJECTS[@]}"; do
    [ -f "$PROJECT" ] || continue
    PROJECT_NAME="$(basename "$(dirname "$PROJECT")")"
    echo "  Building $PROJECT_NAME..."
    dotnet build "$PROJECT" \
        --framework "$FRAMEWORK" \
        --configuration Debug \
        --nologo \
        --no-restore 2>&1 | grep -E "error|warning|succeeded|FAILED" | head -10
done

echo ""
echo "### test ###"
FAILED_PROJECTS=()

for PROJECT in "${PROJECTS[@]}"; do
    [ -f "$PROJECT" ] || continue
    PROJECT_NAME="$(basename "$(dirname "$PROJECT")")"

    echo "--- $PROJECT_NAME ---"
    EXE="/build/$(dirname "$PROJECT")/bin/Debug/$FRAMEWORK/$PROJECT_NAME"

    RUNNER_ARGS=()
    if [[ "$FULL_SUITE" != "true" ]]; then
        RUNNER_ARGS+=("-notrait" "Area=Full" "-notrait" "Speed=Slow")
    fi
    if [[ -n "$TRAIT_FILTER" ]]; then
        # e.g. TEST_TRAIT="Group=ImageReading" adds -trait "Group=ImageReading"
        RUNNER_ARGS+=("-trait" "$TRAIT_FILTER")
    fi
    [[ "$VERBOSE" == true ]] && RUNNER_ARGS+=("-verbose")

    set +e
    if [ -f "$EXE" ]; then
        "$EXE" "${RUNNER_ARGS[@]}"
    else
        dotnet test "$PROJECT" \
            --framework "$FRAMEWORK" \
            --configuration Debug \
            --no-build \
            --filter "Area!=Full&Speed!=Slow" \
            --nologo \
            $([ "$VERBOSE" = "true" ] && echo "--verbosity normal" || true)
    fi
    EXIT_CODE=$?
    set -e

    if [[ $EXIT_CODE -ne 0 ]]; then
        FAILED_PROJECTS+=("$PROJECT_NAME")
        echo "✗ $PROJECT_NAME FAILED (exit $EXIT_CODE)"
    else
        echo "✓ $PROJECT_NAME passed"
    fi
    echo ""
done

echo "=== Summary ==="
if [[ ${#FAILED_PROJECTS[@]} -eq 0 ]]; then
    echo "✓ All test projects passed"
    exit 0
else
    echo "✗ Failed: ${FAILED_PROJECTS[*]}"
    exit 1
fi
