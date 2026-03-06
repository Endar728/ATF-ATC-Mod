@echo off
echo Building NO ATC Mod...
echo.

REM Check if .NET SDK is installed
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Please install .NET SDK 6.0 or later.
    pause
    exit /b 1
)

echo Restoring NuGet packages...
dotnet restore
if errorlevel 1 (
    echo ERROR: Failed to restore packages.
    pause
    exit /b 1
)

echo.
echo Building Release configuration...
dotnet build -c Release
if errorlevel 1 (
    echo ERROR: Build failed.
    pause
    exit /b 1
)

echo.
echo Build successful!
echo Output: bin\Release\NO_ATC_Mod.dll
echo.
echo To install, copy the DLL to: Nuclear Option\BepInEx\plugins\NO_ATC_Mod\
echo.
pause
