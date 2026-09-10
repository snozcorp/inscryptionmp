#!/usr/bin/env bash
# Play a whole match between two real game clients, with nobody at either keyboard.
#
#   tools/match-test.sh                       Act 1, random draw
#   tools/match-test.sh --act 2 --draw order  Act 2, dealt off the top
#   tools/match-test.sh --no-build            use whatever is already deployed
#
# Both clients run the mod's autopilot (see src/Autopilot.cs), which hosts or joins, agrees
# on an act, presses start, takes its turns and then quits. The trace log each one writes is
# the test report; this script waits for both to finish and reports what they said.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

A_DIR="/c/Steam/steamapps/common/Inscryption"
B_DIR="/c/Games/InscryptionClient2"

ACT=1
DRAW=order
RULES=""
EXPECT=""
LEAVE=""
TURNS=4
HOST_TURNS=""
JOIN_TURNS=""
BUILD=1
TIMEOUT=420
# Cheap Act 1 creatures, in an order that is obvious to read back out of the log.
DECK_A="Squirrel,Ant,Rabbit,Bullfrog,Skink,Squirrel,Mole,Rabbit,Raven,Wolf"
DECK_B="Rabbit,Squirrel,Skink,Ant,Squirrel,Bullfrog,Rabbit,Mole,Wolf,Raven"

while [ $# -gt 0 ]; do
  case "$1" in
    --act)      ACT="$2"; shift 2 ;;
    --draw)     DRAW="$2"; shift 2 ;;
    --rules)    RULES="$2"; shift 2 ;;
    --expect)   EXPECT="$2"; shift 2 ;;
    --turns)    TURNS="$2"; shift 2 ;;
    --host-turns) HOST_TURNS="$2"; shift 2 ;;
    --join-turns) JOIN_TURNS="$2"; shift 2 ;;
    --timeout)  TIMEOUT="$2"; shift 2 ;;
    --deck)     DECK_A="$2"; DECK_B="$2"; shift 2 ;;
    --deck-a)   DECK_A="$2"; shift 2 ;;
    --deck-b)   DECK_B="$2"; shift 2 ;;
    --leave)    LEAVE="$2"; shift 2 ;;
    --no-build) BUILD=0; shift ;;
    *) echo "unknown option: $1"; exit 2 ;;
  esac
done
[ -n "$DECK_B" ] || DECK_B="$DECK_A"
# A full rules payload replaces the draw shortcut rather than stacking on top of it.
if [ -n "$RULES" ]; then DRAW=""; [ -n "$EXPECT" ] || EXPECT="$RULES"; fi
[ -n "$HOST_TURNS" ] || HOST_TURNS="$TURNS"
[ -n "$JOIN_TURNS" ] || JOIN_TURNS="$TURNS"

A_LOG="$A_DIR/BepInEx/mp-trace.log"
B_LOG="$B_DIR/BepInEx/mp-trace.log"

kill_clients() {
  # Never leave a headless game running: the next run would fight it for the port.
  powershell -NoProfile -Command \
    "Get-Process Inscryption -ErrorAction SilentlyContinue | Stop-Process -Force" >/dev/null 2>&1 || true
  sleep 2
}

echo "==> act $ACT, host $HOST_TURNS turns / join $JOIN_TURNS turns"
if [ -n "$RULES" ]; then echo "==> rules: $RULES"; else echo "==> draw: $DRAW"; fi

if [ "$BUILD" = "1" ]; then
  echo "==> building and deploying"
  tools/deploy.sh >/dev/null || { echo "build failed"; exit 1; }
fi

for d in "$A_DIR" "$B_DIR"; do
  [ -d "$d" ] || { echo "missing install: $d"; exit 1; }
  # The campaign save is never written during a match, but a copy costs nothing.
  [ -f "$d/SaveFile.gwsave" ] && cp -n "$d/SaveFile.gwsave" "$d/SaveFile.gwsave.autotest-backup" 2>/dev/null
done

kill_clients
rm -f "$A_LOG" "$B_LOG"

launch() {   # launch <dir> <mode> <deck> <turns>
  local dir="$1" mode="$2" deck="$3" turns="$4"
  INSCRYPTIONMP_AUTOPILOT="$mode" \
  INSCRYPTIONMP_AUTOPILOT_ACT="$ACT" \
  INSCRYPTIONMP_AUTOPILOT_DRAW="$DRAW" \
  INSCRYPTIONMP_AUTOPILOT_RULES="$RULES" \
  INSCRYPTIONMP_AUTOPILOT_EXPECT="$EXPECT" \
  INSCRYPTIONMP_AUTOPILOT_LEAVE="$LEAVE" \
  INSCRYPTIONMP_AUTOPILOT_DECK="$deck" \
  INSCRYPTIONMP_AUTOPILOT_TURNS="$turns" \
  INSCRYPTIONMP_AUTOPILOT_QUIT=1 \
  "$dir/Inscryption.exe" -screen-fullscreen 0 -screen-width 960 -screen-height 540 \
    >/dev/null 2>&1 &
}

echo "==> launching host"
launch "$A_DIR" host "$DECK_A" "$HOST_TURNS"
sleep 6
echo "==> launching joiner"
launch "$B_DIR" join "$DECK_B" "$JOIN_TURNS"

done_for() { grep -qs '\[auto\] ==== RUN' "$1"; }

echo "==> waiting (up to ${TIMEOUT}s)"
elapsed=0
while [ "$elapsed" -lt "$TIMEOUT" ]; do
  if done_for "$A_LOG" && done_for "$B_LOG"; then break; fi
  sleep 5
  elapsed=$((elapsed + 5))
  if [ $((elapsed % 30)) = 0 ]; then
    echo "    ${elapsed}s  host=$(grep -cs '\[auto\]' "$A_LOG" || echo 0) lines," \
         "join=$(grep -cs '\[auto\]' "$B_LOG" || echo 0) lines"
  fi
done

sleep 3
kill_clients

# Put back whatever deck was there before the autopilot wrote its own. The mod keeps the
# copy; this is what actually restores it, so a test run leaves the installs as it found them.
for d in "$A_DIR" "$B_DIR"; do
  for saved in "$d"/BepInEx/config/inscryptionmp-deck-*.txt.pre-autopilot; do
    [ -f "$saved" ] || continue
    mv -f "$saved" "${saved%.pre-autopilot}"
    echo "==> restored $(basename "${saved%.pre-autopilot}") in $d"
  done
done

verdict=0
for pair in "HOST:$A_LOG" "JOIN:$B_LOG"; do
  who="${pair%%:*}"; log="${pair#*:}"
  echo
  echo "================ $who ================"
  if [ ! -f "$log" ]; then echo "no trace log - the client never started"; verdict=1; continue; fi

  grep -E '\[auto\]|\[rules\]|\[draw\]|\[side\]|\[deal\]|\[chat\]|\[match\]|ERROR' "$log" | sed 's/^[0-9:.]* //' || true

  if ! grep -q '\[auto\] ==== RUN OK' "$log"; then verdict=1; fi
  if grep -q '\[auto\] FAIL' "$log"; then verdict=1; fi
done

echo
if [ "$verdict" = "0" ]; then echo "==> MATCH TEST PASSED"; else echo "==> MATCH TEST FAILED"; fi
exit "$verdict"
