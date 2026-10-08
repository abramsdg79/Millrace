# Mine conveyors

A reference sample: three belt conveyors in series carrying ore from a feeder to
a stockpile, with the PLC logic a real line would have — a sequenced start, a
sequenced stop, cascade interlocks, permissives and current alarms — declared in
one plant file, and nine scenarios that break it in nine ways. There is no C#
here. Everything is data the `millrace` command line runs.

```
Feed ──▶ CV001 (60 m) ──▶ CH1 ──▶ CV002 (40 m) ──▶ CH2 ──▶ CV003 (30 m) ──▶ Stockpile
```

| file | what it is |
|---|---|
| `plant.json` | the line and its twelve controllers |
| `scenarios/*.json` | the nine scenarios below |
| `expected/*.log` | the golden event log of each scenario |

## The control philosophy

**Start downstream first.** `SEQ_START` resets the three safety relays, then
resets CV003's interlock, starts CV003 and waits until its speed sensor reads
1.74 m/s (90 % of its running speed), then does the same for CV002, then CV001,
then resets the feeder's interlock and enables the feeder. The feeder is declared
`"enabled": false`: like a PLC output it is off at power-up, and only
`SEQ_START` turns it on. A belt never starts
onto a belt that is not already carrying material away. Each proving step has a
15 s timeout; a timeout aborts the sequence, which drops every run command and
the feeder.

**Stop upstream first.** `SEQ_STOP` disables the feeder, waits 40 s for CV001
to run out, stops CV001 and waits for its zero-speed switch, lets CV002 run out
for 20 s, stops it, lets CV003 run out for 15 s, and stops it. The line stops
empty.

**Cascade interlocks.** Each belt's interlock stops it when the belt downstream
of it stops, and the feeder's interlock stops the feeder when CV001 stops. An
interlock reads two field instruments of the downstream belt, ORed: its
contactor's **auxiliary contact** (`CV002.Contactor`, the run feedback) and its
**zero-speed switch** (`CV002.Stopped`) — as a real PLC is wired, never the
true speed. The auxiliary contact opens with the contactor, so any stop that
drops the contactor — a sequenced stop, a pull-key, an e-stop, an overload —
cascades on the next scan, while the belt is still coasting. The zero-speed
switch catches a belt that reports stopped with its contactor still closed — a
stalled belt, or, in this sample, a failed switch, which is why one trips a
healthy line (scenario 6). A welded contactor, whose auxiliary contact stays
closed, cascades nothing (scenario 7). Each interlock also trips on its own
belt's overload relay (`CVn.Tripped`) and on its permissive, and it is latched:
`SEQ_START` resets it only once the belt downstream is proved.

**Run permits.** A tripped interlock does more than drop its device's command
once. It also writes the device's `Permit` false, and gives it back only on the
scan that accepts a reset. A starter closes only while its `Start`, its safety
relay and its `Permit` all allow it; the feeder makes ore only while `Enabled`
and `Permit` are both true. So while an interlock is tripped, no write of
`Start` or `Enabled`, from anywhere, starts its device. Nor can anything else
give the permit back: each interlock claims its device's permit
(`"claims": [ "CV001.Permit" ]`), so `millrace tags` lists `CVn.Permit` and
`Feed.Permit` as `ReadOnly … claimed by INT_…`, and a write from an HMI, a
scenario or another block is refused — as an HMI has no write access to a PLC
program's permit bit. The reset that gives the permit back also writes the
command false, as a seal-in circuit does when the interlock breaks it: a start
written during the trip is forgotten, and a device runs again only on a fresh
start (scenario 9). That is why `SEQ_START` resets each interlock one step
before it starts the device — its own `Start` must land
after the reset's `false` — and each reset step waits for the interlock's `Ok`,
with a 5 s timeout.

**Safety is hardwired.** The pull-keys and the e-stop of each belt are wired
through the belt's own safety relay to its starter, inside the conveyor. They
stop the motor with no PLC involved. The PLC sees the relay only through
`CVn.SafetyOk`, which `PERM_CVn` watches; the interlock then drops the run
command, so that resetting the relay can never restart the belt by itself.

