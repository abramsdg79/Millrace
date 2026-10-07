# Wheel line

The second reference sample: a forging cell that heats steel billets in a
furnace, checks each one's temperature at the furnace exit, carries the good
ones on a belt to a press and forges each into a wheel. Six scenarios break it
in six ways. As in `samples/mine-conveyors/`, there is no C# here: everything is
data the `dse` command line runs.

It is a different domain on purpose. The mine conveyors move bulk ore; here
each billet is an item with its own id, mass and temperature, the press changes
what it is (a billet goes in, a wheel comes out, lighter by its flash), and the
line's rate is set by cycle times rather than by belt speed. The same engine,
the same control blocks and the same scenario files run both; nothing in the
engine knows what a wheel is.

```
Billets ──▶ FCE ──▶ GATE ──▶ CV (10 m) ──▶ PRESS ──▶ Wheels
                     │
                     └─ RejectOut ──▶ Bay (4 billets)

PYRO looks at GATE; HMD looks at CV, 4 m from its tail.
```

| file | what it is |
|---|---|
| `plant.json` | the line and its five controllers |
| `scenarios/*.json` | the six scenarios below |
| `expected/*.log` | the golden event log of each scenario |

## The line

| id | type | what it does |
|---|---|---|
| `Billets` | `item-source` | the saw: one 400 kg billet a minute at 25 °C; its charging table holds two, and the saw waits when it is full |
| `FCE` | `item-process-unit` | the furnace, one billet at a time: heats it toward the zone temperature (time constant 24 s) and discharges it when it reaches 1100 °C. `heatWhileHeld`: a billet that cannot leave keeps soaking |
| `GATE` | `reject-gate` | the measuring station at the furnace exit: holds each billet for 1 s, then passes it to the belt, or kicks it to `Bay` while `Reject` is true |
| `Bay` | `item-sink`, capacity 4 | the reject cradle |
| `CV` | `discrete-belt` | 10 m at 0.5 m/s, blanks at least 2 m apart, so it holds six; a blank cools in transit (time constant 1200 s) |
| `PRESS` | `item-process-unit` | forges one blank in 40 s into a `Wheel`, keeping 92 % of its mass |
| `Wheels` | `item-sink` | finished wheels |
| `PYRO` | `pyrometer` | aimed at `GATE`, no lag: reads the billet on the station, 20 °C when it is empty |
| `HMD` | `pyrometer` | a hot-metal detector aimed at the belt 4 m from its tail, 5 s lag: reads hot only when a blank stands in front of it |

