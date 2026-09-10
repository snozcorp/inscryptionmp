#!/usr/bin/env bash
# Everything that can be checked without a person watching.
#
#   tools/test-all.sh            build, unit tests, then a match on each act
#   tools/test-all.sh --quick    build and unit tests only
#
# The match runs drive two real game clients through the mod's autopilot, which takes a
# couple of minutes each. Their verdicts come out of the trace logs; see match-test.sh.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

QUICK=0
[ "${1:-}" = "--quick" ] && QUICK=1

failed=0
declare -a RESULTS

record() {   # record <name> <status>
  RESULTS+=("$2  $1")
  [ "$2" = "PASS" ] || failed=1
}

echo "=============================== wire format ==============================="
if (cd tools/tests && dotnet run --nologo -v q); then record "unit tests" PASS; else record "unit tests" FAIL; fi

echo
echo "=============================== build ==============================="
if tools/deploy.sh >/dev/null; then
  echo "built and deployed"
  record "build and deploy" PASS
else
  record "build and deploy" FAIL
  echo "build failed - stopping"
  printf '%s\n' "${RESULTS[@]}"
  exit 1
fi

if [ "$QUICK" = "1" ]; then
  echo
  printf '%s\n' "${RESULTS[@]}"
  exit "$failed"
fi

run() {   # run <name> <match-test args...>
  local name="$1"; shift
  echo
  echo "=============================== $name ==============================="
  if tools/match-test.sh --no-build "$@" > ".tmp/test-$RANDOM.log" 2>&1; then
    record "$name" PASS
    echo "PASS"
  else
    record "$name" FAIL
    echo "FAIL - full log follows"
    tail -60 "$(ls -t .tmp/test-*.log | head -1)"
  fi
}

mkdir -p .tmp
rm -f .tmp/test-*.log

run "act 1, in order"  --act 1 --draw order  --turns 4
run "act 1, random"    --act 1 --draw random --turns 4
run "act 2, in order"  --act 2 --draw order  --turns 4
run "act 3, in order"  --act 3 --draw order  --turns 5 \
    --deck-a "BatteryBot,Shieldbot,LeapBot,Steambot,Thickbot,SentryBot,BoltHound,GiftBot,RoboMice,Insectodrone" \
    --deck-b "Shieldbot,BatteryBot,Steambot,LeapBot,SentryBot,Thickbot,GiftBot,BoltHound,Insectodrone,RoboMice"
run "conceding by leaving" --act 1 --draw random --host-turns 2 --join-turns 10

echo
echo "=============================== summary ==============================="
printf '%s\n' "${RESULTS[@]}"
exit "$failed"
