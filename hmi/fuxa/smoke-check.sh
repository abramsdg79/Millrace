#!/bin/sh
# Smoke check for the FUXA stack (plan 8). Run from hmi/fuxa with the stack up
# (docker compose up -d --build). Exits 0 when FUXA has loaded the project,
# reads CV001's speed over Modbus, and the start-sequence script runs the
# line up to speed; prints what failed and exits 1 otherwise.
set -eu
FUXA=${FUXA:-http://localhost:1881}

# The value of one FUXA tag (Bools read as 0 or 1), or nothing if FUXA has none.
value() {
  curl -fsS "$FUXA/api/getTagValue?ids=%5B%22$1%22%5D" |
    sed -n 's/.*"value":\([-0-9.eE+]*\).*/\1/p'
}
speed() { value t_CV001.Speed; }

# Wait for a live reading. FUXA reports 0 for the tag until its Modbus client
# has connected. The sample's speed measurement is noisy at rest, so a
# non-zero value can only have come from the plant (live readings are often 0
# too).
i=0
until awk -v v="$(speed 2>/dev/null || true)" 'BEGIN { exit !(v != "" && v + 0 != 0) }'; do
  i=$((i + 1))
  if [ "$i" -ge 90 ]; then echo "FAIL: FUXA has no live value for t_CV001.Speed after 90 s." >&2; exit 1; fi
  sleep 1
done
echo "FUXA reads CV001.Speed = $(speed) m/s."

# Press Start line, and press again until the sequence is running. Loading the
# project restarts FUXA's runtime, and for a few seconds after that a script
# call is accepted but never runs. Pressing again is safe: Start only acts
# from idle, Reset only from faulted or complete.
press() {
  curl -fsS -o /dev/null -X POST -H 'Content-Type: application/json' \
    -d '{"params":{"script":{"id":"s_pulse","name":"pulse","parameters":[{"name":"tags","type":"value","value":"t_SEQ_START.Reset,t_SEQ_START.Start"}]},"toLogEvent":false}}' \
    "$FUXA/api/runscript"
}
started() { [ "$(value t_SEQ_START.Running)" = 1 ] || [ "$(value t_SEQ_START.Complete)" = 1 ]; }
presses=0
until started; do
  if [ "$presses" -ge 10 ]; then echo "FAIL: the start sequence did not start after $presses presses of Start line." >&2; exit 1; fi
  press
  presses=$((presses + 1))
  sleep 3
done
if [ "$presses" -eq 0 ]; then
  echo "The start sequence has already run; not pressing Start line."
else
  echo "Pressed Start line ($presses press(es) until the sequence ran)."
fi

i=0
until awk -v v="$(speed)" 'BEGIN { exit !(v >= 1.74) }'; do
  i=$((i + 1))
  if [ "$i" -ge 60 ]; then echo "FAIL: CV001.Speed is $(speed) m/s 60 s after the start; expected 1.74 or more." >&2; exit 1; fi
  sleep 1
done
echo "PASS: CV001.Speed = $(speed) m/s after the start sequence."
