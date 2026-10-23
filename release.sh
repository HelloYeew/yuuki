#!/bin/bash
set -e

PATCH="${1:-${PATCH:-0}}"
REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJ_DIR="$REPO_ROOT"
PROJ_PATH="Yuuki.Game.Desktop/Yuuki.Game.Desktop.csproj"
# The name the game goes by wherever it's shown; file names keep to "yuuki", as some can't end in a dot.
TITLE="yuuki."
# What every platform knows the game by: Android's application ID, the macOS bundle ID, and the Windows ID that groups its
# windows and shortcuts on the taskbar.
APP_ID="dev.helloyeew.yuuki"
ANDROID_PROJ_PATH="Yuuki.Game.Android/Yuuki.Game.Android.csproj"
# Where the Android build leaves its APK: beside the project, not in the publish folder.
ANDROID_OUT="Yuuki.Game.Android/bin/Release/net10.0-android"

rm -rf "$PROJ_DIR/bin" "$PROJ_DIR/obj"
rm -rf "$REPO_ROOT/publish-osx" "$REPO_ROOT/publish-win-x64" "$REPO_ROOT/publish-win-x86" "$REPO_ROOT/publish-win-arm64" "$REPO_ROOT/publish-linux-x64" "$REPO_ROOT/publish-linux-arm64" "$REPO_ROOT/publish-android" "$REPO_ROOT/bundle-osx"
rm -rf "$REPO_ROOT/$ANDROID_OUT"

YEAR=$(TZ='Asia/Bangkok' date +'%Y')
MM=$(TZ='Asia/Bangkok' date +'%-m')
DD=$(TZ='Asia/Bangkok' date +'%d')
VERSION="${YEAR}.${MM}${DD}.${PATCH}"

# Android's version code has to be a whole number that only grows, release after release: the date and the patch as
# YYMMDDPP (so up to 99 patches a day).
ANDROID_VERSION_CODE="${YEAR: -2}$(TZ='Asia/Bangkok' date +'%m')${DD}$(printf '%02d' "$PATCH")"

echo "Version: $VERSION"

if [ -n "$GITHUB_OUTPUT" ]; then
    echo "version=$VERSION" >> "$GITHUB_OUTPUT"
fi

cd "$PROJ_DIR"

echo "Running macOS Release Pipeline..."
dotnet publish "$PROJ_PATH" -c Release --self-contained -r osx-arm64 -o "$REPO_ROOT/publish-osx" -p:Version="$VERSION"

# Bundled first, to add what Velopack doesn't write itself: the name shown, and .osz files opening in the game. They're
# claimed the way osu! claims them, by extension, so with both installed the player picks; a file the game opens shows
# a document icon macOS draws from the game's.
vpk bundle --packId "yuuki" --packVersion "$VERSION" --packDir "$REPO_ROOT/publish-osx" --packTitle "$TITLE" --bundleId "$APP_ID" --icon "Yuuki.Game.Desktop/icon.icns" --mainExe "Yuuki.Game.Desktop" -o "$REPO_ROOT/bundle-osx"
OSX_APP=("$REPO_ROOT/bundle-osx/"*.app)
plutil -insert CFBundleDisplayName -string "$TITLE" "${OSX_APP[0]}/Contents/Info.plist"
plutil -insert CFBundleDocumentTypes -json '[{
    "CFBundleTypeName": "osu! beatmap archive",
    "CFBundleTypeRole": "Viewer",
    "LSHandlerRank": "Default",
    "CFBundleTypeExtensions": ["osz"],
    "CFBundleTypeMIMETypes": ["application/x-osu-beatmap-archive"],
    "CFBundleTypeIconSystemGenerated": true
}]' "${OSX_APP[0]}/Contents/Info.plist"

# macOS 26 and later draw the icon made in Icon Composer as Liquid Glass, the one iOS shows too, from the bundle's asset
# catalog; earlier systems take the flat copy actool draws into the same catalog. 12.0 is as far back as .NET runs.
# actool hands the icon to a helper of its own, which reads a relative path from wherever it was started.
mkdir -p "$REPO_ROOT/bundle-osx/icon"
xcrun actool "$REPO_ROOT/Yuuki.Game.iOS/AppIcon.icon" --compile "$REPO_ROOT/bundle-osx/icon" --platform macosx --minimum-deployment-target 12.0 --app-icon AppIcon --output-partial-info-plist "$REPO_ROOT/bundle-osx/icon/partial-info.plist" --output-format human-readable-text --errors --warnings
cp "$REPO_ROOT/bundle-osx/icon/Assets.car" "${OSX_APP[0]}/Contents/Resources/"
plutil -insert CFBundleIconName -string AppIcon "${OSX_APP[0]}/Contents/Info.plist"

# Game Mode, for a game in full screen: the category says it's a game, and the keys opt it in (LSSupportsGameMode from
# macOS 26, GCSupportsGameMode before).
plutil -replace LSApplicationCategoryType -string public.app-category.music-games "${OSX_APP[0]}/Contents/Info.plist"
plutil -replace LSSupportsGameMode -bool YES "${OSX_APP[0]}/Contents/Info.plist"
plutil -replace GCSupportsGameMode -bool YES "${OSX_APP[0]}/Contents/Info.plist"