Two inputs sit at their defaults until something writes them (the zone at
20 °C, the belt stopped): the furnace's zone temperature and the belt's speed.
The plant binds them as tags, `FCE.ZONE_SP` and `CV.SPEED_SP`, through its
`tags` envelope (with
`CV.ItemCount`, the belt's count, bound read-only beside them). There is no
burner model and no drive; the setpoints are the plant.

**Starting the line.** There is no start sequencer. Every scenario opens with
the operator bringing the line up: at 0 s it writes the zone setpoint, 1250 °C,
and the belt speed, 0.5 m/s. On its first scan the coil drives `GATE.Reject`
false, as a PLC drives every output at power-up. Every golden starts with the
same three lines:

```
06:00:00.000  FCE.ZONE_SP  WRITE  Set to 1250.
06:00:00.000  CV.SPEED_SP  WRITE  Set to 0.5.
06:00:00.100  GATE.Reject  WRITE  Set to false by COIL_REJECT.
```

**Ending the shift.** A running line logs every billet's passage, so a scenario
whose line is still producing ends it by starving the saw (`Billets` `starve`),
lets the line run empty, and runs on quietly for at least 50 s.

## The controllers

All five scan every 100 ms.

| block | type | watches | does |
|---|---|---|---|
| `ALM_PYRO` | `alarm` | `PYRO.Value` | `Hi` 1130 °C (warning), `HiHi` 1150 °C (over-soak), deadband 10 °C, no on-delay |
| `COIL_REJECT` | `coil` | `ALM_PYRO.HiHi.Active` | drives `GATE.Reject` to follow it; claims `GATE.Reject` |
| `ALM_QUEUE` | `alarm` | `HMD.Value` | `Hi` 200 °C, deadband 50 °C, 60 s on-delay: the queue has stood at the detector for a minute |
| `ALM_ZONE` | `alarm` | `FCE.ZONE_SP` | `Lo` 1200 °C and `Hi` 1300 °C, deadband 10 °C, 5 s on-delay |
| `INT_BAY` | `interlock` | `Bay.Full`, normal false | trip: `Billets.Enabled` false; reset: `Billets.Enabled` true; claims `Billets.Enabled` |

**The reject is the PLC's decision.** The pyrometer measures, the alarm
decides, the coil drives the kicker. The coil claims `GATE.Reject`, so nothing
else — no HMI, no scenario — can write it: `dse tags` lists it `ReadOnly …
claimed by COIL_REJECT`. `INT_BAY` stops the saw when the reject cradle is full
and claims `Billets.Enabled` the same way.

**Why the queue alarm watches a hot-metal detector.** An `alarm` watches a
Double tag, and a belt's `ItemCount` is an Int64 — `dse validate` refuses an
alarm on it (`DSE114`). Lines detect a queue the way this one does: a
hot-metal detector at the queue-full position. Blanks stand 2 m apart from the
head, so a blank standing at 4 m means at least four are queued. A blank
passing at belt speed is in its view for under half a second, and the 5 s lag
keeps the reading low (103 °C at most in steady running); a blank standing
there brings it up to its own temperature within a few seconds, and the 60 s
on-delay waits out the queue a normal press cycle leaves. The lag also holds
the reading up through the 4 s gap while the queue shifts up a place, standing
in for the off-delay a PLC would put on a real detector: with no lag,
`slow-press` clears and re-raises `ALM_QUEUE` at every shift while the belt is
still backed up.

**Why the zone alarm has an on-delay.** At power-up a block's first scan reads
the primed tag image, in which `FCE.ZONE_SP` is still 20 °C: the operator's
write at 0 s is not yet in the image that scan reads. Without an on-delay
`ALM_ZONE` raises `Lo` at 0 s and clears it 0.1 s later; the 5 s on-delay
keeps the start quiet.

## Sizing

Realistic in kind, compressed in time: a real billet takes hours to heat, not
a minute. This is a demonstration line, not a sized design.

| | |
|---|---|
| billet heated from 25 °C to 1100 °C in a 1250 °C zone | 50.2 s |
| billet at the gate, steady running | 1100.02 °C — 30 °C under `Hi` |
| billet held in the furnace | soaks toward 1250 °C: past `HiHi` 10 s after it reaches its target |
| blanks on the belt, steady running | one at most |
| `HMD`, steady running | 103 °C at most — under `ALM_QUEUE`'s 200 °C |
| wheels, steady running | one a minute, the saw's rate; the 40 s press has a third in hand, which is what lets it clear a queue |

A zone at `ALM_ZONE`'s `Lo` limit, 1200 °C, heats a billet in 59.0 s — just
inside the minute the saw allows. Below it the furnace falls behind.

## The reject decision: sizing the dwell

The gate must hold a billet long enough for the pyrometer to read it, the alarm
to raise, the coil to write `Reject` and the write to land — before the billet
leaves. From the tick the billet lands on the station, *N*, the pyrometer reads
it at *N* + 1, the alarm raises on its first scan after that, the coil writes on
its first scan after the alarm publishes, and the write lands one tick later;
the billet may leave at *N* + ⌈dwell / dt⌉ + 1. With the alarm scanning every
*a* ticks and the coil every *c*, the decision is certain when

⌈dwell / dt⌉ ≥ a + c + 1 + a·⌈onDelay / (a·dt)⌉, plus the pyrometer's lag.

On this line dt = 0.1 s, both blocks scan every 100 ms (a = c = 1), `HiHi` has
no on-delay and `PYRO` no lag:

⌈1.0 s / 0.1 s⌉ = 10 ≥ 1 + 1 + 1 + 0 = 3

— seven ticks to spare. The slow-press golden shows it: the over-soaked billet
lands at 06:18:36.000 (*N*), `HiHi` raises at *N* + 2, the coil's write lands at
*N* + 4 and the billet leaves, rejected, at *N* + 11.

## Running a scenario

From the repository root:

```bash
dotnet run --project src/Dse.Cli -- validate samples/wheel-line/plant.json
dotnet run --project src/Dse.Cli -- tags samples/wheel-line/plant.json
dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/slow-press.json
```

Add `--expect samples/wheel-line/expected/<name>.log` to check a run against
its golden: exit 0 if nothing changed, 4 if the behaviour did.

## 1. Normal run

The line starts, makes ten wheels, one a minute, and runs empty when the saw
stops at 600 s. Each billet leaves the furnace 50.2 s after it enters and is
forged 40 s after it reaches the press. No alarm raises, nothing is rejected,
and `Reject` is never written again after the first scan.

```text expected/normal-run.log
06:01:50.300  FCE  DISCHARGING  Hold satisfied after 50.20 s; discharging 1 items.
06:02:51.700  PRESS  DISCHARGING  Hold satisfied after 40.00 s; discharging 1 items.
06:10:00.000  Billets  FAULT  starve injected.
06:11:51.800  PRESS  IDLE  Batch discharged; ready for the next.
```

`dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/normal-run.json --expect samples/wheel-line/expected/normal-run.log`

## 2. Slow press

The causal chain of the main spec, end to end. At 300 s the press slows to a
tenth of its rate (`slow-cycle`, fraction 0.9): a wheel every 400 s while a
billet still arrives every 60 s. (The press's hold counter runs at the slowed
rate, so a slowed cycle's message gives both times — 40 s of hold in 400 s of
clock — and the cycle the fault's clear at 1140 s falls in logs 40.08 s of hold
in 65.10 s.)
The blanks queue on the belt; when the queue
has stood at the detector for a minute, `ALM_QUEUE` raises. The belt fills, a
good billet waits on the gate for room, and the furnace cannot discharge the
billet behind it: it reached its target at 06:13:50.300 and is held, soaking,
for 285.7 s. When the press next takes a blank the belt moves up, the good
billet goes on, and the over-soaked one reaches the gate at 1250 °C — `Hi` and
`HiHi` raise together, the coil writes `Reject`, and the billet goes to the bay.
The gate empties, the alarm clears and the coil drops `Reject` before the next
billet arrives. The press is put right at 1140 s; the queue drains, `ALM_QUEUE`
clears, and the line runs normally until the saw stops at 2100 s.

```text expected/slow-press.log
06:05:00.000  PRESS  FAULT  slow-cycle injected: fraction=0.9.
06:10:00.500  ALM_QUEUE  ALARM_RAISED  Hi: 1044.1 above 200.
06:11:51.700  PRESS  DISCHARGING  Hold satisfied after 40.00 s of hold (400.00 s elapsed); discharging 1 items.
06:13:50.300  FCE  DISCHARGING  Hold satisfied after 50.20 s; discharging 1 items.
06:18:36.000  FCE  IDLE  Batch discharged; ready for the next.
06:18:36.200  ALM_PYRO  ALARM_RAISED  Hi: 1250.0 above 1130.
06:18:36.200  ALM_PYRO  ALARM_RAISED  HiHi: 1250.0 above 1150.
06:18:36.400  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:18:37.100  GATE  REJECTED  Item 13 rejected.
06:18:37.300  ALM_PYRO  ALARM_CLEARED  HiHi: 20.0 back within limits.
06:18:37.500  GATE.Reject  WRITE  Set to false by COIL_REJECT.
06:19:00.000  PRESS  FAULT_CLEARED  slow-cycle cleared.
06:19:37.200  PRESS  DISCHARGING  Hold satisfied after 40.08 s of hold (65.10 s elapsed); discharging 1 items.
06:27:50.400  ALM_QUEUE  ALARM_CLEARED  Hi: 148.2 back within limits.
```

Only billet 13 is rejected. The rejection itself makes room: the next billet
reaches the gate at its target and waits there, good, for the belt.

`dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/slow-press.json --expect samples/wheel-line/expected/slow-press.log`

## 3. Stuck kicker

The slow press again, but the kicker's actuator sticks at 600 s (`GATE`
`stuck`). The PLC does everything right — `HiHi` raises on billet 13 and the
coil writes `Reject` true — and nothing is rejected: the kicker does not move,
and the over-soaked billet waits on the gate for the belt instead of going to
the bay. Held behind it, billet 14 soaks past `HiHi` too (1169.5 °C). Both
become wheels.