**Alarms.** `ALM_CVn` watches each motor's current: `Hi` a little above the
loaded running current, `HiHi` a little above the motor's rated current, both
with a 3 s on-delay. The start inrush (about six times rated) is above both for
under 1.9 s, so neither raises on a normal start. There are no standing alarms:
an idle line raises nothing.

## Sizing

Realistic in kind, modest in size: this is a demonstration line, not a sized
design. The belts are horizontal, 1 m wide, and run at 1.95 m/s with no load
(1 480 rpm motors, 25:1 gearboxes, 0.63 m pulleys). The feeder delivers
288 t/h, about 40 % of what a belt can carry. The motors' thermal time
constant is the model's default 60 s, far shorter than a real motor's, so an
overload trips in a minute or two rather than in twenty.

| | CV001 | CV002 | CV003 |
|---|---|---|---|
| length | 60 m | 40 m | 30 m |
| motor | 4 kW, 8.2 A | 3 kW, 6.3 A | 2.2 kW, 4.7 A |
| current, belt empty | 4.2 A | 3.1 A | 2.3 A |
| current, belt at 288 t/h | 6.3 A | 4.5 A | 3.4 A |
| start inrush peak | 48.7 A | 37.4 A | 27.9 A |
| time to 90 % speed | 2.3 s | 2.3 s | 2.3 s |
| `Hi` / `HiHi` | 7.5 / 8.6 A | 5.7 / 6.6 A | 4.3 / 4.9 A |

Every scenario starts from a cold plant, writes `SEQ_START.Start` at 1 s, lets
the line fill (ore starts moving at 10.21 s), and injects its event at 80 s, when
CV003's scale is still rising (263 t/h); it reaches full rate at about 86 s.
Each runs in well under a second.

## Running a scenario

From the repository root:

```bash
dotnet run --project src/Millrace.Cli -- validate samples/mine-conveyors/plant.json
dotnet run --project src/Millrace.Cli -- tags samples/mine-conveyors/plant.json
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/pull-key.json
```

Add `--expect samples/mine-conveyors/expected/<name>.log` to check a run against
its golden: exit 0 if nothing changed, 4 if the behaviour did.

## 1. Normal start and stop

`SEQ_START` at 1 s; `SEQ_STOP` at 90 s. Each belt is proved at speed before the
next one starts, and on the way down each belt stops only after the one above
it has stopped and it has run out:

```text expected/normal-start-stop.log
06:00:02.200  SEQ_START  STEP_ENTERED  2: Reset CV003's interlock.
06:00:02.310  CV003.Start  WRITE  Set to false by INT_CV003.
06:00:02.310  CV003.Permit  WRITE  Set to true by INT_CV003.
06:00:02.410  CV003.Start  WRITE  Set to true by SEQ_START.
06:00:05.000  SEQ_START  STEP_ENTERED  5: Start CV002.
06:00:07.600  SEQ_START  STEP_ENTERED  7: Start CV001.
06:00:10.200  SEQ_START  STEP_ENTERED  9: Start the feed.
06:00:11.200  SEQ_START  SEQUENCE_COMPLETE  Finished after 9 steps.
06:01:30.210  Feed.Enabled  WRITE  Set to false by SEQ_STOP.
06:02:10.210  CV001.Start  WRITE  Set to false by SEQ_STOP.
06:02:38.410  CV002.Start  WRITE  Set to false by SEQ_STOP.
06:03:01.610  CV003.Start  WRITE  Set to false by SEQ_STOP.
06:03:09.600  SEQ_STOP  SEQUENCE_COMPLETE  Finished after 6 steps.
```

