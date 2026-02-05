#!/bin/bash
# Antty Bootstrap Installer for Linux/macOS
# This script downloads the repository and runs the full installer

set -e

echo "🚀 Antty Installer"
echo ""

TEMP_DIR=$(mktemp -d)
TAR_FILE="$TEMP_DIR/antty.tar.gz"

cleanup() {
    rm -rf "$TEMP_DIR"
}

trap cleanup EXIT

# Download repository as tar.gz
echo "📥 Downloading Antty..."
curl -fsSL "https://github.com/wisedev-pstach/Antty/archive/refs/heads/main.tar.gz" -o "$TAR_FILE"

# Extract
echo "📦 Extracting..."
tar -xzf "$TAR_FILE" -C "$TEMP_DIR"

# Run the real installer
echo "▶️  Running installer..."
echo ""

cd "$TEMP_DIR/Antty-main"
bash ./install.sh
