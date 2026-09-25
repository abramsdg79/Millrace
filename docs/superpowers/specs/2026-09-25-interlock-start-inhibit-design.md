# Interlock Start Inhibit — Design (plan 6c)

Date: 2026-09-25. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining the interlock of `2026-09-22-control-blocks-design.md` (5c) and the
mine-conveyor sample of `2026-09-25-mine-conveyor-sample-design.md` (6a, with
6a.1). All merged.

## 1. Scope

The 5c interlock writes its trip values once, on the trip scan (R71). It does
not hold the output off, so anything that later writes `Start` or `Enabled`
true while it is tripped starts the device. The 6a final review confirmed this
with a probe: after a pull-key, writing `CV001.Start = true` closed CV001's
contactor and ran the belt onto the stopped CV002 while `INT_CV001` was still
tripped. A real PLC interlock is a contact in series in the run circuit; a
start while tripped closes nothing.

This plan makes an interlock a true start inhibit, the way a plant does it: a
**run permit** on the device, driven by the interlock.

Out of scope, named: block outputs wired directly to component inputs (an
engine binding change, considered and not chosen); a start-refused event or
alarm; redundant safety contactors; any other block's semantics.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| Where the inhibit lives | **A `Permit` input on the device,** ANDed into its run logic, like an interlock contact in series in the MCC run circuit. Not "the interlock re-asserts its writes" (leaves a one-scan contactor blip) and not "block outputs drive component inputs" (a Core binding change). |
| How the interlock drives it | **Reset writes:** the interlock gains optional writes sent once on the scan that accepts a reset, mirroring its trip writes. `Permit = false` on trip and `Permit = true` on reset hold the inhibit with two log lines per trip-and-reset, not one per scan. |
| The feeder | **Included:** `bulk-source` gains the same `Permit`, so every interlocked device in the sample is truly inhibited. |
| Restart on reset | **No.** The trip still drops `Start` / `Enabled`, so a reset alone never restarts a device; a fresh start command is needed. |

### Success criteria

1. A `motor-starter` (and so a `conveyor`) whose `Permit` is false keeps its
   contactor open whatever its command, and closes normally once `Permit` is
   true and the other conditions hold. A welded contactor still defeats it.
2. A `bulk-source` whose `Permit` is false creates no mass whatever `Enabled`
   says.
3. An interlock sends its reset writes exactly once, on the scan that accepts a
   reset, and never on a refused reset.
4. In the mine-conveyor sample, a start or enable written while the cascade is
   tripped closes no contactor and moves no ore (new scenario 9), and the eight
   existing scenarios still tell their stories.
5. With `Permit` and reset writes left at their defaults, every existing plant
   behaves exactly as before: the 5b goldens, the 5c worked-example golden and
   every other event-log golden outside `samples/mine-conveyors/` are unchanged.
   The catalogue and schema goldens change only by additions.
6. Zero package references under `src/`; `Dse.Control` still references only
   `Dse.Io.Abstractions`; Release build with zero warnings; every existing test
   passes, updated only where criterion 5 allows.

## 2. Components (`Dse.Components`)

**`MotorStarter`** gains an input `Permit` (Bool, default true) and a writable
tag `Permit` — "Run permit; false holds the contactor open". Its run logic:

```
closed = welded || (Command && SafetyOk && Permit && !tripped && !open)
```

No event is logged when a command is refused; a real MCC does not log it, and
the tests prove the inhibit by the absence of `CONTACTOR_CLOSED`. The descriptor
gains the port and the tag; conformance proves them.

**`conveyor`** exposes `Permit` (`CVn.Permit`), routed to its starter.

**`bulk-source`** gains an input `Permit` (Bool, default true) and a writable tag
`Permit`; it creates mass only while `Enabled && Permit`. Its existing optional
`enabled` parameter (6a) is unchanged.

## 3. The interlock (`Dse.Control`, `Dse.Control.Catalogue`)

`Interlock` gains an optional list of **reset writes** (`BlockWrite`s, default
empty), validated like the trip writes (one write per tag). On the scan that
accepts a reset — the existing rising edge of `Reset` while every condition is
normal — it raises `INTERLOCK_RESET` as today and sends each reset write once.
A refused reset sends nothing. The block's `Writes` pins are the union of the
trip and reset targets; a tag may appear in both lists (that is the permit
pattern), and the block declares one write pin per distinct tag.

The catalogue descriptor gains `reset: [BlockWrite]`, default empty, described
as "What to command when a reset is accepted, once, on the reset scan." The
constructor keeps its existing signature as an overload, so every existing
call site and test compiles unchanged.

The documented pattern (`docs/control-blocks.md`): an interlock that must
inhibit a start writes the device's `Permit` false on trip and true on reset,
and drops its run command on trip.

## 4. The mine-conveyor sample

`samples/mine-conveyors/plant.json`:

| block | trip writes | reset writes |
|---|---|---|
| `INT_CV001..3` | `CVn.Start` false, `CVn.Permit` false | `CVn.Permit` true |
| `INT_FEED` | `Feed.Enabled` false, `Feed.Permit` false | `Feed.Permit` true |

`SEQ_START` needs no change: it resets each interlock before commanding its
device, so the permit is true by the time the command lands. The plan measures
this rather than assuming it (a reset write and a start write landing on the
same tick is the case to check), and reports it.

All eight goldens are regenerated and read. They gain `Permit` write lines at
the power-up trips, at each `SEQ_START` reset and at each cascade trip; the
stories and their chains are otherwise unchanged, and the chains are re-pinned.

**Scenario 9, `start-while-tripped`:** a pull-key on CV002 trips the cascade;
at 100 s an operator writes `CV001.Start = true` and `Feed.Enabled = true`. The
chain shows both writes landing; the absences show no `CV001.Starter`
`CONTACTOR_CLOSED` and no feed restart from the writes to the end of the run;
a state check shows `CV001.TonnesPerHour` staying at (near) zero. Golden,
chain, absences, replay and README section as for the other eight.

The README's "interlock trips once and does not hold the output off" bullet is
replaced by how the inhibit works (the permit, the paired writes, no restart on
reset) with a pointer to scenario 9.

## 5. Testing

- `Dse.Components.Tests`: a starter with `Permit` false stays open under a true
  command and closes when `Permit` goes true; a welded starter closes regardless;
  a source with `Permit` false creates nothing; descriptors and conformance.
- `Dse.Control.Tests`: pure interlock tests — reset writes on the accepted reset
  scan only, none on a refused reset, none on a trip; the permit pattern (same
  tag in trip and reset writes) declares one pin; constructor validation
  (duplicate tag within the reset list).
- `Dse.Control.Catalogue.Tests`: the `reset` parameter binds and builds; the
  control-catalogue golden regenerated (additions only).
- Catalogue and schema goldens regenerated (additions only), read and checked.
- `Dse.Samples.Tests`: the eight stories re-pinned, scenario 9 added (all its
  rows), the README quote test covering scenario 9.

## 6. Documentation

- `docs/control-blocks.md` — the interlock's reset writes; the permit pattern as
  the way to make an interlock inhibit a start; the sample as its example.
- `samples/mine-conveyors/README.md` — as section 4 says; scenario 9's section.
- `docs/authoring-a-component.md` — only if it lists the starter's ports.
- The 6a spec and the 5c spec gain a dated amendment line pointing here.

Global constraints from 5a–6a apply unchanged: determinism, goldens generated
and read, never invented, event messages as sentences ending in a full stop.
