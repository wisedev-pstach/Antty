#!/bin/bash
# Antty Installation Script for Linux/macOS
# This script publishes the application and creates a launcher in /usr/local/bin

echo "🚀 Installing Antty..."
echo ""

# Get the project root directory
PROJECT_ROOT="$(pwd)"

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
