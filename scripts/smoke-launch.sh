#!/usr/bin/env bash
# Starts the installed app in its demo mode (no network, no sign-in) and waits for its own screenshot
# (TOOLS_SCREENSHOT: ScreenshotRunner renders the window to a PNG and exits). Fails when no PNG appears in time.
# Usage: smoke-launch.sh <png-out> <seconds> <command...>   (Linux: wrap the command in xvfb-run)
set -uo pipefail
png=$1 seconds=$2; shift 2
mkdir -p "$(dirname "$png")"; rm -f "$png"
export TOOLS_DEMO=1 TOOLS_DEMO_LANG=en TOOLS_SCREENSHOT="$png"
"$@" > "${png%.png}.log" 2>&1 &
pid=$!
for _ in $(seq "$seconds"); do
  kill -0 "$pid" 2>/dev/null || break
  sleep 1
done
if kill -0 "$pid" 2>/dev/null; then echo "::warning::the app was still running after ${seconds}s; stopping it"; kill "$pid" 2>/dev/null; sleep 2; kill -9 "$pid" 2>/dev/null; fi
wait "$pid"; code=$?
tail -n 40 "${png%.png}.log"
if [ ! -s "$png" ]; then echo "::error::the app did not start: no screenshot at $png (exit $code)"; exit 1; fi
echo "smoke launch passed: $(wc -c < "$png") byte screenshot, exit $code"
