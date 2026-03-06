#!/bin/bash

echo "Building NO ATC Mod..."
echo ""

# Check if .NET SDK is installed
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: .NET SDK not found. Please install .NET SDK 6.0 or later."
    exit 1
fi

echo "Restoring NuGet packages..."
dotnet restore
if [ $? -ne 0 ]; then
    echo "ERROR: Failed to restore packages."
    exit 1
fi

echo ""
echo "Building Release configuration..."
dotnet build -c Release
if [ $? -ne 0 ]; then
    echo "ERROR: Build failed."
    exit 1
fi

echo ""
echo "Build successful!"
echo "Output: bin/Release/NO_ATC_Mod.dll"
echo ""
echo "To install, copy the DLL to: Nuclear Option/BepInEx/plugins/NO_ATC_Mod/"
