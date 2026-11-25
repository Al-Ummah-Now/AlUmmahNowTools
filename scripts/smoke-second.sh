#!/usr/bin/env bash
# One copy per queue folder (Tools #89). Starts the app in its demo mode on a queue folder of its own
# (TOOLS_DEMO_INSTANCE), then fails unless
#   - a second copy on that folder ends by itself within <seconds>, with exit code 0, and the first one came forward;
#   - the first one is still running;
#   - once the first one is killed (no chance to clean up), another copy starts.
# Usage: smoke-second.sh <seconds> <command...>   (Linux: wrap the command in xvfb-run)
set -uo pipefail
seconds=$1; shift
work=$(mktemp -d)
unset TOOLS_SCREENSHOT TOOLS_DEMO_UPDATE
export TOOLS_DEMO=1 TOOLS_DEMO_LANG=en TOOLS_DEMO_IMPORT=empty TOOLS_DEMO_UPDATE=none TOOLS_DEMO_INSTANCE="$work/queue"
pids=

descendants() {
  ps -A -o pid=,ppid= | awk -v root="$1" '{ kids[$2] = kids[$2] " " $1 } END { n = split(root, queue, " "); i = 1; while (i <= n) { m = split(kids[queue[i]], k, " "); for (j = 1; j <= m; j++) { print k[j]; queue[++n] = k[j] } i++ } }'
}
end_all() { local p; for p in $(descendants "$1") "$1"; do kill -9 "$p" 2>/dev/null; done; }
cleanup() { local p; for p in $pids; do end_all "$p"; done; rm -rf "$work"; }
trap cleanup EXIT
fail() { echo "::error::smoke-second: $1"; tail -n 40 "$work"/*.log; exit 1; }

# The app marks <name>.ready when its window is up (the launch check's own switch) and would close after 5 minutes
start() {
  TOOLS_SMOKE_CLOSE=quit TOOLS_SMOKE_CLOSE_DELAY_MS=300000 TOOLS_SMOKE_READY="$work/$1.ready" "${@:2}" > "$work/$1.log" 2>&1 &
  pids="$pids $!"
  local pid=$! _
  for _ in $(seq 120); do
    [ -s "$work/$1.ready" ] && return 0
    kill -0 "$pid" 2>/dev/null || break
    sleep 1
  done
  fail "$1 did not start"
}

start first "$@"
first=${pids##* }
sleep 2
"$@" > "$work/second.log" 2>&1 &
second=$!; pids="$pids $second"
for _ in $(seq $((seconds * 2))); do kill -0 "$second" 2>/dev/null || break; perl -e 'select(undef,undef,undef,0.5)'; done
kill -0 "$second" 2>/dev/null && fail "the second copy was still running after ${seconds}s"
wait "$second"; code=$?
[ "$code" = 0 ] || fail "the second copy ended with $code"
grep -q "already open; its window is in front" "$work/second.log" || fail "the second copy did not hand over"
grep -q "Brought forward for a second copy" "$work/first.log" || fail "the first copy did not come forward"
kill -0 "$first" 2>/dev/null || fail "the first copy ended"
end_all "$first"; sleep 2
start third "$@"
echo "smoke-second passed: the second copy handed over and ended, and after a kill a new copy starts"
