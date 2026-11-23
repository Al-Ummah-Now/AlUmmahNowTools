#!/usr/bin/env python3
"""Release notes for Al Ummah Now Tools.

Usage: release-notes.py <tag> <version> <windows-signed: true|false> <mac-signed: true|false> <generated-notes.md>
<generated-notes.md> is GitHub's generated list of merged PRs since the previous tag (the release job asks the
API for it). Prints Markdown: install table, signing warnings, which server release each page needs
(packaging/SERVER-REQUIREMENTS.md), then the PRs.
"""
import pathlib
import sys

tag, version, win_signed, mac_signed, generated = sys.argv[1:6]
win_signed, mac_signed = win_signed == "true", mac_signed == "true"
root = pathlib.Path(__file__).resolve().parent.parent
server = (root / "packaging" / "SERVER-REQUIREMENTS.md").read_text(encoding="utf-8").strip()
prs = pathlib.Path(generated).read_text(encoding="utf-8").strip() if pathlib.Path(generated).exists() else ""
w = "" if win_signed else "-unsigned"
m = "" if mac_signed else "-unsigned"

warnings = []
if not win_signed:
    warnings.append(
        "**Windows files are not code-signed.** SmartScreen shows \"Windows protected your PC\": choose *More info*, "
        "then *Run anyway*."
    )
if not mac_signed:
    warnings.append(
        "**The macOS app is not signed or notarized.** After copying it to Applications, run "
        "`xattr -dr com.apple.quarantine \"/Applications/Al Ummah Now Tools.app\"` once, or macOS says it is damaged."
    )
warning = ""
if warnings:
    warning = "> [!WARNING]\n" + "\n".join("> " + line + "\n>" for line in warnings) + (
        "\n> Check each download against `SHA256SUMS.txt`. Signed builds follow once the project's certificates are"
        " set (docs/RELEASE.md).\n"
    )

print(f"""## Al Ummah Now Tools {tag}

The staff app for bulk imports into Al Ummah Now: sign in with your Al Ummah Now staff account (admins only),
pick the environment, then import folders of videos with their caption, chapter, thumbnail and metadata files.
Arabic and English. This release has no automatic update check yet: download the next release from this page.

{warning}
| System | File | Install |
|---|---|---|
| Windows 10/11, x64 | `AlUmmahNow-Tools-Windows-{version}-x64{w}.msi` | Run it; Start menu → Al Ummah Now Tools |
| Windows, no install | `AlUmmahNow-Tools-Windows-{version}-x64-portable{w}.zip` | Unzip, run `AlUmmahNow.VisualImporterUno.exe` |
| macOS 13+, Apple silicon | `AlUmmahNow-Tools-macOS-{version}-arm64{m}.dmg` | Open, drag to Applications |
| macOS 13+, Intel | `AlUmmahNow-Tools-macOS-{version}-x64{m}.dmg` | Open, drag to Applications |
| Ubuntu 22.04+ / Debian, x86-64 | `alummahnow-tools_{version}_amd64.deb` | `sudo apt install ./alummahnow-tools_{version}_amd64.deb` |
| Any Linux, x86-64 | `AlUmmahNow-Tools-Linux-{version}-x64.tar.gz` | Unpack, run `AlUmmahNow.VisualImporterUno` |

Encoding on the computer and speech to text need `ffmpeg` and `ffprobe` installed (Windows: `winget install
Gyan.FFmpeg.Essentials`; macOS: `brew install ffmpeg`; Linux: `sudo apt install ffmpeg`). Without them the server
encodes and transcription is off. The speech model downloads once, on first use. Third-party licences:
`AlUmmahNow-Tools-{version}-licenses.zip` (also in each package's `licenses` folder).

{server}

{prs}
""")