The cascade interlocks trip during the stop too — `INT_FEED` when CV001's
contactor opens, `INT_CV001` when CV002's does, `INT_CV002` when CV003's does,
each one scan later. That is harmless
(each writes a `Start` that is already false, and takes the belt's permit away
until the next `SEQ_START`) and expected: an interlock does not know a stop was
planned. Between `SEQUENCE_COMPLETE` and `SEQ_STOP` nothing
trips, and no alarm raises in the whole run.

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/normal-start-stop.json --expect samples/mine-conveyors/expected/normal-start-stop.log
```

## 2. Pull-key

An operator pulls CV002's first pull-wire at 80 s. The relay drops the contactor
at once. On the next scan CV001's interlock sees CV002's auxiliary contact open
and stops CV001; a scan after that the feeder's interlock sees CV001's open and
stops the feeder, 0.21 s after the pull, while both belts are still coasting.
The PLC also drops CV002's own run command, through its permissive. CV003
carries on and runs empty.

```text expected/pull-key.log
06:01:20.000  CV002.PullKey1  PULLKEY_PULLED  Actuated by the operator.
06:01:20.000  CV002.Safety  SAFETY_TRIP  Channel1 open; relay de-energised.
06:01:20.000  CV002.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.100  PERM_CV002  PERMISSIVE_LOST  CV002.SafetyOk dropped.
06:01:20.100  INT_CV001  INTERLOCK_TRIP  CV002.Contactor abnormal.
06:01:20.110  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:20.110  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.200  INT_CV002  INTERLOCK_TRIP  PERM_CV002.Ok abnormal.
06:01:20.200  INT_FEED  INTERLOCK_TRIP  CV001.Contactor abnormal.
06:01:20.210  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:01:27.820  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:27.920  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
```

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/pull-key.json --expect samples/mine-conveyors/expected/pull-key.log
```

## 3. Emergency stop

CV001's e-stop is pressed at 80 s. The relay stops CV001; on the next scan the
feeder's interlock sees CV001's auxiliary contact open and stops the feeder,
0.11 s after the press, long before CV001 has coasted to rest. CV002 and CV003
are downstream and keep running.

```text expected/e-stop.log
06:01:20.000  CV001.EStop  ESTOP_PRESSED  Actuated by the operator.
06:01:20.000  CV001.Safety  SAFETY_TRIP  Channel3 open; relay de-energised.
06:01:20.000  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.100  INT_FEED  INTERLOCK_TRIP  CV001.Contactor abnormal.
06:01:20.110  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:01:20.200  INT_CV001  INTERLOCK_TRIP  PERM_CV001.Ok abnormal.
06:01:27.810  CV001.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
```

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/e-stop.json --expect samples/mine-conveyors/expected/e-stop.log
```

## 4. Motor overload

A step of 1.0 in CV003's motor thermal state at 80 s — a blocked fan, a hot
restart — takes it past the overload relay's trip level at once. The whole line
stops behind it, one belt a scan, each on the auxiliary contact of the belt
below it: CV002 0.11 s after the trip, CV001 0.21 s, the feeder 0.31 s. The
three belts then coast to rest within about 0.2 s of one another. The current
does not rise: an overload trip opens the contactor, and the current falls to
zero.

```text expected/overload.log
06:01:20.000  CV003.Motor  FAULT  thermal-bias injected: amount=1.
06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.286 reached the trip level 1.1.
06:01:20.100  INT_CV003  INTERLOCK_TRIP  CV003.Tripped abnormal.
06:01:20.100  INT_CV002  INTERLOCK_TRIP  CV003.Contactor abnormal.
06:01:20.110  CV002.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.200  INT_CV001  INTERLOCK_TRIP  CV002.Contactor abnormal.
06:01:20.210  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.300  INT_FEED  INTERLOCK_TRIP  CV001.Contactor abnormal.
06:01:20.310  Feed.Enabled  WRITE  Set to false by INT_FEED.
```

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/overload.json --expect samples/mine-conveyors/expected/overload.log
```

## 5. Chute blockage

