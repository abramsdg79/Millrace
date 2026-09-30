# Discrete Reject Physics — Design (plan 6b.1)

Date: 2026-09-30. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining the discrete flow of the material-flow plan (2) and the component
library (3), and adding a block to the control blocks of
`2026-09-22-control-blocks-design.md` (5c). All merged. This is the first half of
plan 6b; the wheel-line sample itself is plan 6b.2, with its own spec.

## 1. Scope

Main spec §15.2 gives the wheel line's causal chain: the press runs slow →
blanks queue on the conveyor → the furnace cannot discharge → billets over-soak
→ a temperature interlock rejects them. Most of that chain already works —
`item-source`, `item-process-unit` as furnace and press (`thermal-transfer`
toward the zone temperature, `residence-accumulator`, `output: Wheel`, yield),
`discrete-belt` with `thermal-transfer` for cooling in transit, `pyrometer`, and
backpressure through `CanAcceptItem` from the press back to the source. Three
things do not:

1. **A blocked batch freezes.** `ItemProcessUnit` runs its transforms only while
   Processing, so a batch held in Discharging — jammed, or blocked downstream —
   stops heating and stops soaking. The chain's over-soak cannot happen.
2. **No graded slow-cycle fault.** The only process-unit fault is
   `discharge-jam`, and its `ApplyFault` ignores the fault id.
3. **No reject path, and no control block that can drive one.** There is no
   diverter for discrete items, and the only control blocks that write tags
   (`interlock`, `sequencer`) write on events, not continuously.

Over-soak is detected as temperature, the realistic way: a furnace zone is
hotter than the discharge target, so a billet held too long climbs toward the
zone temperature and a pyrometer at discharge sees it.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| Decomposition | **6b.1 physics and the reject mechanism** (this spec); **6b.2 the wheel-line sample** (`samples/wheel-line/`, data only, like `samples/mine-conveyors/`), its own spec later. |
| Who decides a reject | **The PLC.** Pyrometer → `alarm` HiHi → a new **`coil`** block that writes a bool tag to follow a condition → the reject gate's writable `Reject`. The coil claims that tag (6d). (Rejected: the gate deciding from its own temperature limit — the interlock would live in a component; an interlock with auto-reset — bends latch-and-reset.) |
| When the reject decision lands | A **measuring station**: the gate holds each item for a dwell, so the pyrometer, the alarm scan and the coil's write all complete while the item is still on the station. |
| A blocked batch | Opt-in **`heatWhileHeld`** on `item-process-unit`; default off, so nothing existing changes. |
| The slow press | A graded **`slow-cycle{fraction}`** fault, copying `belt-slip{fraction}`. |
| How often the coil writes | **On change, plus once on its first scan.** A real output coil rewrites every scan; with a claim nobody else can change the tag, so writing on change gives the same tag state without a `WRITE` line every scan. |

### Success criteria

1. `item-process-unit` accepts `heatWhileHeld` (bool, default false). When
   true, its transforms run on every tick it holds items — Filling, Processing
   and Discharging, jammed or blocked — not only in Processing. The hold timer
   (`elapsed`, `Progress`) still counts only in Processing. When false the unit
   behaves exactly as today; every existing test and golden is unchanged.
2. `item-process-unit` supports fault `slow-cycle` with argument `fraction`
   (default 0.5, clamped to [0, 1]): while applied, the hold timer advances by
   `dt × (1 − fraction)`. A 10 s timed hold takes 20 s at 0.5; at 1 the timed
   hold never completes. Temperature and state holds are unaffected (they
   depend on the items, not the timer). Clearing it restores full rate from
   that tick; time already counted stays counted. `ApplyFault`/`ClearFault`
   switch on the fault id: `discharge-jam` and `slow-cycle` are independent and
   may be active together.
3. A new component **`reject-gate`** (`Dse.Components`, catalogue type
   `reject-gate`):
   - Flow: discrete inlet `In`; discrete outlets `Out` and `Reject`.
   - Parameter `dwellSeconds` (> 0).
   - Input `Reject` (bool, default false), published as a read-write tag;
     outputs `Occupied` (bool), `Passed` and `Rejected` (counts), published
     read-only. Telemetry `Passed`, `Rejected`.
   - It holds at most one item. It accepts an item only when empty. The item
     stays for `dwellSeconds`; on the first tick the dwell is complete it is
     offered on `Out` if `Reject` is false, or on `Reject` if it is true —
     read on that tick. If that outlet's consumer refuses it, the item waits and
     the choice is taken again on each later tick. An item leaving by `Reject`
     logs `REJECTED` — `Item <id> rejected.`
   - It is an `IMaterialObservable`: a pyrometer targeting it sees the item on
     the station, or its background when empty.
   - Fault `stuck` (no arguments): the kicker does not fire — every item leaves
     by `Out` whatever `Reject` says — until cleared.
   - Mass is conserved: every item that enters leaves by exactly one outlet.
