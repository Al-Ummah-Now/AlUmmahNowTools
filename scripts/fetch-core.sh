#!/usr/bin/env bash
# Downloads the pinned AlUmmahNow.Core package from its public release and verifies it.
set -euo pipefail
version=0.4.0-rc.5
sha256=c3f59a4f5cbf14b59182b34bbd95bd1463fb4cc0765c9a07d6b9e26bfa70047d
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$root/.nuget-local"
file="$root/.nuget-local/AlUmmahNow.Core.$version.nupkg"
if [ ! -f "$file" ]; then
  curl -fsSL --retry 5 --retry-delay 5 --retry-all-errors -o "$file" \
    "https://github.com/Al-Ummah-Now/AlUmmahNowCore/releases/download/v$version/AlUmmahNow.Core.$version.nupkg"
fi
if command -v sha256sum >/dev/null; then actual=$(sha256sum "$file" | cut -d' ' -f1); else actual=$(shasum -a 256 "$file" | cut -d' ' -f1); fi
if [ "$actual" != "$sha256" ]; then echo "checksum mismatch for $file" >&2; rm -f "$file"; exit 1; fi
echo "AlUmmahNow.Core $version verified"
