# Wheel Line Sample — Design (plan 6b.2)

Date: 2026-10-02. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec")
§15.2, building on `2026-09-30-discrete-reject-physics-design.md` (6b.1:
`heatWhileHeld`, `slow-cycle`, `reject-gate`, `coil`, the R168 dwell rule) and
following the shape of `2026-09-25-mine-conveyor-sample-design.md` (6a). All
merged.

**Amended 2026-10-02 by the plan**
(`docs/superpowers/plans/2026-10-02-wheel-line-sample.md`, rulings
R171–R183), where the code or a measured run forced a choice:

- **`ALM_QUEUE` watches a hot-metal detector (R173).** An `alarm` reads a
  Double tag and `CV.ItemCount` is Int64 (`DSE114`), so `ALM_QUEUE` raises on
  `HMD.Value`, a pyrometer aimed at the belt's queue-full position (4 m, 5 s
  lag; `Hi` 200 °C, 60 s on-delay). `CV.ItemCount` stays read-bound.
  Criterion 1 gains `HMD` (a second `pyrometer`, observing `CV`); criteria
  2 and 4 read "the belt queue" as that detector.
- **The saw sets the line's rate (R172)**: one billet a minute; the 40 s
  press has spare capacity, so `normal-run` makes one wheel per billet.
- **`ALM_ZONE` has a 5 s on-delay (R174)**: the tick-0 scan reads the primed
  20 °C, not the line-start write.
- **The dwell is 1 s (R175)**: ⌈1.0 / 0.1⌉ = 10 ≥ 3, seven ticks to spare;
  measured HiHi at N + 2, the write at N + 4, `REJECTED` at N + 11.
- **A producing scenario ends its shift with `Billets` `starve` (R176)**, so
  its log falls quiet; `Billets.Enabled` is claimed and cannot be written.
- **A jam cannot fill the bay (R177).** While the belt is full a good billet
  waits on the gate and hides the over-soaking one behind it, and an
  `item-sink` cannot be emptied. `press-jam` shows the hidden over-soak,
  then the reject when the jam is cleared; `pyro-fail-high` fills the bay:
  `FULL`, `INT_BAY` trips, the saw stops, a reset is refused and the line
  ends held.
- **`ALM_PYRO`'s `Hi` and `HiHi` raise together (R179)**: the reading jumps
  from the empty station's 20 °C.
## 1. Scope

The second reference sample, in a different domain from the mine conveyors:
discrete items with per-item temperature, a type change at the press and a
cycle-time-limited line. It demonstrates main spec §15.2's causal chain end to
end in the event log — the press runs slow → blanks queue on the conveyor → the
furnace cannot discharge → billets over-soak → the PLC rejects them — together
with the line protections a real cell carries. The sample is a data folder,
`samples/wheel-line/`, with no C#.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| How much PLC logic | **The reject chain plus line protection**: pyrometer alarm → `coil` → reject gate; a queue-high alarm; zone temperature alarms; a reject-bay-full interlock that stops the billet source. No start sequencer: the t = 0 scenario writes are the line start. (Rejected: the reject chain only — too thin for a reference sample; a full line PLC like 6a — repeats 6a's sequencing story.) |
| Line start | Every scenario opens with t = 0 writes of the furnace zone setpoint and the belt speed (6b.1 R169): a plant file can give an input no initial value, and no constant component exists. The README presents them as the operator bringing the line up. |
| Claims | `COIL_REJECT` claims `GATE.Reject`; `INT_BAY` claims `Billets.Enabled` (6d). |
| Pyrometer | `lagSeconds` 0, aimed at the gate with `positionM`/`windowM` 0 (6b.1 R168/R169). |

### Success criteria

1. `samples/wheel-line/plant.json` loads with no diagnostic and agrees with the
   generated plant schema. Its flow:
   `Billets` (`item-source`) → `FCE` (`item-process-unit`) → `GATE`
   (`reject-gate`) → `CV` (`discrete-belt`) → `PRESS` (`item-process-unit`) →
   `Wheels` (`item-sink`); `GATE`'s `RejectOut` → `Bay` (`item-sink` with a
   capacity). `PYRO` (`pyrometer`) observes `GATE`.
   - `FCE`: `heatWhileHeld: true`, `thermal-transfer` toward its zone,
     `hold` `temperature-at-least` the discharge target (about 1100 °C); its
     `AmbientTemperature` is write-bound through the plant `tags` envelope.
   - `CV`: `thermal-transfer` (cooling in transit); `Speed` write-bound;
     `ItemCount` read-bound.
   - `PRESS`: `batchSize` 1, a timed `for-seconds` cycle, `output` `Wheel`,
     `yield` below 1 (flash).
   - Materials `Billet` and `Wheel` (discrete).
