#!/bin/bash
# Compiles the game's C# against Unity's reference assemblies, without Unity and without a CI
# build. Catches compile errors in seconds. Needs the .NET 8 SDK (looked for in ~/.dotnet too);
# the reference assemblies are downloaded once into ~/.cache/skyforge-check.
# uGUI and URP have no reference assemblies: compile/Stubs.cs stands in for the few types used.
set -e
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
CACHE=$HOME/.cache/skyforge-check
if [ ! -d "$CACHE/um/lib/net45" ]; then
  mkdir -p "$CACHE/um"
  curl -sL -o "$CACHE/um.nupkg" "https://nuget.bepinex.dev/v3/package/unityengine.modules/6000.3.26/unityengine.modules.6000.3.26.nupkg"
  (cd "$CACHE/um" && unzip -qo ../um.nupkg)
fi
cd "$(dirname "$0")/compile"
dotnet build -v q -nologo 2>&1 | grep -E "error|warning CS(0219|0168|0162)" | sed 's/ \[\/.*//; s#.*/Assets/Skyforge/##' | sort -u | head -${1:-60} || true
echo "check done"
