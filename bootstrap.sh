#!/bin/bash
# Antty Bootstrap Installer for Linux/macOS - Fully Self-Contained

set -e

echo "🚀 Antty Installer"
echo ""

TEMP_DIR=$(mktemp -d)
TAR_FILE="$TEMP_DIR/antty.tar.gz"
INSTALL_DIR="$HOME/.local/share/antty"
BIN_LINK="/usr/local/bin/antty"

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

PROJECT_ROOT="$TEMP_DIR/Antty-main"
echo ""

# Check for .NET
echo "🔍 Checking for .NET SDK..."
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo "✓ Found .NET SDK version: $DOTNET_VERSION"
else
    echo "⚙️  Installing .NET 10 SDK..."
    DOTNET_INSTALL_DIR="$HOME/.dotnet"
    DOTNET_INSTALLER="$TEMP_DIR/dotnet-install.sh"
    
    curl -fsSL "https://dot.net/v1/dotnet-install.sh" -o "$DOTNET_INSTALLER"
    chmod +x "$DOTNET_INSTALLER"
    bash "$DOTNET_INSTALLER" --channel 10.0 --install-dir "$DOTNET_INSTALL_DIR"
    
    export PATH="$DOTNET_INSTALL_DIR:$PATH"
    
    # Add to shell profile
    for PROFILE in "$HOME/.bashrc" "$HOME/.zshrc" "$HOME/.profile"; do
        if [ -f "$PROFILE" ]; then
            if ! grep -q "/.dotnet" "$PROFILE"; then
                echo "export PATH=\"\$HOME/.dotnet:\$PATH\"" >> "$PROFILE"
            fi
        fi
    done
    
    echo "✓ .NET installed"
fi

echo ""

# Build and publish
echo "🔨 Building Antty..."
cd "$PROJECT_ROOT"
dotnet publish src/Antty.csproj --configuration Release --output publish --framework net10.0

if [ $? -ne 0 ]; then
    echo "❌ Build failed!"
    exit 1
fi

echo "✓ Build successful!"
echo ""

# Copy to permanent location
echo "📂 Installing to $INSTALL_DIR..."
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
cp -r publish/* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/Antty"

echo "✓ Files copied"
echo ""

# Create symlink
echo "🔗 Creating symlink..."
if [ -w "/usr/local/bin" ]; then
    rm -f "$BIN_LINK"
    ln -s "$INSTALL_DIR/Antty" "$BIN_LINK"
else
    echo "Creating symlink requires sudo..."
    sudo rm -f "$BIN_LINK"
    sudo ln -s "$INSTALL_DIR/Antty" "$BIN_LINK"
fi

echo "✓ Symlink created at $BIN_LINK"
echo ""
echo "✅ Installation complete!"
echo ""
echo "⚠️  IMPORTANT: Restart your terminal, then run 'antty' from any directory!"
echo ""