2. Controllers, all scanning every 100 ms:
   - `ALM_PYRO` — `alarm` on `PYRO.Value`: Hi (warning) and HiHi (over-soak).
   - `COIL_REJECT` — `coil` following `ALM_PYRO.HiHi.Active`, output
     `GATE.Reject`, claiming it.
   - `ALM_QUEUE` — `alarm` Hi on `CV.ItemCount` (the press falling behind).
   - `ALM_ZONE` — `alarm` Lo and Hi on the furnace zone setpoint.
   - `INT_BAY` — `interlock` on `Bay.Full` (normal false): the trip writes
     `Billets.Enabled` false; the reset writes it true; claims
     `Billets.Enabled`.
3. The gate's `dwellSeconds` satisfies R168 for these scan periods with margin
   (at least one extra tick); the README shows the arithmetic.
4. Numbers are measured by the plan and chosen so that: in steady state every
   billet reaches the gate within a few degrees of the discharge target, with
   clear margin below `ALM_PYRO` Hi; an over-soaked billet in `slow-press`
   reaches well above HiHi; the belt queue in steady state stays below
   `ALM_QUEUE` Hi.
5. Six scenarios, each with a golden event log in `samples/wheel-line/expected/`,
   each opening with the line-start writes, each with a quiet tail of at least
   500 ticks after its last event:

   | Scenario | Action | The log shows |
   |---|---|---|
   | `normal-run` | none | Steady production; no alarm, no reject; wheels at the press rate. |
   | `slow-press` | `PRESS` `slow-cycle` 0.9, cleared later | `ALM_QUEUE` Hi → `FCE` held → over-soak → `ALM_PYRO` Hi, HiHi → `Set to true by COIL_REJECT.` → `REJECTED` → `Reject` false again; after the clear the queue drains and production recovers. |
   | `stuck-kicker` | as `slow-press`, plus `GATE` `stuck` | HiHi and the coil's write as in `slow-press`, but no `REJECTED`: the over-soaked billet becomes a wheel. |
   | `press-jam` | `PRESS` `discharge-jam` | The line backs up; rejects accumulate; `Bay` `FULL`; `INT_BAY` trips and the source stops. Recovery (clear the jam, reset `INT_BAY`) is shown if the bay can be emptied from a scenario; otherwise the scenario ends with the line held, and the plan rules on it. |
   | `zone-low` | zone setpoint written below the discharge target | `ALM_ZONE` Lo; billets never reach target; the furnace never discharges; the press starves. |
   | `pyro-fail-high` | `PYRO` `fail-high` | Every billet reads hot and is rejected, good ones included — the reject's fail-safe direction. |

6. `Dse.Samples.Tests` gains tests mirroring the mine-conveyor sample's: the
   plant validates and agrees with the schema; every scenario matches its
   golden; record and replay are byte-identical; each scenario's quiet tail;
   `dse tags` lists `GATE.Reject` claimed by `COIL_REJECT` and
   `Billets.Enabled` claimed by `INT_BAY`; a causal-chain test for
   `slow-press` asserting the event order and traces of the held billet's
   temperature (past HiHi only while the press is slow) and of `CV`'s item
   count; a `stuck-kicker` test asserting a wheel made from an over-soaked
   billet; mass conservation in every scenario.
7. `samples/wheel-line/README.md` explains the line and why it is a different
   domain, gives the flow and controller tables and the line-start writes,
   walks each scenario with quoted golden lines and the reason behind them,
   applies the R168 dwell rule to this line's numbers, and lists known limits
   (no burner or zone controller, no descaler, one press, lumped billet
   temperature, the zone held as a written setpoint). Documentation tests pin
   its quotes as the mine-conveyor README's are pinned.
8. The root `README.md` and `docs/` refer to the sample where they list
   samples; main spec §15.2 says the wheel line is implemented.

## 2. Out of scope

- Any new component, block or Core change. If the plan finds the sample
  cannot be built from what exists (for example, emptying a full reject bay),
  it rules on the smallest scenario-level way round it and says so in the
  README; a code change needs a new spec.
- A start/stop sequencer, a burner model, several presses or a descaler.
