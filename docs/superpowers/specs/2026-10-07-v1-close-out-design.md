# v1 Close-Out — Design (plan 7)

Date: 2026-10-07. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec").
Every item of main spec §17 (v1 scope) is merged, the two reference samples
included (plans 6a and 6b.2). This plan folds in the follow-ups those samples
surfaced and cuts the `v1.0.0` release.

**Amended 2026-10-07 by the plan**
(`docs/superpowers/plans/2026-10-07-v1-close-out.md`, rulings
R184–R193), where the code or a measured run forced a choice:

- **A reset edge on the scan that trips the interlock is not a refusal
  (R184)**: only an edge that finds it already latched with a condition
  abnormal logs `RESET_REFUSED`, once per edge. The condition named is the
  first abnormal one in declared order, not necessarily `FirstOut`.
- **Only `item-process-unit` has the wall time (R186)**: `bulk-process-unit`
  has no slow-cycle fault, so its hold time is its wall time. "Differs"
  means the two `F2` texts differ.
- **`OVERLOAD_TRIP` never reads below the trip level (R188)**, rather than
  always rounding up past it: rounding up moved the chute-blockage golden
  (`1.100` → `1.101`), which criterion 3 says must not move. The decimals
  are `max(3, d + 1)`, at most 6, as specified; the text is stepped up only
  when it would read below the level.
- **`STALLED` also reads above the breakdown's text (R189)**: when the
  breakdown torque's `F0` text rounds up to the demand's ceiling, the
  demand prints one more.
- **R188 and R189 supersede 6e R148** for `OVERLOAD_TRIP` and `STALLED`:
  those components now direct their rounding too; the alarm is unchanged.
  `STALLED` prints a demand too large for `decimal` (or non-finite) as plain
  `F0`, as before.
- **The release adds `ReleaseTests` (R191)**, two facts pinning the version,
  the changelog's links and the README's commands; every assembly except
  `Dse.Cli` was already 1.0.0 by the SDK default, and `Dse.Cli`'s own 0.1.0
  override was removed, so the `dse` tool and its package move from 0.1.0
  to 1.0.0. `Directory.Build.props` is now the single source, and
  `ReleaseTests` fails if a project under `src/` sets its own version.
- **"The merge commit" (criterion 6) reads "`master`'s head after the
  fast-forward" (R192)**: the repository integrates by fast-forward, so no
  merge commit exists; the tag goes on the commit the merge leaves at the
  head of `master`.
## 1. Scope

No new features and no change to any computed value. Four small follow-ups —
three change event text, one is test-only — then the release.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| Which follow-ups | All four: an interlock's refused reset is logged; a process unit's hold message gives wall time when it differs; `STALLED` and `OVERLOAD_TRIP` round away from their threshold; the two sample test helpers share one loader. |
| Refused reset | Logged only when a reset edge arrives while the interlock is tripped and a condition is abnormal; it names the first abnormal condition. A reset edge on an untripped interlock stays silent. |
| Hold vs wall time | Wall time appended only when it differs from the hold time — so only under `slow-cycle` — and every other hold message is unchanged. |
| Release | `CHANGELOG.md`, `<Version>1.0.0</Version>`, a "Getting started" section in the root README, and an annotated tag `v1.0.0` created locally; pushing the tag is a separate, owner-approved step. |

### Success criteria

1. **Refused reset (`Dse.Control` `Interlock`).** On a scan where `Reset` rises
   while the interlock is tripped and at least one condition is not normal, it
   raises event `RESET_REFUSED` with message
   `Reset refused: <tag> is not normal.`, naming the first condition, in
   declared order, that is not normal. No write is made and the latch holds.
   An accepted reset, a reset edge on an untripped interlock, and every other
   scan behave and log exactly as today.
2. **Hold vs wall time (`item-process-unit`; `bulk-process-unit` if it has the
   same hold timer — the plan confirms).** When a hold is satisfied and the
   elapsed wall time since the unit entered Processing differs from the hold
   time it counted, the message reads
   `Hold satisfied after <hold> s of hold (<wall> s elapsed); …` with both
   values formatted as today (`F2`) and the rest of the message unchanged.
   When they do not differ, the message is exactly as today.
3. **Directed rounding at thresholds (`Motor`, `MotorStarter`).**
   - `STALLED`: the torque demand is rounded up at `F0` (never shown at or
     below the breakdown torque it exceeds); the breakdown torque is formatted
     as today.
   - `OVERLOAD_TRIP`: the thermal state is printed with
     `max(3, d + 1)` decimals, capped at 6 — `d` the decimals of the trip
     level's shortest round-trip form, as the `alarm` counts them — rounded
     away from (up past) the trip level by format-then-step (6e R145). The
     floor of 3 keeps today's `F3` for the shipped trip level `1.1`, so no
     golden moves for this item; a trip level with 3 or more decimals gets
     one more.
4. **Shared sample loader (tests only).** `tests/Dse.Samples.Tests` has one
   loader used by both the mine-conveyor and wheel-line helpers: parse the
   scenario, load the plant, build, schedule the timeline, run cache, and the
   per-sample paths keyed by the sample's folder. Every existing test keeps its
   name and assertions; the test count does not change; no golden moves.
5. **Goldens move only by criteria 1–2.** The plan measures exactly which:
   `samples/wheel-line/expected/pyro-fail-high.log` gains one `RESET_REFUSED`
   line; the slowed hold lines of `slow-press.log` and `stuck-kicker.log` gain
   the wall time; any other golden with a refused reset gains its line. Each is
   regenerated with `DSE_UPDATE_GOLDEN=1` and read. README and documentation
   text that said a refused reset logs nothing, or that quotes a changed line,
   is updated.
6. **Release.**
   - `CHANGELOG.md` at the repository root with a single `1.0.0` entry, dated
     the release day: what v1 contains, by area (Core, Components, Io,
     Realtime, Control, Scenarios, Configuration, Cli, Samples), and links to
     the specs and plans.
   - `Directory.Build.props` sets `<Version>1.0.0</Version>`.
   - The root `README.md` gains a "Getting started" section — prerequisites
     (.NET 10 SDK), build and test commands, running a scenario of each sample
     against its golden with `dse run … --expect …`, and where to read next —
     and its plan-by-plan "Status" section is condensed to a short summary
     pointing at the changelog.
   - After the merge, an annotated tag `v1.0.0` on the merge commit, created
     locally. Pushing it is the owner's call.

## 2. Out of scope

- Any new component, block, adapter or physics.
- Changing how the hold timer counts, how an interlock resets, or any trip or
  stall threshold — only their messages change.
- Emptying an `item-sink`, an Int64-to-Double conversion for alarms, and the
  OPC UA / Modbus adapters (later plans).
