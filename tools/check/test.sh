#!/bin/bash
# Runs the game's EditMode tests outside Unity, against sim/UnityStub.cs: a small stand-in for
# the parts of UnityEngine the core uses. Usage: tools/check/test.sh [filter] [lines]
# The stub's Perlin noise is not Unity's, so the islands here are siblings of the real ones:
# good for rules and balance, not a substitute for the test run inside the CI build.
export PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export SKYFORGE_ASSETS="$ROOT/Assets"
cd "$ROOT/tools/check/sim"
if [ -n "$1" ]; then F="--filter $1"; fi
dotnet test -v q -nologo $F --logger "console;verbosity=normal" 2>&1 | grep -vE "^\s*$|   at |Stack Trace|Трассировка" | sed 's#.*/Assets/Skyforge/##; s/ \[\/.*//' | head -${2:-80}
