#!/bin/bash
# Archive creation helper - sourced by build.sh
# Creates zip archives for distribution

create_archives() {
    local runtime="$1"
    local artifact_name="$2"
    local build_console="$3"
    local build_gui="$4"
    local build_nkds="$5"
    local build_nkds_ui="$6"

    # Use version for archive label if available, otherwise fall back to date
    local archive_label
    if [ -n "$VERSION" ]; then
        archive_label="$VERSION"
    else
        archive_label=$(date +%Y%m%d)
    fi

    # Console archive: NKit_{artifact}_CLI_{version}.zip
    if [[ "$build_console" == true ]]; then
        local cli_zip="NKit_${artifact_name}_CLI_${archive_label}.zip"
        echo "Creating: $cli_zip"

        local cli_dir="publish/console/$runtime"
        local nkds_dir="publish/nkds/$runtime"

        # Include nkds in the console archive if it was built
        if [[ "$build_nkds" == true ]] && [ -d "$nkds_dir" ]; then
            # Copy nkds binary into console dir for combined archive
            local nkds_bin="nkds"
            [[ "$runtime" == win-* ]] && nkds_bin="nkds.exe"
            if [ -f "$nkds_dir/$nkds_bin" ]; then
                cp "$nkds_dir/$nkds_bin" "$cli_dir/"
            fi
        fi

        # Include ConfigInfo.txt if present
        if [ -f "NKitApp/ConfigInfo.txt" ]; then
            cp "NKitApp/ConfigInfo.txt" "$cli_dir/"
        fi

        (cd "$cli_dir" && zip -r "$REPO_ROOT/$cli_zip" .)
        echo "Created: $cli_zip"
    fi

    # GUI archive: NKit_{artifact}_UI_{version}.zip
    if [[ "$build_gui" == true ]]; then
        local ui_zip="NKit_${artifact_name}_UI_${archive_label}.zip"
        echo "Creating: $ui_zip"

        if [[ "$runtime" == osx-* ]]; then
            # macOS: archive the .app bundles using ditto for proper resource fork handling
            local bundle_dir="publish/bundle/$runtime"
            if [ -d "$bundle_dir" ]; then
                if command -v ditto >/dev/null 2>&1; then
                    ditto -c -k --keepParent "$bundle_dir" "$REPO_ROOT/$ui_zip"
                else
                    (cd "$bundle_dir" && zip -r "$REPO_ROOT/$ui_zip" .)
                fi
            fi
        else
            # Windows/Linux: flat zip of gui + nkds-ui
            local gui_dir="publish/gui/$runtime"
            local nkds_ui_dir="publish/nkds-ui/$runtime"

            # Copy nkds-ui into gui dir for combined archive
            if [[ "$build_nkds_ui" == true ]] && [ -d "$nkds_ui_dir" ]; then
                local nkds_ui_bin="nkds-ui"
                [[ "$runtime" == win-* ]] && nkds_ui_bin="nkds-ui.exe"
                if [ -f "$nkds_ui_dir/$nkds_ui_bin" ]; then
                    cp "$nkds_ui_dir/$nkds_ui_bin" "$gui_dir/"
                fi
            fi

            (cd "$gui_dir" && zip -r "$REPO_ROOT/$ui_zip" .)
        fi
        echo "Created: $ui_zip"
    fi
}

# Make REPO_ROOT available if not already set
REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
