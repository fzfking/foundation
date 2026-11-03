#!/usr/bin/env bash
# Rebuilds the generator and copies the DLL into the Unity package (keeping the committed .meta).
set -euo pipefail
cd "$(dirname "$0")"
dotnet build -c Release -o ./bin
cp ./bin/Foundation.TypedIds.Generator.dll ../../Analyzers/Foundation.TypedIds.Generator.dll
echo "Generator copied to Analyzers."
