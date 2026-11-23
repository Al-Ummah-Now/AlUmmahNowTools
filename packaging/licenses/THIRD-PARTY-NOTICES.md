# Al Ummah Now Tools: third-party notices

Al Ummah Now Tools is the staff app for imports into Al Ummah Now. This file and the others in this
`licenses` folder ship inside every package (Windows `.msi` and `.zip`, macOS `.app`, Linux `.deb` and
`.tar.gz`) and are attached to each GitHub release as `AlUmmahNow-Tools-<version>-licenses.zip`.

| Component | Where it is | Licence | Notice |
|---|---|---|---|
| aun-core (`AlUmmahNow.Core`) | `aun_core.dll` / `libaun_core.so` / `libaun_core.dylib` | Al Ummah Now | Its Rust crates: `aun-core-rust-crates.txt` |
| whisper.cpp and ggml (speech to text) | compiled into aun-core (crate `whisper-rs-sys`, which carries the whisper.cpp source) | MIT | `whisper.cpp-MIT.txt` |
| Whisper models (`ggml-*.bin`) | **not in the package**: downloaded once on first use from `huggingface.co/ggerganov/whisper.cpp`, checked against aun-core's SHA-256 catalog | MIT (OpenAI) | `openai-whisper-models-MIT.txt` |
| FFmpeg | **not in the package** (see below) | LGPL-2.1+ / GPL, as built by whoever built it | — |
| OpenH264 | **not in the package** (see below) | BSD-2-Clause, Cisco binary licence | — |
| .NET runtime, Uno Platform, SkiaSharp and the other NuGet packages | the app folder | MIT and others | `NUGET-PACKAGES.txt` (generated for each release) |

## FFmpeg

This release does **not** include FFmpeg. Encoding on the computer (TA-12) and reading the sound for speech to
text (TA-13) use the `ffmpeg` and `ffprobe` already installed on the computer (the FFmpeg settings page, or the
system `PATH`). Without them the app lets the server encode, and transcription is unavailable.

When the packages start carrying FFmpeg (wiki TOOLS-ADMIN TA-19), these rules apply and this file changes with it:

- an **LGPL** build only: configured without `--enable-gpl` and `--enable-nonfree`, so no libx264, libx265 or
  other GPL parts; shared libraries (or a separate `ffmpeg` executable) that the user can replace;
- its `configure` line, the exact source tarball (or a written offer for it) and the LGPL-2.1 text go into this
  folder, and this notice names FFmpeg and links <https://ffmpeg.org>;
- H.264 comes from the system's hardware encoders (VideoToolbox, Media Foundation, NVENC, QSV, AMF, VAAPI).

## OpenH264

This release does **not** include OpenH264. When it is used as the software H.264 fallback (TA-19), Cisco's
patent licence applies only to **Cisco's own binary**, so:

- the app downloads Cisco's prebuilt `libopenh264` from `ciscobinary.openh264.org` on first use, after telling
  the user, and checks it; it is never put in our packages or built by us;
- the user can turn it off, and the app works without it (the server encodes);
- the app shows "OpenH264 Video Codec provided by Cisco Systems, Inc." with the BSD-2-Clause licence text.

## Questions

Source for this app: <https://github.com/Al-Ummah-Now/AlUmmahNowTools>. Licence questions: open an issue there.
