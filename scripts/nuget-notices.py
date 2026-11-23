#!/usr/bin/env python3
"""Lists the NuGet packages the desktop app ships, with licence, copyright and project URL from each nuspec.

Usage: nuget-notices.py <project.assets.json> <out.txt>
Reads the restored app's assets file (net9.0-desktop target) and the nuspecs in the NuGet package folders it
names. Run by .github/workflows/release.yml after a restore; the result goes into every package's licenses/.
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

assets_path, out_path = sys.argv[1], sys.argv[2]
assets = json.load(open(assets_path, encoding="utf-8"))
targets = assets["targets"]
key = next((k for k in targets if "/" not in k and "desktop" in k), None) or next(
    k for k in targets if "/" not in k and "windows" not in k
)
folders = list(assets.get("packageFolders", {}))


def nuspec(name, version):
    for folder in folders:
        path = os.path.join(folder, name.lower(), version.lower(), name.lower() + ".nuspec")
        if os.path.exists(path):
            return path
    return None


def text(meta, tag):
    for el in meta:
        if el.tag.split("}")[-1] == tag:
            return (el.text or "").strip(), el.attrib
    return "", {}


rows = []
for entry, info in targets[key].items():
    if info.get("type") != "package":
        continue
    name, version = entry.split("/", 1)
    licence, url, copyright_ = "", "", ""
    path = nuspec(name, version)
    if path:
        root = ET.parse(path).getroot()
        meta = next(el for el in root if el.tag.split("}")[-1] == "metadata")
        lic, attrs = text(meta, "license")
        licence = lic if attrs.get("type") == "expression" else (f"file {lic} in the package" if lic else "")
        if not licence:
            licence = text(meta, "licenseUrl")[0]
        url = text(meta, "projectUrl")[0]
        copyright_ = text(meta, "copyright")[0]
    rows.append((name, version, licence or "(see the package)", copyright_, url))

rows.sort(key=lambda r: r[0].lower())
with open(out_path, "w", encoding="utf-8") as out:
    out.write("Al Ummah Now Tools: NuGet packages in this build (%s)\n" % key)
    out.write("name version | licence | copyright | project\n\n")
    for row in rows:
        out.write("%s %s | %s | %s | %s\n" % row)
print(f"{len(rows)} packages from {key}")