```text expected/stuck-kicker.log
06:10:00.000  GATE  FAULT  stuck injected.
06:18:36.200  ALM_PYRO  ALARM_RAISED  HiHi: 1250.0 above 1150.
06:18:36.400  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:19:00.000  PRESS  FAULT_CLEARED  slow-cycle cleared.
06:20:21.700  ALM_PYRO  ALARM_CLEARED  HiHi: 20.0 back within limits.
06:20:21.900  GATE.Reject  WRITE  Set to false by COIL_REJECT.
```

The log has no `REJECTED` line; a command is not a confirmation. A real reject
station proves the kick — a sensor on the reject chute, or the gate's own
`Rejected` count — and alarms when a commanded reject does not arrive. This
sample deliberately has no such check, so the failure shows; the tests follow
billets 13 and 14 by id into the `Wheels` sink.

`dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/stuck-kicker.json --expect samples/wheel-line/expected/stuck-kicker.log`

## 4. Press jam

The press's discharge jams at 300 s: the next wheel it forges (06:05:51.700)
cannot leave. The belt fills, a good billet waits on the gate, and the billet
behind it is held in the furnace from 06:12:50.300 — and the pyrometer sees none
of it: it looks at the gate, where the billet is good. While the jam lasts
nothing is rejected and `ALM_PYRO` stays quiet. The jam is cleared at 1200 s;
the belt moves up, the good billet goes on, and the billet that has been soaking
for seven minutes reaches the gate and is rejected five seconds after the clear.

