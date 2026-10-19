#!/bin/bash

set -e

PROJECTS=(
    "Yuuki.Game/Yuuki.Game.csproj"
)

FRAMEWORK_PROJ="../sakura/Sakura.Framework/Sakura.Framework.csproj"

for PROJ in "${PROJECTS[@]}"; do
    echo "Updating $PROJ..."

    dotnet remove "$PROJ" package Sakura.Framework

    dotnet add "$PROJ" reference "$FRAMEWORK_PROJ"

    sed -i.bak -E 's|(<ProjectReference Include=".*Sakura\.Framework\.SourceGenerators\.csproj")[^>]*>|\1 ReferenceOutputAssembly="false" OutputItemType="Analyzer" />|' "$PROJ"
    
    rm -f "${PROJ}.bak"
done
