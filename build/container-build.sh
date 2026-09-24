#!/bin/bash
# =============================================================================
# Container build script — runs INSIDE the Docker container.
# Called by build-linux-x64.sh via:
#   docker run ... bash /src/localbuild/container-build.sh
# All paths are container-internal: /src (repo RO), /gc (GrindCore.a RO),
# /out (output dir), /ngc (persistent NuGet volume).
# =============================================================================
set -euo pipefail

RUNTIME="${RUNTIME:-linux-x64}"
CONFIG="${CONFIG:-Release}"
VERSION="${VERSION:-}"

echo "=== container build: $RUNTIME ==="
[ -n "$VERSION" ] && echo "Version: $VERSION"

# ---- 1. copy source-only tree to ext4 /build --------------------------------
echo "### copying source tree ###"
mkdir -p /build

# Helper: copy source-only files from a project dir, excluding bin/obj
copy_project() {
  local p="$1"
  [ -d "/src/$p" ] || return 0
  mkdir -p "/build/$p"
  find "/src/$p" \
      \( -path "/src/$p/bin" -o -path "/src/$p/obj" \) -prune \
      -o -type f \( \
        -name "*.cs" -o -name "*.csproj" -o -name "*.json" \
        -o -name "*.yaml" -o -name "*.props" -o -name "*.targets" \
        -o -name "*.txt" -o -name "*.editorconfig" -o -name "*.resx" \
        -o -name "*.axaml" -o -name "*.svg" -o -name "*.png" \
        -o -name "*.ico"   -o -name "*.otf" -o -name "*.ttf" \
        -o -name "*.manifest" -o -name "*.xml" \
      \) -print 2>/dev/null \
    | while IFS= read -r f; do
        rel="${f#/src/$p/}"
        mkdir -p "/build/$p/$(dirname "$rel")"
        cp "$f" "/build/$p/$rel" 2>/dev/null || true
      done
}

for p in NKitApp NKDSApp NKDS NKit NKitDataStore NKitCore NKit.Pipeline NKit.Primitives NKitStream NKit.UI NKDS.UI Shared; do
  copy_project "$p"
done

# Tmds.Fuse (FUSE mount support for NKDS on Linux)
mkdir -p /build/Tmds.Fuse
find /src/Tmds.Fuse \
    \( -path "*/bin" -o -path "*/obj" \) -prune \
    -o -type f \( \
      -name "*.cs" -o -name "*.csproj" -o -name "*.json" \
      -o -name "*.props" -o -name "*.targets" \
    \) -print 2>/dev/null \
  | while IFS= read -r f; do
      rel="${f#/src/Tmds.Fuse/}"
      mkdir -p "/build/Tmds.Fuse/$(dirname "$rel")"
      cp "$f" "/build/Tmds.Fuse/$rel" 2>/dev/null || true
    done

# Strip CR (0x0D) from all text source files — Windows git checkout adds CRLF.
echo "### stripping CRLF from source files ###"
find /build \( -name "*.cs" -o -name "*.axaml" -o -name "*.csproj" -o -name "*.props" -o -name "*.targets" \) \
     -exec sed -i 's/\x0d//' {} +

# Top-level build files
for f in Directory.Build.props NKit.slnx .editorconfig; do
  [ -f "/src/$f" ] && cp "/src/$f" "/build/$f" || true
done

# Config + defaults needed for archives
[ -d /src/NKit/defaults ]            && cp -r /src/NKit/defaults /build/NKit/defaults || true
[ -f /src/NKitApp/nkit.yaml ]        && cp /src/NKitApp/nkit.yaml /build/NKitApp/ || true
[ -f /src/NKitApp/ConfigInfo.txt ]   && cp /src/NKitApp/ConfigInfo.txt /build/NKitApp/ || true
[ -f /src/NKit.UI/nkit-ui.yaml ]     && cp /src/NKit.UI/nkit-ui.yaml /build/NKit.UI/ || true
[ -f /src/NKit.UI/ConfigInfo.txt ]   && cp /src/NKit.UI/ConfigInfo.txt /build/NKit.UI/ || true

# ---- 2. NuGet config: ext4 /ngc, not 9p /src --------------------------------
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

# ---- 3. restore --------------------------------------------------------------
echo "### restore ###"
dotnet restore NKitApp/NKitApp.csproj             --runtime "$RUNTIME" >/dev/null
dotnet restore NKitDataStore/NKitDataStore.csproj --runtime "$RUNTIME" >/dev/null
dotnet restore NKDS/NKDS.csproj                   --runtime "$RUNTIME" >/dev/null
dotnet restore NKDSApp/NKDSApp.csproj             --runtime "$RUNTIME" >/dev/null
dotnet restore NKit.UI/NKit.UI.csproj             --runtime "$RUNTIME" >/dev/null
dotnet restore NKDS.UI/NKDS.UI.csproj             --runtime "$RUNTIME" >/dev/null