```text expected/press-jam.log
06:05:00.000  PRESS  FAULT  discharge-jam injected.
06:10:00.500  ALM_QUEUE  ALARM_RAISED  Hi: 1044.1 above 200.
06:12:50.300  FCE  DISCHARGING  Hold satisfied after 50.20 s; discharging 1 items.
06:20:00.000  PRESS  FAULT_CLEARED  discharge-jam cleared.
06:20:04.200  ALM_PYRO  ALARM_RAISED  HiHi: 1250.0 above 1150.
06:20:04.400  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:20:05.100  GATE  REJECTED  Item 12 rejected.
06:20:05.500  GATE.Reject  WRITE  Set to false by COIL_REJECT.
06:26:52.700  ALM_QUEUE  ALARM_CLEARED  Hi: 147.8 back within limits.
```

A jam does not fill the reject bay. With the belt full and a good billet on the
gate, no over-soaked billet can reach the pyrometer, so rejects cannot
accumulate during a jam; the full bay and `INT_BAY` are shown by scenario 6,
where they can.

`dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/press-jam.json --expect samples/wheel-line/expected/press-jam.log`

## 5. Zone low

At 330 s the zone setpoint is written to 1050 °C, below the 1100 °C discharge
target. `ALM_ZONE` raises `Lo` after its 5 s on-delay. The billet in the
furnace climbs toward 1050 °C and never reaches its target, so the furnace
never discharges again; the press finishes the blank it holds and then
starves. The saw fills its table and waits. Nothing is rejected — nothing
reaches the gate.

