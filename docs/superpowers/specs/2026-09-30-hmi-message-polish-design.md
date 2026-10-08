# HMI Message Polish — Design (plan 6e)

Date: 2026-09-30. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining the event messages of the component library (plan 3), the control
blocks of `2026-09-22-control-blocks-design.md` (5c), and the claim diagnostics
of `2026-09-30-block-claimed-tags-design.md` (6d). All merged.

**Amended 2026-09-30 by the plan**
(`docs/superpowers/plans/2026-09-30-hmi-message-polish.md`, rulings
R143–R154), where the code forced a choice:

- **Directed rounding starts from the nearest `F<n>` text and steps it at
  most once (R145)**, when that text lies on the wrong side of the value;
  scaling by 10ⁿ is wrong in binary (`Math.Ceiling(1.1 * 100) / 100` is
  `1.11`), and a value whose `F<n>` text parses back to the same double is
  shown as that text and never stepped (so `1.1` over 0.5 prints `1.10`).
  Where the spacing of doubles reaches the display step, the display is the
  nearest `F<n>` text, still beyond the limit.
- **A raise may overstate by up to one display step (R145)**: a Hi of
  10.0008 over 10 prints `10.1` (the worked example's `10.000765680139894`
  over 8 prints `10.1`).
- **The limit's decimals include its exponent (R144):** `1e-5` (`"1E-05"`)
  has 5, so its alarm prints 6.
- **A negative zero prints as zero in both alarm messages (R146).**
- **`F<n>` rounds an exact binary midpoint half to even (R147)**, e.g.
  `0.125` → `0.12`, and the 12.5 N·m breakdown torque of a small motor → `12`.
- **The `MR016` hint searches only the block's `Writes` the plant has, then
  every tag (R149)**; `Suggest.Closest` sorts its candidates, so ties go to
  the ordinally first name within each set.
- **Criterion 6's check is a script over git history (R151)**, not a test.

## 1. Scope

Event messages print measured values as full-precision doubles:

```
06:00:05.400  CV003.Motor  AT_SPEED  Reached 146.17986855114356 rad/s.
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705084760820622 above 7.5.
06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000357303409216 reached the trip level 1.1.
```

An alarm journal or HMI event list shows engineering precision. This plan gives
every measured value in an event message a fixed display format, so the log
reads like the one a SCADA practitioner expects, and the goldens change once,
deliberately. It also gives the 6d claim diagnostic's "which the plant does not
have" case a nearest-name hint, as other unknown-name diagnostics already have.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| Precision rule | **Fixed decimals per quantity**, chosen per message the way a tag's display format is configured. (Rejected: one significant-figures rule everywhere — uniform, but alarm values show more digits than an HMI would; a per-tag display-format attribute published in the directory — most realistic, but a descriptor change and a value for every instrument.) |
| An alarm value that rounds onto its limit | An alarm prints its value with one more decimal than its limit has, and **a raise rounds away from the limit**, so a raised value never displays equal to or inside its limit. |
| Shared helper? | **No.** `Millrace.Control` references only `Millrace.Io.Abstractions`; each site changes one format. The alarm keeps its decimals rule private. |
| MR016 hint | `Suggest.Closest` only, not `Suggest.Fix`: listing eight of a plant's ~124 tags is noise. |

### Success criteria

1. Measured values in event messages use these formats, all with
   `CultureInfo.InvariantCulture` and the standard fixed-point format `F<n>`:

   | Event (source) | Value | Format | Example |
   |---|---|---|---|
   | `AT_SPEED` (`Motor`) | speed, rad/s | `F1` | `Reached 146.2 rad/s.` |
   | `STALLED` (`Motor`) | torque demand and breakdown torque, N·m | `F0` | `Torque demand 1843 N·m exceeds breakdown torque 1790 N·m.` |
   | `OVERLOAD_TRIP` (`MotorStarter`) | thermal state | `F3` | `Thermal state 1.100 reached the trip level 1.1.` |
   | hold satisfied (`BulkProcessUnit`) | elapsed s; batch mass kg | `F2`; `F1` | `Hold satisfied after 30.00 s; discharging 2000.0 kg of …` |
   | hold satisfied (`ItemProcessUnit`) | elapsed s | `F2` | `Hold satisfied after 30.00 s; discharging 4 items.` |
   | `ALARM_RAISED`, `ALARM_CLEARED` (`Alarm`) | the value | `F<d+1>` (below) | `Hi: 7.71 above 7.5.` |

   The breakdown torque is a product of configured values but is printed as a
   measured one (`F0`), since it is computed. The examples show the formats;
   their numbers are illustrative except where a golden already contains them.
