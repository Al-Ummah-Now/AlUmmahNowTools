#!/usr/bin/env bash
# Moves everything that isn't native code from an .app's Contents/MacOS to Contents/Resources/app and leaves links.
# codesign keeps the signature of such a file in an extended attribute, which an update package doesn't carry, so an
# updated app's signature would no longer verify. In Resources the files are sealed by the bundle's own signature.
# Usage: mac-bundle.sh <App.app>   (before signing; any signature it has is replaced later)
set -euo pipefail
macos="$1/Contents/MacOS" resources="$1/Contents/Resources/app"
mkdir -p "$resources"
native() { file -b "$1" | grep -q 'Mach-O'; }
has_native() { while IFS= read -r -d '' f; do native "$f" && return 0; done < <(find "$1" -type f -print0); return 1; }
move() { # <path under MacOS>
  local rel=$1 up
  up=$(dirname "$rel" | awk -F/ '{ if ($0 == ".") print ""; else { s = ""; for (i = 1; i <= NF; i++) s = s "../"; print s } }')
  mkdir -p "$resources/$(dirname "$rel")"
  mv "$macos/$rel" "$resources/$rel"
  ln -s "${up}../Resources/app/$rel" "$macos/$rel"
}
walk() { # <folder under MacOS, "" for the top>
  local entry rel
  for entry in "$macos/$1"*; do
    rel=${entry#"$macos"/}
    if [ -L "$entry" ]; then continue
    elif [ -d "$entry" ]; then if has_native "$entry"; then walk "$rel/"; else move "$rel"; fi
    elif ! native "$entry"; then move "$rel"; fi
  done
}
walk ""
broken=$(find "$macos" -type l ! -exec test -e {} \; -print)
[ -z "$broken" ] || { echo "::error::broken links: $broken"; exit 1; }
echo "$(find "$macos" -type l | wc -l | tr -d ' ') links, $(find "$macos" -type f | wc -l | tr -d ' ') native files in Contents/MacOS"