```text expected/zone-low.log
06:05:30.000  FCE.ZONE_SP  WRITE  Set to 1050.
06:05:35.200  ALM_ZONE  ALARM_RAISED  Lo: 1050.0 below 1200.
06:05:51.700  PRESS  DISCHARGING  Hold satisfied after 40.00 s; discharging 1 items.
06:05:51.800  PRESS  IDLE  Batch discharged; ready for the next.
```

`dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/zone-low.json --expect samples/wheel-line/expected/zone-low.log`

## 6. Pyrometer fails high

At 300 s the pyrometer fails high: it reads the top of its range, 1400 °C,
whatever is on the gate. `HiHi` raises and the coil holds `Reject` true, so
every billet is kicked to the bay, good ones included. That is the fail-safe
direction for a reject: failing upscale, a failed instrument costs good
billets, never passes a bad one. (A downscale failure — failing low, or reading
the empty station's background — would pass every billet, and is not shown.)
After four the bay is full, `INT_BAY` trips and stops the saw, and the line runs
dry. The operator's reset at 600 s is refused — the interlock resets only when
every condition is normal, and the bay is still full.

```text expected/pyro-fail-high.log
06:05:00.000  PYRO  FAULT  fail-high injected.
06:05:00.100  ALM_PYRO  ALARM_RAISED  HiHi: 1400.0 above 1150.
06:05:00.300  GATE.Reject  WRITE  Set to true by COIL_REJECT.
06:05:51.500  GATE  REJECTED  Item 5 rejected.
06:08:51.500  GATE  REJECTED  Item 8 rejected.
06:08:51.600  Bay  FULL  Capacity reached; accepting nothing more.
06:08:51.700  INT_BAY  INTERLOCK_TRIP  Bay.Full abnormal.
06:08:51.800  Billets.Enabled  WRITE  Set to false by INT_BAY.
06:10:00.000  INT_BAY.Reset  WRITE  Set to true.
06:10:00.100  INT_BAY  RESET_REFUSED  Reset refused: Bay.Full is not normal.
06:10:01.000  INT_BAY.Reset  WRITE  Set to false.
```

The interlock refuses the reset on the scan after the write and says why:
`RESET_REFUSED` names the first of its conditions, in the order they are
declared, that is still not normal. Neither `INTERLOCK_RESET` nor a
`Billets.Enabled … Set to true` line follows. The false write a second later
releases the momentary reset button; the interlock resets only on a rising
edge, so the operator must press it again once the bay is emptied.

The scenario ends with the line held. A real reject cradle is emptied by a
crane or a forklift, after which the reset is accepted and writes
`Billets.Enabled` true; an `item-sink` cannot be emptied, so no scenario can
show that recovery.

`dotnet run --project src/Dse.Cli -- run samples/wheel-line/scenarios/pyro-fail-high.json --expect samples/wheel-line/expected/pyro-fail-high.log`

## Known limits

- **No burner or zone controller.** The zone temperature is a written setpoint,
  reached at once; a real zone heats and cools at a rate its burners set.
- **No descaler.** Scale on the billet's surface, and the descaler that blasts
  it off before the press, are not modelled.
- **One press.** A real cell may feed two presses or a press and a ring mill.
- **Lumped billet temperature.** Each billet has one temperature; a real one is
  hotter at its core than its skin as it heats, and the reverse as it cools.
- **The zone held as a written setpoint.** `ALM_ZONE` watches the setpoint, not
  a thermocouple, because there is no zone model to measure.
- **Cold blanks are not rejected.** Blanks queued on the belt behind a slow
  press cool — to about 620 °C in scenario 2, and to about 553 °C in scenario
  4, where the jam leaves them queued longer (measured 552.6 °C) — and are
  forged anyway; a real press would refuse them. A second pyrometer at the press
  would be the check.
- **An analog stand-in for a discrete detector.** `HMD` is an analog pyrometer
  standing in for a discrete hot-metal detector; `ALM_QUEUE`'s `Hi` is its
  switching threshold and the 5 s lag stands in for the detector's off-delay.
- **No proof of the kick** (scenario 3).
