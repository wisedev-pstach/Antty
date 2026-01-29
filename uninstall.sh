#!/bin/bash
# Antty Uninstallation Script for Linux/macOS

echo "🗑️  Uninstalling Antty..."
echo ""

# Remove symlink
SYMLINK_PATH="/usr/local/bin/antty"

if [ -L "$SYMLINK_PATH" ]; then
    echo "🔧 Removing symlink..."
    if [ -w "/usr/local/bin" ]; then
        rm "$SYMLINK_PATH"
    else
        echo "Removing symlink requires sudo privileges..."
        sudo rm "$SYMLINK_PATH"
    fi
    
    if [ $? -eq 0 ]; then
        echo "✓ Symlink removed"
    else
        echo "❌ Failed to remove symlink"
        exit 1
    fi
else
    echo "✓ Symlink not found"
fi

# Remove published files
PUBLISH_DIR="$(pwd)/publish"

if [ -d "$PUBLISH_DIR" ]; then
    echo "🗑️  Removing published files..."
    rm -rf "$PUBLISH_DIR"
    echo "✓ Published files removed"
else
    echo "✓ No published files found"
fi

echo ""
echo "✅ Uninstallation complete!"
