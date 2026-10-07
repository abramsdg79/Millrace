#!/bin/sh
# Smoke check for the FUXA stack (plan 8). Run from hmi/fuxa with the stack up
# (docker compose up -d --build). Exits 0 when FUXA has loaded the project,
# reads CV001's speed over Modbus, and the start-sequence script runs the
# line up to speed; prints what failed and exits 1 otherwise.
set -eu
FUXA=${FUXA:-http://localhost:1881}

speed() {
  curl -fsS "$FUXA/api/getTagValue?ids=%5B%22t_CV001.Speed%22%5D" |
    sed -n 's/.*"value":\([-0-9.eE+]*\).*/\1/p'
}

i=0
until [ -n "$(speed 2>/dev/null || true)" ]; do
  i=$((i + 1))
  if [ "$i" -ge 90 ]; then echo "FAIL: FUXA has no value for t_CV001.Speed after 90 s." >&2; exit 1; fi
  sleep 1
done
echo "FUXA reads CV001.Speed = $(speed) m/s."

curl -fsS -o /dev/null -X POST -H 'Content-Type: application/json' \
  -d '{"params":{"script":{"id":"s_pulse","name":"pulse","parameters":[{"name":"tags","type":"value","value":"t_SEQ_START.Reset,t_SEQ_START.Start"}]},"toLogEvent":false}}' \
  "$FUXA/api/runscript"
echo "Pressed Start line."

i=0
until awk -v v="$(speed)" 'BEGIN { exit !(v >= 1.74) }'; do
  i=$((i + 1))
  if [ "$i" -ge 60 ]; then echo "FAIL: CV001.Speed is $(speed) m/s 60 s after the start; expected 1.74 or more." >&2; exit 1; fi
  sleep 1
done
echo "PASS: CV001.Speed = $(speed) m/s after the start sequence."
