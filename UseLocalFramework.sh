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

FRAMEWORK_PROJ="../sakura/Sakura.Framework/Sakura.Framework.csproj"

pin_configuration() {
    sed -i.bak -E "s|(<ProjectReference Include=\"[^\"]*Sakura\.Framework\.csproj\")[^>]*>|\1 SetConfiguration=\"Configuration=$CONFIGURATION\" />|" "$1"

    rm -f "$1.bak"
}

for PROJ in "${PROJECTS[@]}"; do
    echo "Updating $PROJ..."

    if grep -q 'PackageReference Include="Sakura.Framework"' "$PROJ"; then
        dotnet remove "$PROJ" package Sakura.Framework
    fi

    dotnet add "$PROJ" reference "$FRAMEWORK_PROJ"

    sed -i.bak -E 's|(<ProjectReference Include=".*Sakura\.Framework\.SourceGenerators\.csproj")[^>]*>|\1 ReferenceOutputAssembly="false" OutputItemType="Analyzer" />|' "$PROJ"

    rm -f "${PROJ}.bak"

    pin_configuration "$PROJ"
done

for PROJ in "${DEPENDENT_PROJECTS[@]}"; do
    echo "Updating $PROJ..."

    dotnet add "$PROJ" reference "$FRAMEWORK_PROJ"

    pin_configuration "$PROJ"
done

echo "Local framework will build in $CONFIGURATION."
