#!/usr/bin/env bash
# Packs a published app with Velopack (wiki AUN-APP-UPDATES §5.2): the installer or bundle, and the update package.
# Usage: vpk-pack.sh <rid> <package-version> <publish-dir-or-.app> <main-exe> [more vpk options]   → out/vpk
# The tool's version is the library's (Directory.Packages.props).
set -euo pipefail
rid=$1 version=$2 dir=$3 exe=$4; shift 4
root="$(cd "$(dirname "$0")/.." && pwd)"
vpk=$(sed -n 's/.*Include="Velopack" Version="\([^"]*\)".*/\1/p' "$root/AlUmmahNow.VisualImporterUno/Directory.Packages.props")
[ -n "$vpk" ] || { echo "::error::no Velopack version in Directory.Packages.props"; exit 1; }
rm -rf out/vpk out/vpk-tool; mkdir -p out/vpk
dotnet tool install vpk --version "$vpk" --tool-path out/vpk-tool >/dev/null
DOTNET_ROLL_FORWARD=Major out/vpk-tool/vpk pack --packId AlUmmahNow.Tools --packVersion "$version" --packDir "$dir" --mainExe "$exe" \
  --packTitle "Al Ummah Now Tools" --packAuthors "Al Ummah Now" --channel "$rid" --runtime "$rid" --delta None --outputDir out/vpk "$@"
ls -l out/vpk