Ore bridges in CH1 at 80 s. The chute fills in 12.5 s; then CV001 cannot
discharge, the ore backs up along it, the belt loads, the current climbs through
`Hi` and `HiHi`, the motor heats, and the overload relay trips — the engine's
causal chain, with no rule anywhere that says "a blocked chute trips the belt
feeding it". Nothing interlocks CV001 before its own overload does. A real
transfer chute carries a blocked-chute probe interlocked to the belt feeding it,
which trips that belt within seconds; this sample deliberately has none, so the
overload chain can play out, and it does not interlock on `CH1.Full` instead —
that tag is the model's truth, not an instrument a PLC could wire. Once the
overload relay does trip, the feeder stops on the next scan, on CV001's
auxiliary contact.

```text expected/chute-blockage.log
06:01:20.000  CH1  FAULT  blockage injected.
06:01:32.500  CH1  FULL  Chute is full.
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.71 above 7.5.
06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.81 above 8.6.
06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.100 reached the trip level 1.1.
06:03:09.800  INT_CV001  INTERLOCK_TRIP  CV001.Tripped abnormal.
06:03:09.800  INT_FEED  INTERLOCK_TRIP  CV001.Contactor abnormal.
06:03:09.810  Feed.Enabled  WRITE  Set to false by INT_FEED.
```

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/chute-blockage.json --expect samples/mine-conveyors/expected/chute-blockage.log
```

## 6. Failed zero-speed switch

CV002's zero-speed switch fails low at 80 s: it reads stopped while the belt
runs at full speed. `INT_CV001` ORs the switch with CV002's auxiliary contact,
which still reads closed; either one abnormal trips it, so the PLC believes the
switch and stops CV001, and the feeder follows a scan later on CV001's auxiliary
contact. CV002 and CV003 never stop. This is a nuisance trip, and it is the
price of an interlock that reads the instrument.

```text expected/failed-zero-speed.log
06:01:20.000  CV002.ZeroSpeed  FAULT  fail-low injected.
06:01:20.990  CV002.ZeroSpeed  ZERO_SPEED  Speed below 0.02 m/s for 1 s.
06:01:21.000  INT_CV001  INTERLOCK_TRIP  CV002.Stopped abnormal.
06:01:21.010  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:21.010  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:21.100  INT_FEED  INTERLOCK_TRIP  CV001.Contactor abnormal.
06:01:21.110  Feed.Enabled  WRITE  Set to false by INT_FEED.
```

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/failed-zero-speed.json --expect samples/mine-conveyors/expected/failed-zero-speed.log
```

## 7. Welded contactor

CV003's contactor welds at 80 s. At 90 s the operator stops the line:
`SEQ_STOP` stops CV001 and CV002, then drops CV003's run command — and CV003
keeps running. The stop sequence times out waiting for CV003's switch and
faults. At 210 s the e-stop is pressed: the safety relay drops, the PLC trips
`INT_CV003` — and CV003 *still* runs, at full speed, to the end of the run. The
relay de-energises the contactor's coil, and welded contacts do not open when
their coil drops. The PLC's command and the hardwired safety circuit both act
through the one contactor, so one weld defeats both.

