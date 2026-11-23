#!/usr/bin/env bash
# Builds the Al Ummah Now Tools .deb from a self-contained linux-x64 publish (after AlUmmahNowDesktop's
# scripts/build-deb.sh). Usage: build-deb.sh <publish-dir> <version> <icon-512.png> <out-dir>
# Installs to /opt/alummahnow-tools, links /usr/bin/alummahnow-tools, adds the desktop entry and the icon.
set -euo pipefail
publish=$1 version=$2 icon=$3 out=$4
package=alummahnow-tools appid=net.alummahnow.tools exe=AlUmmahNow.VisualImporterUno
here="$(cd "$(dirname "$0")/.." && pwd)"
root=$(mktemp -d)
mkdir -p "$root/DEBIAN" "$root/opt/$package" "$root/usr/bin" "$root/usr/share/applications"
cp -r "$publish"/. "$root/opt/$package/"
chmod 755 "$root/opt/$package/$exe"
ln -s "/opt/$package/$exe" "$root/usr/bin/$package"
install -m644 "$here/packaging/linux/$appid.desktop" "$root/usr/share/applications/$appid.desktop"
install -Dm644 "$icon" "$root/usr/share/icons/hicolor/512x512/apps/$appid.png"
install -Dm644 "$here/packaging/licenses/THIRD-PARTY-NOTICES.md" "$root/usr/share/doc/$package/THIRD-PARTY-NOTICES.md"
size=$(du -sk "$root" | cut -f1)
cat > "$root/DEBIAN/control" <<CONTROL
Package: $package
Version: $version
Architecture: amd64
Maintainer: Al Ummah Now (https://github.com/Al-Ummah-Now/AlUmmahNowTools)
Installed-Size: $size
Depends: libx11-6, libxcursor1, libxi6, libxrandr2, libxext6, libfontconfig1, libfreetype6, libgl1, libegl1, libsecret-1-0, libicu74 | libicu72 | libicu70 | libicu-dev, ca-certificates, xdg-utils
Recommends: ffmpeg, libvulkan1, gnome-keyring
Section: video
Priority: optional
Homepage: https://github.com/Al-Ummah-Now/AlUmmahNowTools
Description: Al Ummah Now Tools - staff app for bulk imports into Al Ummah Now
CONTROL
mkdir -p "$out"
dpkg-deb --root-owner-group --build "$root" "$out/${package}_${version}_amd64.deb"
rm -rf "$root"
