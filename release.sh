#!/bin/bash
set -e

PATCH="${1:-${PATCH:-0}}"
REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJ_DIR="$REPO_ROOT"
PROJ_PATH="Yuuki.Game.Desktop/Yuuki.Game.Desktop.csproj"

rm -rf "$PROJ_DIR/bin" "$PROJ_DIR/obj"
rm -rf "$REPO_ROOT/publish-osx" "$REPO_ROOT/publish-win-x64" "$REPO_ROOT/publish-win-x86" "$REPO_ROOT/publish-win-arm64"

YEAR=$(TZ='Asia/Bangkok' date +'%Y')
MM=$(TZ='Asia/Bangkok' date +'%-m')
DD=$(TZ='Asia/Bangkok' date +'%d')
VERSION="${YEAR}.${MM}${DD}.${PATCH}"

echo "Version: $VERSION"

if [ -n "$GITHUB_OUTPUT" ]; then
    echo "version=$VERSION" >> "$GITHUB_OUTPUT"
fi

cd "$PROJ_DIR"

echo "Running macOS Release Pipeline..."
dotnet publish "$PROJ_PATH" -c Release --self-contained -r osx-arm64 -o "$REPO_ROOT/publish-osx" -p:Version="$VERSION"

vpk pack --packId "yuuki" --packVersion "$VERSION" --packDir "$REPO_ROOT/publish-osx" --packTitle Yuuki --icon "Yuuki.Game.Desktop/icon.icns" --mainExe "Yuuki.Game.Desktop"

echo "Running Windows Release Pipeline..."
WIN_ARCHS=("win-arm64" "win-x64" "win-x86")

for ARCH in "${WIN_ARCHS[@]}"; do
    echo "--> Processing Windows Architecture: $ARCH"
    OUTPUT_DIR="$REPO_ROOT/publish-$ARCH"

    # Publish each target architecture
    dotnet publish "$PROJ_PATH" -c Release --self-contained -r "$ARCH" -o "$OUTPUT_DIR" -p:Version="$VERSION"

    # Pack for Windows using architecture-specific channels and icon.ico
    vpk [win] pack -u "yuuki" -v "$VERSION" -p "$OUTPUT_DIR" --channel "$ARCH" --icon "Yuuki.Game.Desktop/icon.ico" --mainExe "Yuuki.Game.Desktop.exe"
done

echo "All macOS and Windows releases built successfully!"