2. **The alarm's decimals.** *d* is the number of digits after the decimal
   point in the limit's shortest round-trip form (`"R"`, invariant culture;
   0 for a whole number), and the value prints with `min(d + 1, 6)` decimals.
   Limit 7.5 → 2 decimals; limit 100 → 1; limit 0.125 → 4.
3. **The alarm's rounding direction.** `ALARM_RAISED` rounds away from the
   limit: up (ceiling at that many decimals) for `Hi`/`HiHi`, down (floor) for
   `Lo`/`LoLo`, then formats with `F<n>`. A Hi value 7.5004 over 7.5 prints
   `7.51`; a Lo value 1.9996 under 2 prints `1.9`. `ALARM_CLEARED` formats the
   value with `F<n>` directly.
4. Configured values in messages — limits, the trip level, the zero-speed
   threshold and delay — print exactly as today.
5. Unchanged: the `WRITE` log (`Set to … .`), every tag value in the image,
   frames and telemetry, and every other message. This is display text only.
6. **Goldens change only in the formatted numbers.** Every event log in the
   solution — the nine sample goldens, `tests/Millrace.Control.Tests/Golden/conveyor-control.log`,
   the `Millrace.Scenarios.Tests` goldens — is regenerated with `MILLRACE_UPDATE_GOLDEN=1`
   and checked mechanically against its previous version: the same number of
   lines, and on every line the same time, source and event, with only numbers
   in the message text differing. README and doc quotes of those lines follow.
7. **MR016 hint.** When a claim names no tag, Core looks for the nearest name
   with `Suggest.Closest`, first among the claiming block's `Writes`, then among
   all plant tags. If one is found, the fix reads
   `Check the name against 'millrace tags' — '<closest>' is closest; a block claims a tag it commands.`
   If none is found, the message is exactly as today. Matching stays ordinal
   and exact (6d R137); the hint only suggests.

## 2. Components (`Millrace.Components`)

`Motor` (`AT_SPEED`, `STALLED`), `MotorStarter` (`OVERLOAD_TRIP`),
`BulkProcessUnit` and `ItemProcessUnit` (hold satisfied) change their
interpolation holes to the formats of criterion 1. Existing tests that assert
these texts change to the new text and nothing else.

## 3. Control (`Millrace.Control`)

`Alarm` formats its value by criteria 2 and 3 through one private static
method (the decimals rule and the directed rounding), used by both messages.
Its unit tests cover a Hi and a Lo raise just past the limit, a whole-number
limit, the 6-decimal cap, and a clear.

## 4. Core (`Millrace.Core`)

`SimulationBuilder.CheckClaims`' "which the plant does not have" MR016 gains
the hint of criterion 7. The hint sits in the fix half after the first `". "`;
the quoted names contain no `". "`, so the loader's split after the quoted
claim (6d R133) still holds. Tests: a near-miss claim (`cv003.permit` against
`CV003.Permit`) gets the hint in Core and, through a plant file, on the
`Fix:` line of `millrace validate`; a far-off claim does not.

## 5. Goldens and documentation

Every golden that moves is regenerated and read, and the criterion 6 check is
run and quoted. `samples/mine-conveyors/README.md`, `docs/control-blocks.md`
and any other document quoting a changed line are updated to the regenerated
text; documentation tests that pin those quotes follow. The diagnostics
reference does not change (MR016's summary still holds).

## 6. Out of scope

- Units: the motor's speed stays in rad/s; no rpm or belt-speed conversion.
- A per-tag display-format attribute.
- A hint on Core's MR014 unknown-pin message (plant files report MR113 first).
- Any change to how values are computed or when events fire.
