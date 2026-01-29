#!/bin/bash
# Antty Installation Script for Linux/macOS
# This script publishes the application and creates a symlink in /usr/local/bin

echo "🚀 Installing Antty..."
echo ""

# Detect OS and architecture
OS=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

# Map architecture names
if [ "$ARCH" = "x86_64" ]; then
    ARCH="x64"
elif [ "$ARCH" = "aarch64" ] || [ "$ARCH" = "arm64" ]; then
    ARCH="arm64"
fi

# Determine runtime identifier
if [ "$OS" = "linux" ]; then
    RID="linux-$ARCH"
elif [ "$OS" = "darwin" ]; then
    RID="osx-$ARCH"
else
    echo "❌ Unsupported operating system: $OS"
    exit 1
fi

echo "📦 Publishing application for $RID..."

# Build and publish the application
dotnet publish src/Antty.csproj -c Release -r $RID --self-contained false -o publish/$RID

if [ $? -ne 0 ]; then
    echo "❌ Build failed!"
    exit 1
fi

echo "✓ Build successful!"
echo ""

# Get the published executable path
PUBLISH_PATH="$(pwd)/publish/$RID"
EXE_PATH="$PUBLISH_PATH/Antty"

if [ ! -f "$EXE_PATH" ]; then
    echo "❌ Executable not found at: $EXE_PATH"
    exit 1
fi

# Make the executable runnable
chmod +x "$EXE_PATH"

# Create symlink in /usr/local/bin
echo "🔧 Creating symlink..."
SYMLINK_PATH="/usr/local/bin/antty"

# Check if we need sudo
if [ -w "/usr/local/bin" ]; then
    ln -sf "$EXE_PATH" "$SYMLINK_PATH"
else
    echo "Creating symlink requires sudo privileges..."
    sudo ln -sf "$EXE_PATH" "$SYMLINK_PATH"
fi

if [ $? -eq 0 ]; then
    echo "✓ Symlink created: $SYMLINK_PATH"
else
    echo "❌ Failed to create symlink"
    exit 1
fi

echo ""
echo "✅ Installation complete!"
echo ""
echo "You can now use 'antty' from any directory!"
echo ""
echo "Try it now: antty"
