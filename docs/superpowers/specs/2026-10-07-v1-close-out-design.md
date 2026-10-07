# v1 Close-Out — Design (plan 7)

Date: 2026-10-07. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec").
Every item of main spec §17 (v1 scope) is merged, the two reference samples
included (plans 6a and 6b.2). This plan folds in the follow-ups those samples
surfaced and cuts the `v1.0.0` release.

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
