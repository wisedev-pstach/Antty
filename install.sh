#!/bin/bash
# Antty Installation Script for Linux/macOS
# This script publishes the application and creates a launcher in /usr/local/bin

echo "🚀 Installing Antty..."
echo ""

# Get the project root directory
PROJECT_ROOT="$(pwd)"

# Check if .NET SDK is installed
echo "🔍 Checking for .NET SDK..."
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo "✓ Found .NET SDK version: $DOTNET_VERSION"
else
    echo "⚠ .NET SDK not found. Installing .NET 10..."
    echo ""
    
    # Download and run the official .NET install script
    INSTALL_SCRIPT="/tmp/dotnet-install.sh"
    DOTNET_INSTALL_DIR="$HOME/.dotnet"
    
    echo "📥 Downloading .NET installer..."
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$INSTALL_SCRIPT"
    
    if [ $? -ne 0 ]; then
        echo "❌ Failed to download .NET installer"
        exit 1
    fi
    
    chmod +x "$INSTALL_SCRIPT"
    
    echo "📦 Installing .NET 10 SDK (this may take a few minutes)..."
    "$INSTALL_SCRIPT" --channel 10.0 --install-dir "$DOTNET_INSTALL_DIR"
    
    if [ $? -ne 0 ]; then
        echo "❌ Failed to install .NET SDK"
        echo "Please install .NET 10 manually from: https://dotnet.microsoft.com/download"
        rm -f "$INSTALL_SCRIPT"
        exit 1
    fi
    
    # Add .NET to PATH for current session
    export PATH="$DOTNET_INSTALL_DIR:$PATH"
    
    # Add .NET to shell profile for future sessions
    PROFILE_FILE=""
    if [ -f "$HOME/.bashrc" ]; then
        PROFILE_FILE="$HOME/.bashrc"
    elif [ -f "$HOME/.zshrc" ]; then
        PROFILE_FILE="$HOME/.zshrc"
    elif [ -f "$HOME/.profile" ]; then
        PROFILE_FILE="$HOME/.profile"
    fi
    
    if [ -n "$PROFILE_FILE" ]; then
        if ! grep -q "export PATH=\"\$HOME/.dotnet:\$PATH\"" "$PROFILE_FILE"; then
            echo "" >> "$PROFILE_FILE"
            echo "# Added by Antty installer" >> "$PROFILE_FILE"
            echo "export PATH=\"\$HOME/.dotnet:\$PATH\"" >> "$PROFILE_FILE"
            echo "✓ Added .NET to PATH in $PROFILE_FILE"
        fi
    fi
    
    # Verify installation
    DOTNET_VERSION=$("$DOTNET_INSTALL_DIR/dotnet" --version 2>/dev/null)
    if [ $? -eq 0 ]; then
        echo "✓ .NET SDK $DOTNET_VERSION installed successfully!"
    else
        echo "⚠ .NET installation completed but verification failed. You may need to restart your terminal."
    fi
    
    # Clean up installer script
    rm -f "$INSTALL_SCRIPT"
    echo ""
fi

echo ""

echo "📦 Publishing application..."

# Build and publish the application (framework-dependent to avoid LlamaSharp conflicts)
# Using framework-dependent deployment - no runtime identifier to avoid native lib conflicts
dotnet publish src/Antty.csproj \
    --configuration Release \
    --output publish \
    --framework net10.0

if [ $? -ne 0 ]; then
    echo "❌ Build failed!"
    exit 1
fi

echo "✓ Build successful!"
echo ""

# Get the published executable path
PUBLISH_PATH="$PROJECT_ROOT/publish"
EXE_PATH="$PUBLISH_PATH/Antty"

if [ ! -f "$EXE_PATH" ]; then
    echo "❌ Executable not found at: $EXE_PATH"
    exit 1
fi

# Make the executable runnable
chmod +x "$EXE_PATH"

# Create wrapper script in /usr/local/bin
echo "🔧 Creating launcher script..."
LAUNCHER_PATH="/usr/local/bin/antty"

# Create wrapper script that preserves user's working directory
# Set DYLD_LIBRARY_PATH so native libraries are found
WRAPPER_SCRIPT="#!/bin/bash
# Antty launcher - sets library path for native libraries while preserving working directory
export DYLD_LIBRARY_PATH=\"$PUBLISH_PATH:\$DYLD_LIBRARY_PATH\"
exec \"$EXE_PATH\" \"\$@\"
"

# Check if we need sudo
if [ -w "/usr/local/bin" ]; then
    echo "$WRAPPER_SCRIPT" > "$LAUNCHER_PATH"
    chmod +x "$LAUNCHER_PATH"
else
    echo "Creating launcher requires sudo privileges..."
    echo "$WRAPPER_SCRIPT" | sudo tee "$LAUNCHER_PATH" > /dev/null
    sudo chmod +x "$LAUNCHER_PATH"
fi

if [ $? -eq 0 ]; then
    echo "✓ Launcher created: $LAUNCHER_PATH"
else
    echo "❌ Failed to create launcher"
    exit 1
fi

echo ""
echo "✅ Installation complete!"
echo ""
echo "You can now use 'antty' from any directory!"
echo ""
echo "Try it now: antty"
