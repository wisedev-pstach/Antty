#!/bin/bash
# Antty Uninstallation Script for Linux/macOS

echo "🗑️  Uninstalling Antty..."
echo ""

INSTALL_DIR="$HOME/.local/share/antty"
SYMLINK_PATH="/usr/local/bin/antty"

# Remove symlink
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

# Remove installation directory
if [ -d "$INSTALL_DIR" ]; then
    echo "🗑️  Removing installation files from $INSTALL_DIR..."
    rm -rf "$INSTALL_DIR"
    echo "✓ Installation files removed"
else
    echo "✓ Installation directory not found"
fi

echo ""
echo "✅ Uninstallation complete!"