# ---- 4. inject GrindCore static lib -----------------------------------------
GC_VER=$(grep -oP '(?<=Include="GrindCore" Version=")[^"]+' /build/NKit/NKit.csproj | head -1)
GC_VER="${GC_VER:-0.9.0}"
GC_DEST="/ngc/grindcore/$GC_VER/runtimes/$RUNTIME/native"
mkdir -p "$GC_DEST"
cp /gc/libGrindCore.a "$GC_DEST/libGrindCore.a"
echo "Injected GrindCore $GC_VER -> $GC_DEST"

# ---- 5. publish --------------------------------------------------------------
EXTRA=""
[ -n "$VERSION" ] && EXTRA="--property:Version=$VERSION"

pub() {
  local proj="$1" apptype="$2"
  echo "### publish $apptype ###"
  dotnet publish "$proj" \
    --framework net10.0 --runtime "$RUNTIME" --configuration "$CONFIG" \
    --self-contained true \
    --property:PublishSingleFile=true \
    --property:StripSymbols=true \
    --property:AllowUnsafeBlocks=true \
    --property:PlatformName="$RUNTIME" \
    $EXTRA \
    --output "/build/publish/$apptype/$RUNTIME"
}

pub NKitApp/NKitApp.csproj  console
pub NKDSApp/NKDSApp.csproj  nkds
pub NKit.UI/NKit.UI.csproj  gui
pub NKDS.UI/NKDS.UI.csproj  nkds-ui

# ---- 6. post-process ---------------------------------------------------------
for apptype in console nkds gui nkds-ui; do
    dir="/build/publish/$apptype/$RUNTIME"
    rm -f "$dir"/*.pdb "$dir"/*.deps.json "$dir"/*.crproj "$dir"/rd.xml \
          "$dir"/*.a "$dir"/*.dbg 2>/dev/null || true
    # Remove GrindCore dynamic lib — statically linked so not needed at runtime
    rm -f "$dir"/libGrindCore.so "$dir"/libGrindCore.dylib 2>/dev/null || true
done
chmod +x /build/publish/console/$RUNTIME/nkit    2>/dev/null || true
chmod +x /build/publish/nkds/$RUNTIME/nkds       2>/dev/null || true
chmod +x /build/publish/gui/$RUNTIME/nkit-ui     2>/dev/null || true
chmod +x /build/publish/nkds-ui/$RUNTIME/nkds-ui 2>/dev/null || true

# ---- 7. assemble CLI archive dir (nkit + nkds + config) ----------------------
cli_dir="/build/publish/console/$RUNTIME"
[ -f /build/NKitApp/nkit.yaml ]    && cp /build/NKitApp/nkit.yaml "$cli_dir/"
mkdir -p "$cli_dir/defaults/fix"
find /src/NKit/defaults/fix -name "*.yaml" 2>/dev/null | while IFS= read -r f; do
    cp "$f" "$cli_dir/defaults/fix/"
done
[ -f /build/NKitApp/ConfigInfo.txt ] && cp /build/NKitApp/ConfigInfo.txt "$cli_dir/"
cp /build/publish/nkds/$RUNTIME/nkds "$cli_dir/"
rm -f "$cli_dir"/*.a "$cli_dir"/*.dbg "$cli_dir"/*.so "$cli_dir"/*.dylib 2>/dev/null || true

# ---- 8. assemble UI archive dir (nkit-ui + nkds-ui + config) ----------------
ui_dir="/build/publish/gui/$RUNTIME"
[ -f /build/NKit.UI/nkit-ui.yaml ]  && cp /build/NKit.UI/nkit-ui.yaml "$ui_dir/"
mkdir -p "$ui_dir/defaults/fix"
find /src/NKit/defaults/fix -name "*.yaml" 2>/dev/null | while IFS= read -r f; do
    cp "$f" "$ui_dir/defaults/fix/"
done
[ -f /build/NKit.UI/ConfigInfo.txt ] && cp /build/NKit.UI/ConfigInfo.txt "$ui_dir/"
cp /build/publish/nkds-ui/$RUNTIME/nkds-ui "$ui_dir/"
rm -f "$ui_dir"/*.a "$ui_dir"/*.dbg 2>/dev/null || true

# ---- 9. create archives ------------------------------------------------------
VERSION_LABEL="${VERSION:-$(date +%Y%m%d)}"

CLI_ZIP="NKit_CLI_${RUNTIME}_${VERSION_LABEL}.zip"
echo "### creating $CLI_ZIP ###"
(cd "$cli_dir" && zip -r "/out/$CLI_ZIP" .)
echo "Created: $CLI_ZIP"

UI_ZIP="NKit_UI_${RUNTIME}_${VERSION_LABEL}.zip"
echo "### creating $UI_ZIP ###"
(cd "$ui_dir" && zip -r "/out/$UI_ZIP" .)
echo "Created: $UI_ZIP"

echo ""
echo "### done ###"
ls -lh /out/