vpk pack --packId "yuuki" --packVersion "$VERSION" --packDir "${OSX_APP[0]}" --packTitle "$TITLE" --mainExe "Yuuki.Game.Desktop"

echo "Running Windows Release Pipeline..."
WIN_ARCHS=("win-arm64" "win-x64" "win-x86")

for ARCH in "${WIN_ARCHS[@]}"; do
    echo "--> Processing Windows Architecture: $ARCH"
    OUTPUT_DIR="$REPO_ROOT/publish-$ARCH"

    # Publish each target architecture
    dotnet publish "$PROJ_PATH" -c Release --self-contained -r "$ARCH" -o "$OUTPUT_DIR" -p:Version="$VERSION"

    # Pack for Windows using architecture-specific channels and icon.ico, which is the installer's: the game's own .exe
    # has it from the project, as has every shortcut to it. Installing it puts it in "Open with" for .osz files
    # (WindowsFileAssociations).
    vpk [win] pack -u "yuuki" -v "$VERSION" -p "$OUTPUT_DIR" --channel "$ARCH" --packTitle "$TITLE" --aumid "$APP_ID" --icon "Yuuki.Game.Desktop/icon.ico" --mainExe "Yuuki.Game.Desktop.exe"
done

echo "Running Linux Release Pipeline..."
# .NET has no 32-bit Linux, so it's the two 64-bit architectures, each as an AppImage. Building those on a Mac needs
# mksquashfs (brew install squashfs).
LINUX_ARCHS=("linux-arm64" "linux-x64")

for ARCH in "${LINUX_ARCHS[@]}"; do
    echo "--> Processing Linux Architecture: $ARCH"
    OUTPUT_DIR="$REPO_ROOT/publish-$ARCH"

    # Publish each target architecture
    dotnet publish "$PROJ_PATH" -c Release --self-contained -r "$ARCH" -o "$OUTPUT_DIR" -p:Version="$VERSION"

    # Pack for Linux using architecture-specific channels and icon.png (an AppImage takes a PNG)
    vpk "[linux]" pack -u "yuuki" -v "$VERSION" -p "$OUTPUT_DIR" --channel "$ARCH" --packTitle "$TITLE" --icon "Yuuki.Game.Desktop/icon.png" --categories Game --mainExe "Yuuki.Game.Desktop"
done

echo "Running Android Release Pipeline..."
# Needs the .NET Android workload (dotnet workload install android) and the Android SDK. Velopack has no Android, so
# this makes an APK to sideload and an AAB for Google Play.

ANDROID_ARGS=()

# The JDK the Android build runs on: JAVA_HOME, or else Homebrew's OpenJDK 21 where it's installed.
JDK="${JAVA_HOME:-/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home}"
if [ -d "$JDK" ]; then
    ANDROID_ARGS+=("-p:JavaSdkDirectory=$JDK")
fi

# Signed with the release keystore when one's given: ANDROID_KEYSTORE (its path) and ANDROID_KEY_ALIAS, with the
# passwords in ANDROID_KEYSTORE_PASSWORD and ANDROID_KEY_PASSWORD, which are read from the environment rather than
# passed on the command line. Without one it's the debug key: fine to try, but an update has to be signed with the
# same key as what it replaces, so a release signed with it can't be followed by one signed properly.
if [ -n "$ANDROID_KEYSTORE" ]; then
    # A PKCS12 keystore (keytool's default) has the one password for the store and its key.
    export ANDROID_KEY_PASSWORD="${ANDROID_KEY_PASSWORD:-$ANDROID_KEYSTORE_PASSWORD}"

    ANDROID_ARGS+=(
        -p:AndroidKeyStore=true
        "-p:AndroidSigningKeyStore=$ANDROID_KEYSTORE"
        "-p:AndroidSigningKeyAlias=$ANDROID_KEY_ALIAS"
        -p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD
        -p:AndroidSigningKeyPass=env:ANDROID_KEY_PASSWORD
    )
else
    echo "warning: ANDROID_KEYSTORE isn't set, so the Android build is signed with the debug key (fine to test, not to ship)"
fi

dotnet publish "$ANDROID_PROJ_PATH" -c Release -f net10.0-android -o "$REPO_ROOT/publish-android" \
    -p:Version="$VERSION" -p:ApplicationDisplayVersion="$VERSION" -p:ApplicationVersion="$ANDROID_VERSION_CODE" \
    "-p:AndroidPackageFormats=apk%3Baab" "${ANDROID_ARGS[@]}"

mkdir -p "$REPO_ROOT/Releases"
cp "$ANDROID_OUT/dev.helloyeew.yuuki-Signed.apk" "$REPO_ROOT/Releases/yuuki-$VERSION-android-arm64.apk"
cp "$ANDROID_OUT/dev.helloyeew.yuuki-Signed.aab" "$REPO_ROOT/Releases/yuuki-$VERSION-android-arm64.aab"

echo "All macOS, Windows, Linux and Android releases built successfully!"
