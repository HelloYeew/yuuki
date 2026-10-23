#!/bin/bash

set -e

CONFIGURATION="${1:-Release}"

PROJECTS=(
    "Yuuki.Game/Yuuki.Game.csproj"
)

DEPENDENT_PROJECTS=(
    "Yuuki.Game.Desktop/Yuuki.Game.Desktop.csproj"
    "Yuuki.Game.Tests/Yuuki.Game.Tests.csproj"
)

# Heads that reference the framework's Android library too.
ANDROID_PROJECTS=(
    "Yuuki.Game.Android/Yuuki.Game.Android.csproj"
)

# And its iOS library.
IOS_PROJECTS=(
    "Yuuki.Game.iOS/Yuuki.Game.iOS.csproj"
)

FRAMEWORK_PROJ="../sakura/Sakura.Framework/Sakura.Framework.csproj"
ANDROID_FRAMEWORK_PROJ="../sakura/Sakura.Framework.Android/Sakura.Framework.Android.csproj"
# The framework's package brings its source generator along; a project reference doesn't, so the game gets it here.
GENERATOR_PROJ="../sakura/Sakura.Framework.SourceGenerators/Sakura.Framework.SourceGenerators.csproj"
# The same goes for the Android library's build targets, which the heads import instead (relative to the head's folder).
ANDROID_TARGETS='..\..\sakura\Sakura.Framework.Android\buildTransitive\Sakura.Framework.Android.targets'
IOS_FRAMEWORK_PROJ="../sakura/Sakura.Framework.iOS/Sakura.Framework.iOS.csproj"
# The iOS library has build props as well as targets: props go first in the project, targets last.
IOS_PROPS='..\..\sakura\Sakura.Framework.iOS\buildTransitive\Sakura.Framework.iOS.props'
IOS_TARGETS='..\..\sakura\Sakura.Framework.iOS\buildTransitive\Sakura.Framework.iOS.targets'

pin_configuration() {
    sed -i.bak -E "s|(<ProjectReference Include=\"[^\"]*Sakura\.Framework(\.Android\|\.iOS)?\.csproj\")[^>]*>|\1 SetConfiguration=\"Configuration=$CONFIGURATION\" />|" "$1"

    rm -f "$1.bak"
}

for PROJ in "${PROJECTS[@]}"; do
    echo "Updating $PROJ..."

    if grep -q 'PackageReference Include="Sakura.Framework"' "$PROJ"; then
        dotnet remove "$PROJ" package Sakura.Framework
    fi

    dotnet add "$PROJ" reference "$FRAMEWORK_PROJ" "$GENERATOR_PROJ"

    sed -i.bak -E 's|(<ProjectReference Include=".*Sakura\.Framework\.SourceGenerators\.csproj")[^>]*>|\1 ReferenceOutputAssembly="false" OutputItemType="Analyzer" />|' "$PROJ"

    rm -f "${PROJ}.bak"

    pin_configuration "$PROJ"
done

for PROJ in "${DEPENDENT_PROJECTS[@]}"; do
    echo "Updating $PROJ..."

    dotnet add "$PROJ" reference "$FRAMEWORK_PROJ"

    pin_configuration "$PROJ"
done

for PROJ in "${ANDROID_PROJECTS[@]}"; do
    echo "Updating $PROJ..."

    if grep -q 'PackageReference Include="Sakura.Framework.Android"' "$PROJ"; then
        dotnet remove "$PROJ" package Sakura.Framework.Android
    fi

    dotnet add "$PROJ" reference "$FRAMEWORK_PROJ" "$ANDROID_FRAMEWORK_PROJ"

    pin_configuration "$PROJ"

    if ! grep -q 'Sakura\.Framework\.Android\.targets' "$PROJ"; then
        # Read from the environment rather than with awk -v, which would take the path's backslashes for escapes.
        IMPORT_LINE="  <Import Project=\"$ANDROID_TARGETS\" />" awk '/<\/Project>/ { print ENVIRON["IMPORT_LINE"] } { print }' "$PROJ" > "$PROJ.tmp"
        mv "$PROJ.tmp" "$PROJ"
    fi
done

for PROJ in "${IOS_PROJECTS[@]}"; do
    echo "Updating $PROJ..."

    if grep -q 'PackageReference Include="Sakura.Framework.iOS"' "$PROJ"; then
        dotnet remove "$PROJ" package Sakura.Framework.iOS
    fi

    dotnet add "$PROJ" reference "$FRAMEWORK_PROJ" "$IOS_FRAMEWORK_PROJ"

    pin_configuration "$PROJ"

    if ! grep -q 'Sakura\.Framework\.iOS\.props' "$PROJ"; then
        IMPORT_LINE="  <Import Project=\"$IOS_PROPS\" />" awk '{ print } /<Project Sdk=/ && !done { print ENVIRON["IMPORT_LINE"]; done = 1 }' "$PROJ" > "$PROJ.tmp"
        mv "$PROJ.tmp" "$PROJ"
    fi

    if ! grep -q 'Sakura\.Framework\.iOS\.targets' "$PROJ"; then
        IMPORT_LINE="  <Import Project=\"$IOS_TARGETS\" />" awk '/<\/Project>/ { print ENVIRON["IMPORT_LINE"] } { print }' "$PROJ" > "$PROJ.tmp"
        mv "$PROJ.tmp" "$PROJ"
    fi
done

echo "Local framework will build in $CONFIGURATION."