4. A new control block **`coil`** (`Dse.Control`, catalogue type `coil`):
   - Parameters `condition` — `{ "tag": <bool tag>, "normal": <bool> }`, the
     shape the interlock and permissive use — and `output` (a bool tag).
   - The coil is energised while the condition's tag equals `normal`, with the
     same treatment of tag quality as the interlock's conditions.
   - It writes `output` = energised on its first scan and on every scan where
     that value changes, and at no other time.
   - Owned output `Energised` (bool).
   - `output` must be a read-write bool tag the coil commands (its `Writes`);
     the existing pin checks (DSE014) report otherwise. It may be claimed
     (6d `claims`).
5. Catalogue, plant schema and documentation: `reject-gate`, `coil`, the
   `heatWhileHeld` parameter and the `slow-cycle` fault appear in the catalogue
   with descriptions, in the generated plant schema (its golden regenerated
   deliberately), and in `docs/` where components, faults and control blocks are
   documented. Main spec §18's "splitters … deferred" is amended to say a
   routed reject station exists; general splitters and mergers stay deferred.
6. An integration test in `Dse.Components.Tests`, built with the
   `SimulationBuilder` like the existing flow tests, proves the physical half
   of the chain: an item source → furnace (`heatWhileHeld`, zone above the
   discharge target) → reject gate → discrete belt → press. With the press
   slowed by `slow-cycle`, blanks queue, the furnace is held in Discharging, and
   a held billet's temperature rises past a limit it would not reach unblocked;
   with `Reject` written true the gate routes it to a reject sink. The full PLC
   chain (pyrometer → alarm → coil) is proven in 6b.2.

## 2. Components (`Dse.Components`)

`ItemProcessUnit` gains the parameter and fault of criteria 1–2; its descriptor
gains both. `RejectGate` is a new leaf in `Flow/` with its descriptor; it
provides tags (`ITagProvider`), faults (`IFaultTarget`) and observation
(`IMaterialObservable`) as the existing leaves do. The item hand-off follows
the discrete protocol unchanged: `CanAcceptItem`, `TryPeekItem`/`WithdrawItem`
per outlet, one tick per hand-off. No Core change is expected; the plan confirms
it.

## 3. Control (`Dse.Control`, `Dse.Control.Catalogue`)

`Coil` is an `IScanBlock`: `Inputs` = the condition's tag; `Writes` = the
output tag; `Outputs` = `Energised`; no commands. It keeps the last value it
wrote and whether it has scanned. Its catalogue entry binds `condition` and
`output` like the interlock's parameters.

## 4. Testing

- `item-process-unit`: `heatWhileHeld` heats a jammed batch and a batch
  blocked downstream, and heats nothing extra when false; `slow-cycle` stretches
  a timed hold by `1 / (1 − fraction)`, stops it at 1, restores on clear, and
  coexists with `discharge-jam`; the `Progress` output stays consistent.
- `reject-gate`: dwell timing; routing on the `Reject` value read at release;
  backpressure on each outlet, including a `Reject` change while waiting; one
  item at a time; observation by a pyrometer; `stuck`; mass conservation; the
  `REJECTED` event.
- `coil`: energises and de-energises with its condition; `normal: false`
  inverts; writes on the first scan and only on transitions; quality handling
  as the interlock; catalogue and plant-file binding; a claim on its output.
- Catalogue and schema: descriptors, conformance, the plant schema golden.
- The integration test of criterion 6.
- Every existing golden is unchanged except the plant schema golden (and any
  catalogue golden that lists every type), which moves by the additions only.

## 5. Out of scope

- The wheel-line sample, its controllers, scenarios and goldens (6b.2).
- General splitters and mergers; a discrete conveyor composite (motor, starter,
  sensors around a discrete belt); a zone-temperature controller or burner
  model; a `Permit` on `item-source`.
- Carrying item state through a re-type, and exposing item state to
  instruments.