That is why a real safety circuit of Category 3 / PL d does not stop a motor
through a single contactor: it switches two in series, each able to break the
motor current alone, and monitors them — a normally-closed auxiliary contact of
each is fed back to the safety relay, which will not reset while either reports
closed. A weld is then detected at the next stop and cannot defeat the e-stop.
This sample's starter has one contactor and no feedback to the safety relay.
The PLC does read CV003's auxiliary contact, but only as run feedback for the
cascade: a welded contactor reads closed, so `INT_CV002` never trips on it, and
nothing compares that feedback with the run command (see "What this demo line
leaves out on purpose" below). A redundant, monitored safety-contactor starter
is a later component.

```text expected/welded-contactor.log
06:01:20.000  CV003.Starter  FAULT  contactor-welded injected.
06:03:01.610  CV003.Start  WRITE  Set to false by SEQ_STOP.
06:03:21.800  SEQ_STOP  SEQUENCE_FAULTED  Step 6 timed out after 20 s.
06:03:30.000  CV003.EStop  ESTOP_PRESSED  Actuated by the operator.
06:03:30.000  CV003.Safety  SAFETY_TRIP  Channel3 open; relay de-energised.
06:03:30.200  INT_CV003  INTERLOCK_TRIP  PERM_CV003.Ok abnormal.
06:03:30.210  CV003.Start  WRITE  Set to false by INT_CV003.
```

Nothing after that: CV003's contactor never opens, and its zero-speed switch
never reports it stopped — so `INT_CV002`, which reads both, never trips on
CV003.

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/welded-contactor.json --expect samples/mine-conveyors/expected/welded-contactor.log
```

## 8. Feed starve

The ore supply runs out at 80 s. Nothing trips, nothing stops and no alarm
raises: the belts run on, empty. The log shows only the fault:

```text expected/feed-starve.log
06:01:20.000  Feed  FAULT  starve injected.
```

The story is in the belt scales, not the log. Sampled every 100 ms, each scale
falls to 5 t/h or less, and stays there, in transport order: CV001 at 104.2 s,
CV002 at 133.7 s, CV003 at 153.7 s. The sample's tests check that order from
the scales' values, and check that nothing trips, stops or alarms.

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/feed-starve.json --expect samples/mine-conveyors/expected/feed-starve.log
```

## 9. A start while the line is tripped

An operator pulls CV002's pull-wire at 80 s, as in scenario 2, and the cascade
stops CV001 and the feeder. At 100 s, with the pull-key still out and nothing
reset, someone writes `CV001.Start` true and `Feed.Enabled` true, as a start
button on an HMI would. Both writes land, and nothing moves: when each
interlock tripped it wrote its device's `Permit` false as well as its command,
and the starter and the feeder AND their command with that permit, the way an
interlock contact sits in series in a real run circuit.

Then the line is put right, by hand: the pull-key is restored at 105 s,
CV002's safety relay reset at 106 s, `INT_CV002` reset at 108 s and CV002
started at 110 s; `INT_CV001` is reset at 115 s. That reset gives CV001 its
permit back — and, like a seal-in circuit broken by the interlock, drops the
start written at 100 s, so CV001 stays stopped. It runs only when the operator
starts it again, at 125 s. `INT_FEED`, reset at 130 s, likewise drops the
enable written at 100 s: the feeder stays off to the end of the run.

```text expected/start-while-tripped.log
06:01:20.100  INT_CV001  INTERLOCK_TRIP  CV002.Contactor abnormal.
06:01:20.110  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:20.110  CV001.Permit  WRITE  Set to false by INT_CV001.
06:01:20.110  CV001.Starter  CONTACTOR_OPENED  Motor de-energised.
06:01:20.200  INT_FEED  INTERLOCK_TRIP  CV001.Contactor abnormal.
06:01:20.210  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:01:20.210  Feed.Permit  WRITE  Set to false by INT_FEED.
06:01:40.000  CV001.Start  WRITE  Set to true.
06:01:40.000  Feed.Enabled  WRITE  Set to true.
06:01:55.100  INT_CV001  INTERLOCK_RESET  Reset with all conditions normal.
06:01:55.110  CV001.Start  WRITE  Set to false by INT_CV001.
06:01:55.110  CV001.Permit  WRITE  Set to true by INT_CV001.
06:02:05.000  CV001.Start  WRITE  Set to true.
06:02:05.000  CV001.Starter  CONTACTOR_CLOSED  Motor energised.
06:02:10.100  INT_FEED  INTERLOCK_RESET  Reset with all conditions normal.
06:02:10.110  Feed.Enabled  WRITE  Set to false by INT_FEED.
06:02:10.110  Feed.Permit  WRITE  Set to true by INT_FEED.
```

Between 100 s and the fresh start at 125 s, sampled every 100 ms, CV001's
speed stays under 0.01 m/s and its scale under 0.1 t/h; the feeder's hopper
stays empty from 100 s to the end. Before the permit existed, the writes at
100 s closed CV001's contactor at once and ran it at full speed onto the
stopped CV002 — and, left alone, would have filled CH1 at 114.26 s.

```bash
dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/start-while-tripped.json --expect samples/mine-conveyors/expected/start-while-tripped.log
```

## What this demo line leaves out on purpose

- `CVn.Start` and `Feed.Enabled` are held values, not momentary push-buttons:
  the interlock's reset writes stand in for the seal-in contact a real run
  circuit has. Anything that writes one of them true *after* the reset starts
  the device, exactly as `SEQ_START` does; that is the fresh start a real
  operator gives, but nothing here asks who gave it.
- The cascade reads the downstream contactor's auxiliary contact and the
  downstream zero-speed switch, not underspeed: a downstream belt that slows —
  slipping, say — with its contactor still closed does not stop the belts
  feeding it until its switch reports it stopped (see the underspeed item
  below). `CVn.Contactor` models an ideal auxiliary contact that always
  follows the main contacts: unlike the zero-speed switch in scenario 6, it
  has no fault of its own. On a real plant a mechanically linked auxiliary
  contact can disagree with them — a broken linkage, a wiring fault — and if
  it fails closed the cascade silently falls back to the zero-speed switch
  alone, which is why the zero-speed condition stays in each interlock.
- CV001 has no plugged-chute switch and no HiHi-current interlock of its own,
  so in the chute-blockage scenario above it runs about a minute at HiHi
  current until its thermal relay trips; see that section for why a real
  transfer chute would stop the feeding belt within seconds instead.
- No belt has underspeed (belt-slip) protection: on a real line, CV002 would
  trip when its speed sensor reads meaningfully below its running speed —
  roughly 80–90 % of it — while the belt is energised. `Interlock` conditions
  are binary, and the block set has no analog comparator, so underspeed
  cannot be expressed with these blocks today.
- No belt checks a switch-versus-command discrepancy either: a zero-speed
  switch that disagrees with the run command it is meant to confirm would go
  unnoticed here. Scenario 6 shows the opposite failure instead — a switch
  that fails and trips a belt that was never in trouble.
- No pre-start warning: a real belt line sounds a horn and waits before each
  belt starts, so anyone near it has time to clear — a statutory requirement
  in most mining jurisdictions. `SEQ_START` here starts every belt with no
  warning at all.
- `SEQ_START` and `SEQ_STOP` have no mutual exclusion between them, and
  `SEQ_START` does not abort if a belt trips partway through it: it keeps
  proving the next step regardless of what an interlock just did upstream.
- No fail-to-stop (run-feedback discrepancy) alarm: in the welded-contactor
  scenario above, `Start` goes false while `CV003.Contactor` stays true, and
  only the stop sequence's own timeout ever reports it. `Alarm` here only
  compares one analog value against a threshold, so it cannot watch two
  binary tags for disagreement.
- Each starter has a single contactor, which a weld defeats, as the
  welded-contactor scenario above shows. A real safety circuit uses two
  monitored contactors in series, so a weld in one is caught at the next stop
  instead of defeating the e-stop.
- A claimed permit cannot be forced: there is no override path for
  commissioning or fault-finding, as a PLC's force table would give. A
  scenario that writes `CV001.Permit` is refused before tick 0
  (`Tag 'CV001.Permit' is claimed by INT_CV001; a scenario cannot write it.`),
  so the way to move a device during a trip is the one the plant offers: put
  the fault right and reset its interlock.

## Power-up

The first second of every run is the same. Every interlock trips at 0.000 s:
each belt's because it reads its permissive, whose `Ok` starts false until
`SEQ_START` resets the safety relays, and `INT_FEED` because CV001's contactor
starts open. Each writes its device's command false and its `Permit` false at
0.010 s. `Feed.Enabled` is false already: the feeder is declared disabled, so
no ore moves until `SEQ_START`, a step after resetting `INT_FEED`, enables it at
10.21 s.
