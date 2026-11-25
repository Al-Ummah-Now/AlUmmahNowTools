#!/usr/bin/env bash
# Closing the app quits it (Tools #78). Starts the installed app in its demo mode with an empty queue, lets it close
# itself the way the platform closes it (TOOLS_SMOKE_CLOSE: close = Window.Close(), quit = the request the close button,
# Cmd+Q and Dock > Quit make, terminate = the host's own exit), then fails unless
#   - the process ends within <seconds> of the close, and
#   - no child process it started (the FileScan service, ffmpeg) is left behind.
# Usage: smoke-close.sh <mode> <seconds> <command...>   (Linux: wrap the command in xvfb-run)
set -uo pipefail
mode=$1 seconds=$2; shift 2
work=$(mktemp -d)
ready="$work/ready"
unset TOOLS_SCREENSHOT # a screenshot run would end the app by itself before the close
export TOOLS_DEMO=1 TOOLS_DEMO_LANG=en TOOLS_DEMO_IMPORT=empty TOOLS_SMOKE_CLOSE="$mode" TOOLS_SMOKE_CLOSE_DELAY_MS=8000 TOOLS_SMOKE_READY="$ready"
log="$work/app.log"
"$@" > "$log" 2>&1 &
launcher=$!

# Every process below a pid, from one ps listing
descendants() {
  ps -A -o pid=,ppid= | awk -v root="$1" '{ kids[$2] = kids[$2] " " $1 } END { n = split(root, queue, " "); i = 1; while (i <= n) { m = split(kids[queue[i]], k, " "); for (j = 1; j <= m; j++) { print k[j]; queue[++n] = k[j] } i++ } }'
}

# Whatever happens, nothing of this run outlives it: the launcher and everything below it (under xvfb-run, the app too)
cleanup() {
  local p
  for p in $(descendants "$launcher") "$launcher"; do kill -9 "$p" 2>/dev/null; done
  rm -rf "$work"
}
trap cleanup EXIT

for _ in $(seq 120); do
  [ -s "$ready" ] && break
  kill -0 "$launcher" 2>/dev/null || break
  sleep 1
done
if [ ! -s "$ready" ]; then
  tail -n 40 "$log"
  echo "::error::smoke-close ($mode): the app did not start"
  kill "$launcher" 2>/dev/null; exit 1
fi
sleep 3
# What the app started, not the display server or the launcher's own helpers
app_children() {
  local p name
  for p in $(descendants "$launcher"); do
    name=$(basename "$(ps -o comm= -p "$p" 2>/dev/null)" 2>/dev/null)
    if [ -n "$name" ] && [ "$name" != Xvfb ] && [ "$name" != xvfb-run ] && [ "$name" != xauth ] && [ "$name" != sleep ]; then echo "$p"; fi
  done
}
children=$(app_children)
echo "smoke-close ($mode): app running, children: $(echo $children | tr '\n' ' ')"

closed_at=
for _ in $(seq $(( (8000 / 1000 + seconds + 10) * 2 ))); do
  [ -z "$closed_at" ] && [ -e "$ready.closing" ] && closed_at=$SECONDS
  kill -0 "$launcher" 2>/dev/null || break
  perl -e 'select(undef,undef,undef,0.5)'
done
if kill -0 "$launcher" 2>/dev/null; then
  tail -n 60 "$log"
  echo "::error::smoke-close ($mode): the app was still running after the close"
  kill "$launcher" 2>/dev/null; sleep 2; kill -9 "$launcher" 2>/dev/null
  exit 1
fi
wait "$launcher"; code=$?
[ -n "$closed_at" ] || { tail -n 60 "$log"; echo "::error::smoke-close ($mode): the app ended without being closed (exit $code)"; exit 1; }
took=$(( SECONDS - closed_at ))
if [ "$took" -gt "$seconds" ]; then echo "::error::smoke-close ($mode): the app took ${took}s to end after the close (limit ${seconds}s)"; exit 1; fi
sleep 1
# The app's last-resort exit (10 s after a clean stop) is a late end, not a quit: it means something still held the process
if grep -q "still running .* after the quit" "$log"; then echo "::error::smoke-close ($mode): only the last-resort exit ended the app"; tail -n 30 "$log"; exit 1; fi
left=
for p in $children; do kill -0 "$p" 2>/dev/null && left="$left $p"; done
if [ -n "$left" ]; then
  echo "::error::smoke-close ($mode): child processes left running:$left"
  ps -o pid,ppid,command -p "$(echo $left | tr ' ' ',')"
  kill $left 2>/dev/null; exit 1
fi
tail -n 12 "$log"
echo "smoke-close ($mode) passed: the app ended ${took}s after the close (exit $code), no child left"
