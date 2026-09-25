#!/bin/bash
# macOS App Bundle creation helper - sourced by build.sh

create_nkit_macos_bundle() {
    local runtime="$1"

    if [ ! -f "publish/gui/$runtime/nkit-ui" ]; then
        echo "GUI app not found, skipping NKit macOS bundle"
        return 0
    fi

    local app_path="publish/bundle/$runtime/NKit.app"
    local contents="$app_path/Contents"
    local macos="$contents/MacOS"
    local resources="$contents/Resources"

    echo "Creating NKit.app bundle..."
    mkdir -p "$macos" "$resources"

    # Copy executable
    cp "publish/gui/$runtime/nkit-ui" "$macos/"
    chmod +x "$macos/nkit-ui"

    # Copy native libraries (SkiaSharp, HarfBuzz etc)
    find "publish/gui/$runtime" -name "*.dylib" -maxdepth 1 -exec cp {} "$macos/" \; 2>/dev/null || true

    # Remove statically-linked GrindCore dylib if present
    rm -f "$macos/libGrindCore.dylib" 2>/dev/null || true

    # Copy fix files to Resources
    mkdir -p "$resources/fix"
    for fix_file in fix_dreamcast.yaml fix_gamecube.yaml fix_ps3.yaml fix_wii.yaml; do
        if [ -f "publish/gui/$runtime/defaults/fix/$fix_file" ]; then
            cp "publish/gui/$runtime/defaults/fix/$fix_file" "$resources/fix/"
        elif [ -f "NKitApp/defaults/fix/$fix_file" ]; then
            cp "NKitApp/defaults/fix/$fix_file" "$resources/fix/"
        fi
    done

    # Copy template config to Resources
    if [ -f "NKitApp/defaults/nkit.yaml" ]; then
        cp "NKitApp/defaults/nkit.yaml" "$resources/"
    fi

    # Copy other default subdirs
    for subdir in dats keys scans; do
        if [ -d "NKitApp/defaults/$subdir" ]; then
            cp -r "NKitApp/defaults/$subdir" "$resources/"
        fi
    done

    # Place GUI config NEXT TO the .app bundle (portable mode)
    local bundle_parent="publish/bundle/$runtime"
    if [ -f "NKit.UI/nkit-ui.yaml" ]; then
        cp "NKit.UI/nkit-ui.yaml" "$bundle_parent/"
    fi

    # Write Info.plist
    cat > "$contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>com.nanook.nkit</string>
  <key>CFBundleName</key>
  <string>NKit</string>
  <key>CFBundleDisplayName</key>
  <string>NKit</string>
  <key>CFBundleExecutable</key>
  <string>nkit-ui</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSMinimumSystemVersion</key>
  <string>10.13</string>
</dict>
</plist>
PLIST

    # Create ad-hoc signing helper
    cat > "$bundle_parent/RunMeToSignNKit.command" << 'SIGN'
#!/bin/bash
cd "$(dirname "$0")"
codesign --force --deep --sign - NKit.app
echo "NKit.app signed (ad-hoc). Press any key to close."
read -n 1
SIGN
    chmod +x "$bundle_parent/RunMeToSignNKit.command"

    echo "Created: $app_path"
}

create_nkds_ui_macos_bundle() {
    local runtime="$1"

    if [ ! -f "publish/nkds-ui/$runtime/nkds-ui" ]; then
        echo "NKDS.UI app not found, skipping NkdsUi macOS bundle"
        return 0
    fi

    local app_path="publish/bundle/$runtime/NkdsUi.app"
    local contents="$app_path/Contents"
    local macos="$contents/MacOS"
    local resources="$contents/Resources"

    echo "Creating NkdsUi.app bundle..."
    mkdir -p "$macos" "$resources"

    # Copy executable
    cp "publish/nkds-ui/$runtime/nkds-ui" "$macos/"
    chmod +x "$macos/nkds-ui"

    # Copy native libraries
    find "publish/nkds-ui/$runtime" -name "*.dylib" -maxdepth 1 -exec cp {} "$macos/" \; 2>/dev/null || true
    rm -f "$macos/libGrindCore.dylib" 2>/dev/null || true

    # Copy fix files to Resources
    mkdir -p "$resources/fix"
    for fix_file in fix_dreamcast.yaml fix_gamecube.yaml fix_ps3.yaml fix_wii.yaml; do
        if [ -f "publish/nkds-ui/$runtime/defaults/fix/$fix_file" ]; then
            cp "publish/nkds-ui/$runtime/defaults/fix/$fix_file" "$resources/fix/"
        elif [ -f "NKitApp/defaults/fix/$fix_file" ]; then
            cp "NKitApp/defaults/fix/$fix_file" "$resources/fix/"
        fi
    done

    # Copy fix-info.yaml if present
    if [ -f "publish/nkds-ui/$runtime/defaults/fix-info.yaml" ]; then
        cp "publish/nkds-ui/$runtime/defaults/fix-info.yaml" "$resources/"
    elif [ -f "NKitApp/defaults/fix-info.yaml" ]; then
        cp "NKitApp/defaults/fix-info.yaml" "$resources/"
    fi

    # Copy template config to Resources
    if [ -f "NKitApp/defaults/nkit.yaml" ]; then
        cp "NKitApp/defaults/nkit.yaml" "$resources/"
    fi

    # Write Info.plist
    cat > "$contents/Info.plist" << 'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key>
  <string>com.nanook.nkds-ui</string>
  <key>CFBundleName</key>
  <string>NkdsUi</string>
  <key>CFBundleDisplayName</key>
  <string>NKDS UI</string>
  <key>CFBundleExecutable</key>
  <string>nkds-ui</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0</string>
  <key>CFBundleVersion</key>
  <string>1</string>
  <key>LSMinimumSystemVersion</key>
  <string>10.13</string>
</dict>
</plist>
PLIST

    # Create ad-hoc signing helper
    local bundle_parent="publish/bundle/$runtime"
    cat > "$bundle_parent/RunMeToSignNkdsUi.command" << 'SIGN'
#!/bin/bash
cd "$(dirname "$0")"
codesign --force --deep --sign - NkdsUi.app
echo "NkdsUi.app signed (ad-hoc). Press any key to close."
read -n 1
SIGN
    chmod +x "$bundle_parent/RunMeToSignNkdsUi.command"

    echo "Created: $app_path"
}
