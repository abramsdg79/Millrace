# HMI Message Polish Implementation Plan (plan 6e)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every measured value in an event message a fixed display
format — the motor's speed to 0.1 rad/s, its torques to whole N·m, the
starter's thermal state to three decimals, a process unit's hold time to
0.01 s and batch mass to 0.1 kg, and an alarm's value to one more decimal than
its limit, rounded away from the limit on a raise — regenerate every event-log
golden once, checked mechanically to differ only in those numbers, and give
the 6d `MR016` "which the plant does not have" diagnostic a nearest-name
hint.

**Architecture:** Display text only. Four `Millrace.Components` message sites change
an interpolation hole to `F<n>` (`Motor`, `MotorStarter`, `BulkProcessUnit`,
`ItemProcessUnit`). `Millrace.Control.Alarm` gains one private static method,
`Display(value, limit, direction)`, that derives the decimals from the limit's
shortest round-trip form and rounds up, down or to nearest; both alarm messages
use it. `Millrace.Core.SimulationBuilder.CheckClaims` builds the "does not have" fix
through a static local function that asks `Suggest.Closest` first among the
claiming block's `Writes` the plant has, then among every tag. No value that
reaches the tag image, a frame or telemetry changes; no event fires at a
different time.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401, runtime 10.0.12), C#, xUnit
2.9.3. No package under `src/`. JsonSchema.Net 8.0.5, test-only and pinned
(unchanged).

**Spec:** `docs/superpowers/specs/2026-09-30-hmi-message-polish-design.md` (all
of it, as amended by this plan — R154 gives the amendment note the controller
adds to the spec with the plan commit), refining the component library's event
messages (plan 3), the `Alarm` of `2026-09-22-control-blocks-design.md` (5c) and
the claim diagnostics of `2026-09-30-block-claimed-tags-design.md` (6d).

**Plan sequence:** This is plan 6e. Plans 1–5d, 6a, 6a.1, 6c and 6d are merged
on `master`; this plan starts from `7041258` (the commit that added the 6e
spec). Measured on that commit with `dotnet test Millrace.sln`: **1408 tests**, all
passing — 37 `Millrace.Io.Abstractions` / 491 `Millrace.Core` / 132 `Millrace.Components` / 57
`Millrace.Realtime` / 229 `Millrace.Configuration` / 167 `Millrace.Scenarios` / 78 `Millrace.Cli` /
118 `Millrace.Control` / 23 `Millrace.Control.Catalogue` / 76 `Millrace.Samples`. Release build
`0 Warning(s)`, `0 Error(s)`.

**Task shape.** Three tasks, sequential, each leaving the whole suite green:

- **Task 1 — the `MR016` hint.** No event log moves, so it goes first and
  alone: Core, the plant-file split, and `millrace validate` on the sample.
- **Task 2 — the alarm.** The one piece of real arithmetic (R145), reviewed by
  Opus. It moves two goldens (the alarm lines of `chute-blockage.log` and
  `conveyor-control.log`) and two README lines, and writes the criterion 6
  check script.
- **Task 3 — the component formats.** Moves all eleven event-log goldens (two
  of them again), the remaining README and `docs/control-blocks.md` quotes, adds
  a guard on the control-blocks quote, and runs the criterion 6 check over
  everything that moved since `7041258`.

A task that changes a message regenerates, in the same task, every golden that
message appears in (R152); otherwise the golden tests would fail between tasks.

## Global Constraints

- **`src/` changes in exactly six files.** After Task 3,
  `git diff --stat 7041258 -- src/` lists exactly
  `src/Millrace.Components/Flow/BulkProcessUnit.cs`,
  `src/Millrace.Components/Flow/ItemProcessUnit.cs`,
  `src/Millrace.Components/Mechanical/Motor.cs`,
  `src/Millrace.Components/Mechanical/MotorStarter.cs`,
  `src/Millrace.Control/Alarm.cs` and
  `src/Millrace.Core/SimulationBuilder.cs`. No change under `src/Millrace.Io.Abstractions`,
  `src/Millrace.Configuration`, `src/Millrace.Scenarios`, `src/Millrace.Cli`,
  `src/Millrace.Realtime` or `src/Millrace.Control.Catalogue`; no public API changes.
  `git grep -n PackageReference -- 'src/*.csproj'` prints nothing.
- **Display text only** (spec criterion 5). No computed value, tag value,
  frame, telemetry channel, event time or event order changes. The `WRITE` log
  (`Set to … .`), `FAULT` messages (`thermal-bias injected: amount=0.8.`) and
  every configured value inside a message — alarm limits, the trip level, the
  zero-speed threshold and delay, a sequencer step timeout — print exactly as
  today (criterion 4, R143).
- **Goldens change only in the formatted numbers** (criterion 6). Measured with
  this plan's whole change applied, `MILLRACE_UPDATE_GOLDEN=1 dotnet test Millrace.sln` (in
  a scratch copy — never in the repository) rewrites exactly eleven files: the
  nine `samples/mine-conveyors/expected/*.log`,
  `tests/Millrace.Control.Tests/Golden/conveyor-control.log` and
  `tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log`. The other
  three `Millrace.Scenarios.Tests` goldens, `plant.schema.json`,
  `control-catalogue.json`, the components export and both diagnostics pages do
  not move. Every regenerated log is checked by
  `.superpowers/sdd/6e/golden-shape.sh` (R151).
- **Goldens are generated and read, never invented or hand-edited.** Regenerate
  only with `MILLRACE_UPDATE_GOLDEN=1` and the `--filter` the task names, then read
  the whole `git diff` of each file that moved and quote it in the task report.
  A `MILLRACE_UPDATE_GOLDEN=1` run writes the source files but the tests that read
  a *copy* in `bin/` (`SampleReadmeTests`, the CLI's linked control golden) see
  the old copy until the next build, so every regeneration is followed by a
  normal `dotnet test` (R152).
- **Determinism.** No `System.Random`, no `string.GetHashCode()`, no
  `Dictionary`/`HashSet` iteration order reaching a message: `Suggest.Closest`
  sorts its candidates ordinally before it scans them, so the dictionary key
  order it is handed is irrelevant (R149). All formatting uses
  `CultureInfo.InvariantCulture`; every new format is the standard fixed-point
  `F<n>`.
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)` after every task:
  `dotnet build Millrace.sln -c Release --nologo`.
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`, `Assert.All`;
  never `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`.
- **Test names** are long descriptive PascalCase sentences, like the existing
  ones (`ARaisePrintsOneMoreDecimalThanTheLimitRoundedAwayFromIt`).
- **Messages are verbatim.** Every message in this plan is asserted exactly
  somewhere; copy them byte for byte, including the em dash (`—`, U+2014) in
  the `MR016` hint and the `N·m` middle dot (U+00B7). Core messages read
  "symptom. fix." — the loader splits a `MR016` at the first `". "` after the
  quoted claim (6d R133); the hint adds no `". "` (a tag name holds no
  whitespace, `TagNameRules`).
- **Report every measurement.** Where an expected value in this plan (a test
  count, a message, a golden line, a failure list) disagrees with what the code
  produces, report the measured value in the task report; never adjust an
  assertion to fit without saying so.
- **Markdown files** are edited exactly as this plan shows: LF line endings, no
  tabs, a final newline.
- **Git, for every task.** One git command per `Bash` call. `git add` names
  paths explicitly — never `git add -A`, never `git add .`. **Never `git
  stash`.** Commit messages are conventional (`feat(core): …`, `docs: …`): a
  subject line, a blank line, a body wrapped at about 78 columns, and the trailer
  as the last line, copied verbatim:

  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  ```

  The trailer identifies the **session** that planned and drives this work, not
  the model that implements a given task: it is the same on every commit,
  whichever implementer model a task names. Never put it on the subject line.
  Write each message with the Write tool to `.superpowers/sdd/6e/msg-taskN.txt`
  and commit with `git commit -F`.
- **Commands**, from the repository root: `dotnet build Millrace.sln -c Release --nologo`
  (expect `0 Warning(s)`, `0 Error(s)`) and `dotnet test Millrace.sln --nologo`
  (expect the task's total). `.superpowers/` is git-ignored; scratch work,
  commit messages and the check script go under `.superpowers/sdd/6e/` and are
  never added. Inside a worktree the harness refuses Bash text that mentions git
  inside a heredoc, `$(…)` or a variable, and any complex command containing
  "Github": write files with the Write tool and run the check script as the
  plain command this plan shows.

## Review Focus

The five input classes the spec implies but does not name, most likely to bite
first. Each has its pinning test in the owning task.

1. **A raise a hair past its limit.** A value one ulp above `8.6`
   (`8.600000000000001`) or one ulp below `2` (`1.9999999999999998`) formats to
   the limit itself with `F<n>`; the operator must never read "HiHi: 8.60 above
   8.6". The author expects the next display step away from the limit. Test:
   Task 2, `AlarmTests.ARaisePrintsOneMoreDecimalThanTheLimitRoundedAwayFromIt`
   rows `HiHi 8.6 / 8.600000000000001 → "8.61"` and
   `LoLo 2 / 1.9999999999999998 → "1.9"`.
2. **A value already exact at the display decimals, where scaling by 10ⁿ
   lies.** Measured: `Math.Ceiling(1.1 * 100) / 100` is `1.11` (`1.1 * 100` is
   `110.00000000000001`) and `Math.Floor(4.35 * 100) / 100` is `4.34`
   (`434.99999999999994`). The author expects `1.10` and `4.35`: rounding
   "away" must never move a value the display already shows exactly. Test:
   Task 2, same theory, rows `Hi 0.5 / 1.1 → "1.10"`, `Lo 4.4 / 4.35 → "4.35"`
   and `Hi 7.5 / 7.71 → "7.71"`.
3. **A limit whose shortest form is not plain decimals.** `1e-5`'s round-trip
   form is `1E-05`; `0.1234567` has seven decimals. The author expects six
   decimals for the first (d = 5, from the exponent) and the six-decimal cap for
   the second, with the limit still printed as configured. Test: Task 2, rows
   `Hi 1E-05 / 1.23E-05 → "Hi: 0.000013 above 1E-05."` and
   `Hi 0.1234567 / 0.12345671 → "0.123457"`.
4. **Negative values and a value that rounds to zero from below.** Measured:
   .NET 10 formats `-0.001` as `"-0.00"` with `F2`. The author expects a
   ceiling toward +∞ for a negative Hi (`-2.96` over `-3` → `-2.9`), a floor
   toward −∞ for a negative Lo (`-3.04` under `-3` → `-3.1`), and `0.0`, never
   `-0.0`. Tests: Task 2, rows `Hi -3 / -2.96`, `Lo -3 / -3.04`,
   `Hi -1 / -0.01 → "Hi: 0.0 above -1."`, and
   `AClearPrintsTheValueRoundedToNearest` row `-0.001 → "Hi: 0.00 back within limits."`.
5. **A mistyped claim whose nearest name is ambiguous or is itself a mistake.**
   `V.Enable` is one edit from both `T.Enable` and `U.Enable`, and `T.Enable`
   sorts first; the block commands only `U.Enable`. And a block whose `Writes`
   hold the same typo as its claim (`U.Enabel`, a `MR014` too) must not be told
   that its typo is "closest". Tests: Task 1,
   `ClaimValidationTests.TheBlocksOwnWritesAreSearchedBeforeEveryOtherTag` and
   `AWriteThePlantDoesNotHaveIsNeverTheHint`.

Also pinned, beyond the five: an exact binary midpoint in a clear (`0.125` →
`"0.12"`, `0.375` → `"0.38"`, R147), the breakdown torque `12.5` printed `12`
by `F0` (R147), a clear rounding up to nearest rather than down
(`6.4951` → `"6.50"`), an empty claim getting no hint
(`AClaimNearNoTagKeepsThePlainFix` row `""`), and the control-blocks page's
quoted log lines, now pinned against the worked-example golden
(`DocumentationTests.EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden`).

## Decisions settled here (rulings R143–R154)

These refine the spec where the code, or a measured run, forced a choice.
R154 gives the amendment note that records them in the spec.

- **R143 — The spec's five sites are all the computed doubles (measured
  sweep).** Every event message in `src/` was read: each `ctx.Log` call
  (`Millrace.Components`, `TagImage`), each `outputs.Raise` (`Millrace.Control`) and each
  direct `EventLog.Record` (`Simulation.FaultEvent`). Computed doubles are
  printed only by `Motor` `AT_SPEED` (`_speed`) and `STALLED` (`demand`, and the
  breakdown torque `BreakdownTorqueMultiple * ratedTorque`), `MotorStarter`
  `OVERLOAD_TRIP` (`thermal`), `BulkProcessUnit` `DISCHARGING` (`_elapsed`,
  `_batch.Mass`), `ItemProcessUnit` `DISCHARGING` (`_elapsed`) and `Alarm`
  `ALARM_RAISED`/`ALARM_CLEARED` (`value`) — the spec's list. The others print
  configured values or integers and stay as they are: `ZeroSpeedSwitch`
  `ZERO_SPEED` (`Speed below {ThresholdSpeed} m/s for {DelaySeconds} s.`),
  `MotorStarter`'s `{TripLevel}`, `Alarm`'s `{limit.Value}`, `Sequencer`
  `SEQUENCE_FAULTED` (`timed out after {limit.TotalSeconds} s`, the configured
  timeout), `SEQUENCE_ABORTED`/`STEP_ENTERED`/`SEQUENCE_COMPLETE` (step numbers
  and counts), `ItemProcessUnit`'s `{_items.Count}`, `Simulation.FaultEvent`
  `FAULT` (`{faultId} injected: {arguments}` — the scenario's own arguments),
  `TagImage` `WRITE` (criterion 5). `Timer` raises no event. Every other message
  is constant text or names tags. After the whole change no golden line holds a
  number with four or more decimals (measured: `grep -E '[0-9]\.[0-9]{4,}'` over
  all eleven logs prints nothing).
- **R144 — The alarm's decimals come from the double's round-trip form, with
  its exponent.** *d* is read from `limit.ToString("R", InvariantCulture)`: the
  digits after the point in the mantissa, minus the exponent when the form has
  one, at least 0; the value prints with `min(d + 1, 6)` decimals. Measured
  forms: `7.5`, `8.6` → d 1; `8`, `3`, `100`, `-3` → 0; `0.125` → 3;
  `0.1234567` → 7 (capped to 6 decimals); `1e-5` → `"1E-05"` → 5 (6 decimals);
  `1e-7` → `"1E-07"` → 7 (capped); `1e16` → `"10000000000000000"` → 0 and
  `1e17` → `"1E+17"` → 0 (the exponent form begins at `1e17`). *d* comes from the
  double, not the plant file's text: `"value": 3.0` and `"value": 3` both give
  one decimal. The limits used in the repository: the sample's `7.5`/`8.6`,
  `5.7`/`6.6`, `4.3`/`4.9` (two decimals); the worked example's `3`/`8` (one);
  the unit tests' `80`, `90`, `20`, `10` (one) — so every existing `AlarmTests`
  message (`Hi: 82.3 above 80.`, `Lo: 18.5 below 20.`, `LoLo: 8.5 below 10.`,
  `Hi: 71.5 back within limits.`, `LoLo: 12.5 back within limits.`) is
  unchanged, and they stay as regression rows. `docs/control-blocks.md`'s
  example (`Hi: 82.5 above 80.`, `Hi: 71.5 back within limits.`) already obeys
  the rule and does not change.
- **R145 — Directed rounding: nearest `F<n>` text, then at most one step.**
  Spec criterion 3 says "ceiling at that many decimals". Scaling by 10ⁿ is
  wrong in binary floating point (Review Focus 2: measured `1.1 → 1.11` and
  `4.35 → 4.34`), and so is `decimal` conversion of the double (it keeps 15
  significant digits and can pull a value one ulp above the limit back onto
  it). Chosen: format the value with `F<n>` (the runtime's exact, correctly
  rounded decimal expansion), parse that text back to a double, and only when
  the parsed double lies on the wrong side of the value — below it for a Hi
  raise, above it for a Lo raise — add or subtract one unit in the last place
  (`new decimal(1, 0, 0, negative, n)`) in `decimal` arithmetic. A value whose
  `F<n>` text parses back to the same double is shown as that text and never
  stepped (so `1.1` over 0.5 prints `1.10`, though its binary value is slightly
  above 1.1; `7.71` stays `7.71`, `82.3` stays `82.3`). At magnitudes where the
  spacing of doubles reaches the display step, the display is simply the
  nearest `F<n>` text, which is still beyond the limit (below). **Why this never shows a raised
  value on or inside its limit:** the value `v` exceeds the limit double `L`;
  the displayed text either round-trips to `v` (then it is above `L`'s decimal
  form, since rounding a decimal to the nearest double is monotonic) or it was
  stepped past `v`; either way it is strictly beyond the limit as a decimal. A
  step only happens when the text does not round-trip, which needs an ulp
  below 10⁻ⁿ, so |v| < 10¹⁵ and `decimal` cannot overflow; `±Infinity` formats
  as `"Infinity"`/`"-Infinity"` and round-trips, so it is never stepped; `NaN`
  never raises or clears (every comparison with it is false). A raise can
  therefore overstate by up to one display step — a Hi of 10.0008 over 10
  prints `10.1`; measured in the worked example, `10.000765680139894` over
  HiHi 8 prints `10.1` — which is the spec's "away from the limit" choice.
- **R146 — Negative zero prints as zero, in both alarm messages.** Measured:
  `(-0.001).ToString("F2", InvariantCulture)` is `"-0.00"` and a ceiling of
  `-0.01` at one decimal is `-0.0`. The helper strips the sign from a text that
  is all zeros after it, so a raise prints `Hi: 0.0 above -1.` and a clear
  `Hi: 0.00 back within limits.`. This refines criterion 3's "`ALARM_CLEARED`
  formats the value with `F<n>` directly". The component formats are not
  touched (a speed, torque, thermal state, time or mass that rounds to a
  negative zero is not reachable from the models: each is clamped at zero or
  counts up).
- **R147 — `F<n>` rounds an exact binary midpoint half to even (measured,
  accepted).** On .NET 10.0.12, `0.125` → `"0.12"`, `0.375` → `"0.38"`,
  `2.5` → `"2"` (F0), `3.5` → `"4"`, `12.5` → `"12"`; non-midpoints round by
  their exact binary value (`2.675`, binary `2.67499…`, → `"2.67"`;
  `1.005` → `"1.00"`). It is deterministic and display-only; the spec's
  "formats with `F<n>`" is kept. Pinned: `AClearPrintsTheValueRoundedToNearest`
  rows `0.125` and `0.375`, and the motor's `STALLED` test, whose 5 N·m test
  motor's breakdown torque `2.5 × 5 = 12.5` prints `12`.
- **R148 — Only the alarm rounds away from a configured value.** The
  component formats may display a measured value equal to the configured value
  next to it — the spec's own example `Thermal state 1.100 reached the trip
  level 1.1.` (the chute-blockage golden), or `Torque demand 13 N·m exceeds
  breakdown torque 12 N·m.` for 13 over 12.5. That is the spec's decision
  (criterion 1's table and "Shared helper? No"); it is recorded here so that a
  reviewer does not "fix" it.
- **R149 — The `MR016` hint: candidates, order and placement.** Measured:
  `Suggest.Closest(given, candidates)` compares case-insensitively
  (`ToUpperInvariant`) by optimal-string-alignment distance, accepts a
  candidate within `max(2, given.Length / 3)`, and scans candidates **after
  sorting them ordinally**, keeping the first strictly nearer — so the order of
  the list it is handed never matters and ties go to the ordinally first name.
  "First among the block's `Writes`, then among all plant tags" is therefore two
  calls, the second only when the first returns `null`. The first call's
  candidates are the block's `Writes` **that the plant has** (`byName`
  contains them): a `Write` the plant does not have is already a `MR014`, and
  offered as a candidate it would be the "closest" name to an identical typo in
  the claim (Review Focus 5; measured without the filter: `'U.Enabel' is
  closest`). "All plant tags" is every name in `CheckClaims`' `byName` — the
  directory's tags including block-owned outputs and commands. The fix becomes
  `Check the name against 'millrace tags' — '<closest>' is closest; a block claims a tag it commands.`;
  with no candidate near enough it is exactly as today. An empty claim gets no
  hint (distance to every tag exceeds 2). It is built by a `static` local
  function `NearestFix` beside the existing `Claim` local function in
  `CheckClaims`. `BuildStage.Split` is unchanged: it cuts at the first `". "`
  after the quoted claim, which is still the end of the symptom, since the hint
  adds only an em dash, quotes and a semicolon and a tag name holds no
  whitespace (measured through `millrace validate`, Task 1).
- **R150 — Existing tests that change, and where the plant-file tests live.**
  Measured by grep over `tests/` for every changed message and for
  `does not have`: two existing tests change, both because their near-miss
  claim now earns a hint — `ClaimValidationTests.AClaimNamingNoTagIsMr016`
  (`U.Enabel` → `'U.Enable' is closest`) and
  `ClaimTests.TheMessageIsSplitIntoSymptomAndFix` (`FEED.Permt` and
  `FEED. Permit` → `'FEED.Permit' is closest`). Tests that compare only the
  symptom half (`AClaimIsMatchedOrdinallyAndExactly`,
  `ARepeatedClaimOnAMissingTagIsReportedOnceAtEachPosition`,
  `AClaimCoreRefusesIsMr016AtTheClaim`) are unaffected. No existing test
  asserts a changed component or alarm message except through substrings that
  still match (`Stories.cs` `"above 7.5."`/`"above 8.6."`,
  `MotorStarterTests` `Contains("1.1")`, `WorkedExampleTests`
  `StartsWith("HiHi:")`) or through the goldens. The spec's "through a plant
  file, on the `Fix:` line of `millrace validate`" test names the sample's
  `cv003.permit`, so it lives in `Millrace.Samples.Tests` (which runs the CLI
  in-process and holds the sample), not `Millrace.Cli.Tests`.
- **R151 — Criterion 6's check is a script, not a test.** The check compares
  each regenerated log with its *previous* version, which exists only in git
  history; a test would need the old text committed as fixtures that are dead
  weight once this plan lands. The goldens themselves, the README and
  control-blocks quote guards stay the durable checks. The script,
  `.superpowers/sdd/6e/golden-shape.sh` (git-ignored; written in Task 2, run in
  Tasks 2 and 3), takes a base commit, lists every `*.log` that differs from it,
  and for each checks: the same number of lines; on every line the same time,
  source and event (the first three fields, split at two spaces); and message
  texts equal once every number (`-?[0-9]+(\.[0-9]+)?`) is replaced by `#`. It
  reports every file that fails and, having checked them all, exits 1 if any
  failed. Base `7041258`: the plan commit
  on top of it adds only Markdown, so every golden is identical at both.
  Measured against a deliberately broken copy (a changed word, a moved time, an
  extra line) it reports all three and exits 1.
- **R152 — Task order and golden regeneration.** Each task regenerates, in the
  same task, every golden its message change reaches, so the suite is green at
  every commit. Measured: the alarm change alone moves exactly
  `chute-blockage.log` (4 lines) and `conveyor-control.log` (4 lines); the
  component change then moves all eleven logs (the same two again for their
  `AT_SPEED`/`OVERLOAD_TRIP` lines). Regeneration uses three filters, never a
  whole-solution update run in the repository:
  `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.WorkedExampleTests"`,
  `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"` and
  `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Scenarios.Tests --nologo --filter "FullyQualifiedName~EveryValidScenarioRunsCleanAndMatchesItsGolden"`.
  Measured: during an update run `SampleReadmeTests` still reads the stale
  `bin/` copy of each golden and passes; only the following normal
  `dotnet test` exposes a stale README quote.
  A red run of a `Golden.Assert` test also leaves a git-ignored `.actual`
  file beside the golden (`tests/Shared/Golden.cs`); Task 2's deliberate red
  run in Step 5 leaves `tests/Millrace.Control.Tests/Golden/conveyor-control.log.actual`
  with the old full-precision lines, so Task 2 Step 7 deletes it, and Task 3's
  decimal grep is restricted to `*.log`.
- **R153 — Documentation scope.** Measured by grep over `README.md`,
  `samples/`, and `docs/` excluding `docs/superpowers/`: the changed lines are
  quoted in five places — `samples/mine-conveyors/README.md` lines 213
  (`overload.log`, `OVERLOAD_TRIP`), 244–245 (`chute-blockage.log`, the two
  `ALARM_RAISED`) and 246 (`chute-blockage.log`, `OVERLOAD_TRIP`), and
  `docs/control-blocks.md` line 220 (the worked example's `OVERLOAD_TRIP`). No
  quoted timestamp changes. The README's quotes are pinned by
  `SampleReadmeTests.EveryQuotedLineIsAWholeLineOfItsGolden`; the control-blocks
  quote was pinned by nothing, so Task 3 adds
  `DocumentationTests.EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden`.
  Unchanged: `docs/control-blocks.md`'s `Alarm` event examples (R144),
  `docs/configuration-diagnostics.md` (its `MR016` summary still holds; no doc
  quotes the fix text), the root `README.md` (no quoted event line), and the
  historical plans and specs under `docs/superpowers/` that quote old numbers
  (`2026-09-25-interlock-start-inhibit-design.md`, its plan, and this plan's
  own spec) — they record what was true then.
- **R154 — The spec amendment.** The controller adds this note to the spec,
  under its title, with the plan commit (as 6c and 6d did; no task edits the
  spec):

  ```markdown
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
  ```

## Measurements

Scratch runs on `7041258` plus this plan's whole change, in a throwaway `git
worktree` (removed after).

**Suite:** 1442 tests, all passing; Release build `0 Warning(s)`, `0 Error(s)`.
Per project: 37 Io.Abstractions / 498 Core / 137 Components / 57 Realtime /
230 Configuration / 167 Scenarios / 78 Cli / 137 Control / 23 Control.Catalogue /
78 Samples.

**Every golden line that moves** (before → after; the time, source and event
of every line are unchanged):

In each of the nine sample goldens (`chute-blockage`, `e-stop`,
`failed-zero-speed`, `feed-starve`, `normal-start-stop`, `overload`,
`pull-key`, `start-while-tripped`, `welded-contactor`), lines 56, 68 and 77:

```
-06:00:05.400  CV003.Motor  AT_SPEED  Reached 146.17986855114356 rad/s.
+06:00:05.400  CV003.Motor  AT_SPEED  Reached 146.2 rad/s.
-06:00:08.000  CV002.Motor  AT_SPEED  Reached 146.1547694598214 rad/s.
+06:00:08.000  CV002.Motor  AT_SPEED  Reached 146.2 rad/s.
-06:00:10.590  CV001.Motor  AT_SPEED  Reached 145.98559884017868 rad/s.
+06:00:10.590  CV001.Motor  AT_SPEED  Reached 146.0 rad/s.
```

`samples/mine-conveyors/expected/chute-blockage.log`, lines 81–83 and 88–89
(lines 81, 82, 88, 89 move in Task 2; line 83 and the three above in Task 3):

```
-06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705084760820622 above 7.5.
-06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806537054771315 above 8.6.
-06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000357303409216 reached the trip level 1.1.
+06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.71 above 7.5.
+06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.81 above 8.6.
+06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.100 reached the trip level 1.1.
-06:03:09.800  ALM_CV001  ALARM_CLEARED  Hi: 0 back within limits.
-06:03:09.800  ALM_CV001  ALARM_CLEARED  HiHi: 0 back within limits.
+06:03:09.800  ALM_CV001  ALARM_CLEARED  Hi: 0.00 back within limits.
+06:03:09.800  ALM_CV001  ALARM_CLEARED  HiHi: 0.00 back within limits.
```

`samples/mine-conveyors/expected/overload.log`, line 80:

```
-06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.285658707267615 reached the trip level 1.1.
+06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.286 reached the trip level 1.1.
```

`samples/mine-conveyors/expected/start-while-tripped.log`, lines 117 and 127:

```
-06:01:52.990  CV002.Motor  AT_SPEED  Reached 144.75486557608986 rad/s.
+06:01:52.990  CV002.Motor  AT_SPEED  Reached 144.8 rad/s.
-06:02:07.990  CV001.Motor  AT_SPEED  Reached 144.5334902393653 rad/s.
+06:02:07.990  CV001.Motor  AT_SPEED  Reached 144.5 rad/s.
```

`tests/Millrace.Control.Tests/Golden/conveyor-control.log`, lines 21–23 and 26
(Task 2), 27 and 29 (Task 3):

```
-06:00:03.400  CUR01  ALARM_RAISED  HiHi: 10.000765680139894 above 8.
-06:00:03.700  CUR01  ALARM_CLEARED  HiHi: 7.40937291733113 back within limits.
-06:00:03.800  CUR01  ALARM_RAISED  Hi: 6.701381615037194 above 3.
+06:00:03.400  CUR01  ALARM_RAISED  HiHi: 10.1 above 8.
+06:00:03.700  CUR01  ALARM_CLEARED  HiHi: 7.4 back within limits.
+06:00:03.800  CUR01  ALARM_RAISED  Hi: 6.8 above 3.
-06:00:04.700  CUR01  ALARM_CLEARED  Hi: 2.707919679974949 back within limits.
-06:00:06.190  CV001.Motor  AT_SPEED  Reached 140.02609575456847 rad/s.
+06:00:04.700  CUR01  ALARM_CLEARED  Hi: 2.7 back within limits.
+06:00:06.190  CV001.Motor  AT_SPEED  Reached 140.0 rad/s.
-06:00:40.000  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1796472378913028 reached the trip level 1.1.
+06:00:40.000  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.180 reached the trip level 1.1.
```

`tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log`, lines 9 and 11:

```
-06:00:07.970  CV001.Motor  AT_SPEED  Reached 139.81136237898272 rad/s.
+06:00:07.970  CV001.Motor  AT_SPEED  Reached 139.8 rad/s.
-06:00:30.000  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1558222267049405 reached the trip level 1.1.
+06:00:30.000  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.156 reached the trip level 1.1.
```

No golden holds a `STALLED` or `DISCHARGING` (hold satisfied) line; those two
formats are pinned by unit tests only. The other three `Millrace.Scenarios.Tests`
goldens (`instrumented-belt-drift`, `item-line-blinded-counter`,
`minimal-feed-throttled`) do not move.

**Criterion 6, the check** (`bash .superpowers/sdd/6e/golden-shape.sh 7041258`
after Task 3; exit 0):

```
OK  samples/mine-conveyors/expected/chute-blockage.log  95 lines, 8 changed
OK  samples/mine-conveyors/expected/e-stop.log  92 lines, 3 changed
OK  samples/mine-conveyors/expected/failed-zero-speed.log  90 lines, 3 changed
OK  samples/mine-conveyors/expected/feed-starve.log  79 lines, 3 changed
OK  samples/mine-conveyors/expected/normal-start-stop.log  112 lines, 3 changed
OK  samples/mine-conveyors/expected/overload.log  104 lines, 4 changed
OK  samples/mine-conveyors/expected/pull-key.log  99 lines, 3 changed
OK  samples/mine-conveyors/expected/start-while-tripped.log  132 lines, 5 changed
OK  samples/mine-conveyors/expected/welded-contactor.log  117 lines, 3 changed
OK  tests/Millrace.Control.Tests/Golden/conveyor-control.log  40 lines, 6 changed
OK  tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log  16 lines, 2 changed
```

**`millrace validate` on the sample with `INT_CV003`'s claim mistyped** (stderr,
exit 1; the plant file saved as `near.json`, `far.json`, `typo.json`):

```
MR016 $.controllers[3].claims[0]
  Block 'INT_CV003' claims tag 'cv003.permit', which the plant does not have.
  Fix: Check the name against 'millrace tags' — 'CV003.Permit' is closest; a block claims a tag it commands.

1 error in near.json
```

```
MR016 $.controllers[3].claims[0]
  Block 'INT_CV003' claims tag 'Conveyor3.RunPermit', which the plant does not have.
  Fix: Check the name against 'millrace tags'; a block claims a tag it commands.

1 error in far.json
```

`CV003.Permt` gives the same fix as `cv003.permit`. The split holds: the
symptom ends at the first `". "` after `'cv003.permit'`, and the fix is the
whole hint.

**Component messages, measured on the unit-test rigs** (before → after):
`Reached 142.93480695413064 rad/s.` → `Reached 142.9 rad/s.`;
`Torque demand 13 N·m exceeds breakdown torque 12.5 N·m.` →
`Torque demand 13 N·m exceeds breakdown torque 12 N·m.`;
`Hold satisfied after 1 s; discharging 9 kg of Dough.` →
`Hold satisfied after 1.00 s; discharging 9.0 kg of Dough.`;
`Hold satisfied after 2 s; discharging 3 items.` →
`Hold satisfied after 2.00 s; discharging 3 items.`

## File structure

```
src/Millrace.Core/SimulationBuilder.cs                    using Millrace.Core.Catalogue; MR016 "does not have" fix via NearestFix (Task 1)
tests/Millrace.Core.Tests/ClaimValidationTests.cs         1 test changed; + 2 facts, + theory of 3, + theory of 2 (Task 1)
tests/Millrace.Configuration.Tests/ClaimTests.cs          1 test changed; + 1 fact (Task 1)
tests/Millrace.Samples.Tests/MineConveyorTests.cs         + theory of 2 (Task 1)
src/Millrace.Control/Alarm.cs                             both messages through Display(value, limit, direction) (Task 2)
tests/Millrace.Control.Tests/AlarmTests.cs                + theory of 14, + theory of 4 (Task 2)
tests/Millrace.Control.Tests/Golden/conveyor-control.log  regenerated (Tasks 2 and 3)
samples/mine-conveyors/expected/chute-blockage.log   regenerated (Tasks 2 and 3)
samples/mine-conveyors/README.md                     two quoted lines (Task 2), two more (Task 3)
src/Millrace.Components/Mechanical/Motor.cs               AT_SPEED F1, STALLED F0 (Task 3)
src/Millrace.Components/Mechanical/MotorStarter.cs        OVERLOAD_TRIP F3 (Task 3)
src/Millrace.Components/Flow/BulkProcessUnit.cs           hold satisfied F2 / F1 (Task 3)
src/Millrace.Components/Flow/ItemProcessUnit.cs           hold satisfied F2 (Task 3)
tests/Millrace.Components.Tests/MotorTests.cs             + 1 fact (Task 3)
tests/Millrace.Components.Tests/MotorStarterTests.cs      + theory of 2 (Task 3)
tests/Millrace.Components.Tests/BulkProcessUnitTests.cs   + 1 fact (Task 3)
tests/Millrace.Components.Tests/ItemProcessUnitTests.cs   + 1 fact (Task 3)
samples/mine-conveyors/expected/*.log                the other eight regenerated (Task 3)
tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log   regenerated (Task 3)
docs/control-blocks.md                               the worked example's OVERLOAD_TRIP line (Task 3)
tests/Millrace.Control.Tests/DocumentationTests.cs        partial; + 1 fact guarding the page's quoted log lines (Task 3)
.superpowers/sdd/6e/golden-shape.sh                  the criterion 6 check (Task 2; git-ignored, never added)
```

## Task map

| # | Task | Implementer | Reviewer | Tests after |
|---|---|---|---|---|
| 1 | `MR016` nearest-name hint (Core), through the loader and `millrace validate` | sonnet | sonnet | 1418 |
| 2 | `Alarm` display decimals and directed rounding; two goldens, two README lines; the criterion 6 script | sonnet | opus (rounding) | 1436 |
| 3 | Component formats; all eleven goldens; README and control-blocks quotes and their guard | sonnet | sonnet | 1442 |

Every task's brief contains its complete code; Sonnet implements throughout
because every task edits existing files and the commit trailer must be right
first time. The whole-branch review at the end is Opus. Tasks are sequential:
Task 3's golden regeneration and check script build on Task 2's.

---

### Task 1: The `MR016` "does not have" diagnostic names the nearest tag

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `src/Millrace.Core/SimulationBuilder.cs` (a `using`; the "does not have" branch of `CheckClaims`; a new local function)
- Test: `tests/Millrace.Core.Tests/ClaimValidationTests.cs` (1 changed, 7 added)
- Test: `tests/Millrace.Configuration.Tests/ClaimTests.cs` (1 changed, 1 added)
- Test: `tests/Millrace.Samples.Tests/MineConveyorTests.cs` (2 added)

**Interfaces:**
- Consumes: `Millrace.Core.Catalogue.Suggest.Closest(string given, IEnumerable<string> candidates)`
  (public, unchanged); `IScanBlock.Writes` (`IReadOnlyList<TagRef>`);
  `CheckClaims`' `byName` (`Dictionary<string, TagBinding>`, every tag in the
  directory, ordinal).
- Produces: in `CheckClaims`, `static string NearestFix(IScanBlock block, string claim, Dictionary<string, TagBinding> byName)`
  (a local function); the `MR016` message
  `Block '<id>' claims tag '<claim>', which the plant does not have. Check the name against 'millrace tags' — '<closest>' is closest; a block claims a tag it commands.`
  when a name is near enough, else exactly the 6d text
  `Block '<id>' claims tag '<claim>', which the plant does not have. Check the name against 'millrace tags'; a block claims a tag it commands.`
  No other `MR016` message changes.

- [ ] **Step 1: Write the failing Core tests**

In `tests/Millrace.Core.Tests/ClaimValidationTests.cs`, in
`AClaimNamingNoTagIsMr016`, replace

```csharp
            "Block 'A' claims tag 'U.Enabel', which the plant does not have. Check the name against 'millrace tags'; a block claims " +
            "a tag it commands.",
```

with

```csharp
            "Block 'A' claims tag 'U.Enabel', which the plant does not have. Check the name against 'millrace tags' — 'U.Enable' is " +
            "closest; a block claims a tag it commands.",
```

Then insert, immediately before

```csharp
    [Fact]
    public void AClaimOnAReadOnlyTagIsMr016()
```

the following (the plant's tags are `A.Cmd`, `T.Enable`, `T.Output`,
`T.Setpoint`, `U.Enable`, `U.Output`, `U.Setpoint`; block `A` commands only
`U.Enable`):

```csharp
    [Theory]
    [InlineData("u.enable", "U.Enable")]
    [InlineData(" U.Enable", "U.Enable")]
    [InlineData("U.Setpont", "U.Setpoint")]
    public void AClaimNearATagIsHintedWithTheNearestName(string claim, string closest)
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), [claim]));

        Assert.Equal(
            $"Block 'A' claims tag '{claim}', which the plant does not have. Check the name against 'millrace tags' — '{closest}' is " +
            "closest; a block claims a tag it commands.",
            error.Message);
    }

    [Theory]
    [InlineData("Heater9.RunPermit")]
    [InlineData("")]
    public void AClaimNearNoTagKeepsThePlainFix(string claim)
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), [claim]));

        Assert.Equal(
            $"Block 'A' claims tag '{claim}', which the plant does not have. Check the name against 'millrace tags'; a block claims " +
            "a tag it commands.",
            error.Message);
    }

    [Fact]
    public void TheBlocksOwnWritesAreSearchedBeforeEveryOtherTag()
    {
        // 'T.Enable' is as near to 'V.Enable' as 'U.Enable' and sorts first; the block commands only U.Enable.
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), ["V.Enable"]));

        Assert.Contains("— 'U.Enable' is closest;", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWriteThePlantDoesNotHaveIsNeverTheHint()
    {
        ValidationResult result = Plant().AddScanBlock(Block("A").MayWrite("U.Enabel"), ["U.Enabel"]).Validate();

        ValidationError claim = Assert.Single(result.Errors, e => e.Code == "MR016");
        Assert.Contains("— 'U.Enable' is closest;", claim.Message, StringComparison.Ordinal);
        Assert.Single(result.Errors, e => e.Code == "MR014");
    }

```

(`U.Setpont` is nearer no `Write` of the block — `U.Enable` is 5 edits away,
over the threshold of 3 — so the hint comes from the second search.)

- [ ] **Step 2: Write the failing plant-file tests**

In `tests/Millrace.Configuration.Tests/ClaimTests.cs`, replace

```csharp
        Assert.Equal("Check the name against 'millrace tags'; a block claims a tag it commands.", d.Fix);
        Assert.Equal(d.Fix, spaced.Fix);
    }
```

with

```csharp
        Assert.Equal("Check the name against 'millrace tags' — 'FEED.Permit' is closest; a block claims a tag it commands.", d.Fix);
        Assert.Equal(d.Fix, spaced.Fix);
        Assert.Equal("Block 'INT01' claims tag 'FEED. Permit', which the plant does not have.", spaced.Message);
    }

    [Fact]
    public void AClaimNearNoTagKeepsThePlainFix()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, """[ "Silo9.RunPermit" ]""")));

        Assert.Equal(("MR016", "$.controllers[0].claims[0]"), (d.Code, d.Path));
        Assert.Equal("Block 'INT01' claims tag 'Silo9.RunPermit', which the plant does not have.", d.Message);
        Assert.Equal("Check the name against 'millrace tags'; a block claims a tag it commands.", d.Fix);
    }
```

In `tests/Millrace.Samples.Tests/MineConveyorTests.cs`, insert immediately before

```csharp
    [Fact]
    public void ATraceCannotSampleFasterThanTheTimeStep()
```

the following:

```csharp
    [Theory]
    [InlineData("cv003.permit", "Check the name against 'millrace tags' — 'CV003.Permit' is closest; a block claims a tag it commands.")]
    [InlineData("Conveyor3.RunPermit", "Check the name against 'millrace tags'; a block claims a tag it commands.")]
    public void MillraceValidateNamesTheNearestTagOnTheFixLineOfAMistypedClaim(string claim, string fix)
    {
        string plant = File.ReadAllText(Sample.Plant);
        string mistyped = plant.Replace("\"claims\": [ \"CV003.Permit\" ]", $"\"claims\": [ \"{claim}\" ]", StringComparison.Ordinal);
        Assert.NotEqual(plant, mistyped);
        string path = Path.Combine(Path.GetTempPath(), $"millrace-sample-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, mistyped);
        try
        {
            CliRun run = Cli.Run("validate", path);

            Assert.Equal(ExitCodes.PlantInvalid, run.ExitCode);
            Assert.Empty(run.Out);
            Assert.StartsWith(
                $"MR016 $.controllers[3].claims[0]\n  Block 'INT_CV003' claims tag '{claim}', which the plant does not have.\n  Fix: {fix}\n",
                run.Err,
                StringComparison.Ordinal);
            Assert.EndsWith($"1 error in {Path.GetFileName(path)}\n", run.Err, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

```

(`INT_CV003` is the fourth controller in the sample, `$.controllers[3]`;
measured.)

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter "FullyQualifiedName~Millrace.Core.Tests.ClaimValidationTests"`
Expected: FAIL — 6 failed, 23 passed, 29 total: `AClaimNamingNoTagIsMr016`,
the three `AClaimNearATagIsHintedWithTheNearestName` rows,
`TheBlocksOwnWritesAreSearchedBeforeEveryOtherTag` and
`AWriteThePlantDoesNotHaveIsNeverTheHint` (each message lacks the hint). Both
`AClaimNearNoTagKeepsThePlainFix` rows pass already.

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~Millrace.Configuration.Tests.ClaimTests"`
Expected: FAIL — 1 failed (`TheMessageIsSplitIntoSymptomAndFix`), 13 passed.

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~Millrace.Samples.Tests.MineConveyorTests.MillraceValidate"`
Expected: FAIL — 1 failed (the `cv003.permit` row), 1 passed.

- [ ] **Step 4: Build the hint**

In `src/Millrace.Core/SimulationBuilder.cs`, replace

```csharp
using System.Globalization;
using Millrace.Core.Control;
```

with

```csharp
using System.Globalization;
using Millrace.Core.Catalogue;
using Millrace.Core.Control;
```

In `CheckClaims`, replace

```csharp
                else if (!byName.TryGetValue(claim, out TagBinding? binding))
                {
                    error = Claim(
                        block.Id, claim, j, "which the plant does not have. Check the name against 'millrace tags'; a block claims a tag it commands.");
                }
```

with

```csharp
                else if (!byName.TryGetValue(claim, out TagBinding? binding))
                {
                    error = Claim(block.Id, claim, j, $"which the plant does not have. {NearestFix(block, claim, byName)}");
                }
```

and replace

```csharp
        return claimants;

        static ValidationError Claim(string blockId, string claim, int index, string rest) =>
```

with

```csharp
        return claimants;

        // The nearest name among the block's own writes the plant has, then among every tag (spec 6e criterion 7);
        // Suggest.Closest sorts its candidates, so the order handed to it never matters.
        static string NearestFix(IScanBlock block, string claim, Dictionary<string, TagBinding> byName)
        {
            string? closest = Suggest.Closest(claim, block.Writes.Select(w => w.Name).Where(byName.ContainsKey))
                ?? Suggest.Closest(claim, byName.Keys);
            return closest is null
                ? "Check the name against 'millrace tags'; a block claims a tag it commands."
                : $"Check the name against 'millrace tags' — '{closest}' is closest; a block claims a tag it commands.";
        }

        static ValidationError Claim(string blockId, string claim, int index, string rest) =>
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter "FullyQualifiedName~Millrace.Core.Tests.ClaimValidationTests"` — expect PASS, 29.
Run: `dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~Millrace.Configuration.Tests.ClaimTests"` — expect PASS, 14.
Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~Millrace.Samples.Tests.MineConveyorTests.MillraceValidate"` — expect PASS, 2.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1418**: 37 / 498 / 132 / 57 /
230 / 167 / 78 / 118 / 23 / 78.
Run: `git status --short` — expect exactly the four files of this task.

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/SimulationBuilder.cs tests/Millrace.Core.Tests/ClaimValidationTests.cs tests/Millrace.Configuration.Tests/ClaimTests.cs tests/Millrace.Samples.Tests/MineConveyorTests.cs
git commit -F .superpowers/sdd/6e/msg-task1.txt
```

with `.superpowers/sdd/6e/msg-task1.txt` (written with the Write tool) holding:

```
feat(core): hint the nearest tag when a claim names none

The MR016 raised for a claim that names no tag now suggests the nearest
name, as other unknown-name diagnostics do: Suggest.Closest over the
claiming block's writes the plant has, then over every tag, so
"cv003.permit" reads "Check the name against 'millrace tags' — 'CV003.Permit'
is closest; a block claims a tag it commands." A claim near no tag keeps
the plain fix. Matching stays ordinal and exact; the hint only suggests,
and the loader's split after the quoted claim is unchanged.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'` and check the subject, the blank
line and the trailer.

---

### Task 2: The alarm prints its value to one more decimal than its limit

**Model:** implementer sonnet; reviewer **opus** (the directed rounding, R145).

**Files:**
- Modify: `src/Millrace.Control/Alarm.cs` (both messages; new private static `Display`)
- Test: `tests/Millrace.Control.Tests/AlarmTests.cs` (+ theory of 14, + theory of 4)
- Regenerate: `tests/Millrace.Control.Tests/Golden/conveyor-control.log`, `samples/mine-conveyors/expected/chute-blockage.log`
- Modify: `samples/mine-conveyors/README.md` (two quoted lines)
- Create (git-ignored, never added): `.superpowers/sdd/6e/golden-shape.sh`

**Interfaces:**
- Consumes: `AlarmLimit.Value`, `AlarmLimitKind` (unchanged); the test
  harness `Scan` (`tests/Millrace.Control.Tests/Scan.cs`) and `AlarmTests`' private
  `Make`/`Limit` helpers.
- Produces: `private static string Display(double value, double limit, int direction)`
  on `Millrace.Control.Alarm` — `direction` +1 rounds up (Hi, HiHi raise), −1 down
  (Lo, LoLo raise), 0 to nearest (clear); decimals `min(d + 1, 6)` with *d* from
  the limit's `"R"` form including its exponent (R144); a negative zero prints
  without its sign (R146). `ALARM_RAISED` reads
  `<Kind>: <Display(value, limit, ±1)> above|below <limit as today>.` and
  `ALARM_CLEARED` reads `<Kind>: <Display(value, limit, 0)> back within limits.`

- [ ] **Step 1: Write the failing tests**

In `tests/Millrace.Control.Tests/AlarmTests.cs`, replace the end of the class

```csharp
        Assert.Equal("ALARM_RAISED,ALARM_ACKED,ALARM_CLEARED", scan.Codes());
    }
}
```

with

```csharp
        Assert.Equal("ALARM_RAISED,ALARM_ACKED,ALARM_CLEARED", scan.Codes());
    }

    [Theory]
    [InlineData(AlarmLimitKind.Hi, 7.5, 7.5004, "Hi: 7.51 above 7.5.")]
    [InlineData(AlarmLimitKind.HiHi, 8.6, 8.600000000000001, "HiHi: 8.61 above 8.6.")]
    [InlineData(AlarmLimitKind.Lo, 2.0, 1.9996, "Lo: 1.9 below 2.")]
    [InlineData(AlarmLimitKind.LoLo, 2.0, 1.9999999999999998, "LoLo: 1.9 below 2.")]
    [InlineData(AlarmLimitKind.Hi, 7.5, 7.71, "Hi: 7.71 above 7.5.")]
    [InlineData(AlarmLimitKind.Hi, 0.5, 1.1, "Hi: 1.10 above 0.5.")]
    [InlineData(AlarmLimitKind.Lo, 4.4, 4.35, "Lo: 4.35 below 4.4.")]
    [InlineData(AlarmLimitKind.Hi, 100.0, 100.05, "Hi: 100.1 above 100.")]
    [InlineData(AlarmLimitKind.Hi, 0.125, 0.12500001, "Hi: 0.1251 above 0.125.")]
    [InlineData(AlarmLimitKind.Hi, 0.1234567, 0.12345671, "Hi: 0.123457 above 0.1234567.")]
    [InlineData(AlarmLimitKind.Hi, 1e-5, 1.23e-5, "Hi: 0.000013 above 1E-05.")]
    [InlineData(AlarmLimitKind.Hi, -3.0, -2.96, "Hi: -2.9 above -3.")]
    [InlineData(AlarmLimitKind.Lo, -3.0, -3.04, "Lo: -3.1 below -3.")]
    [InlineData(AlarmLimitKind.Hi, -1.0, -0.01, "Hi: 0.0 above -1.")]
    public void ARaisePrintsOneMoreDecimalThanTheLimitRoundedAwayFromIt(AlarmLimitKind kind, double limit, double value, string message)
    {
        var scan = new Scan(Make(Limit(kind, limit)));

        scan.Set("CV001.Current", value).Once();

        BlockEvent raised = Assert.Single(scan.LastEvents);
        Assert.Equal(("ALARM_RAISED", message), (raised.Code, raised.Message));
    }

    [Theory]
    [InlineData(7.5, 1.0, 6.4951, "Hi: 6.50 back within limits.")]
    [InlineData(1.5, 1.0, 0.125, "Hi: 0.12 back within limits.")]
    [InlineData(1.5, 1.0, 0.375, "Hi: 0.38 back within limits.")]
    [InlineData(0.5, 0.5, -0.001, "Hi: 0.00 back within limits.")]
    public void AClearPrintsTheValueRoundedToNearest(double limit, double deadband, double value, string message)
    {
        var scan = new Scan(Make(Limit(AlarmLimitKind.Hi, limit, deadband)));
        scan.Set("CV001.Current", limit + 1.0).Once();

        scan.Set("CV001.Current", value).Once();

        BlockEvent cleared = Assert.Single(scan.LastEvents);
        Assert.Equal(("ALARM_CLEARED", message), (cleared.Code, cleared.Message));
    }
}
```

What each row pins: `7.5004` and the next double above `8.6` round up to the
next display step, `1.9996` and the next double below `2` round down (Review
Focus 1); `7.71`, `1.1` over `0.5` and `4.35` under `4.4` print as their
`F<n>` text (it parses back to the same double) and must not be stepped at their
decimals and must not move — scaling by 10ⁿ would print `1.11` and `4.34`
(Review Focus 2); `100` has no decimals so the value gets one; `0.125` gives 4;
`0.1234567` is capped at 6; `1E-05` counts its exponent (Review Focus 3); the
last three are negative and a negative zero (Review Focus 4). The clear rows:
`6.4951` rounds up to nearest, not down; `0.125` and `0.375` are exact binary
midpoints and round half to even (R147); `-0.001` prints `0.00` (R146). Each
scan sets the value on the first scan, which raises at once (on-delay 0); the
clear rows raise first at `limit + 1`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.AlarmTests"`
Expected: FAIL — 16 failed, 27 passed, 43 total. The two rows that already
pass are `Hi 7.5 / 7.71` and `Lo 4.4 / 4.35` (today's full-precision text is
already `7.71` and `4.35`); every other new row fails on the message (e.g.
`Hi: 7.5004 above 7.5.`, `Hi: 1.23E-05 above 1E-05.`). The 25 existing
`AlarmTests` pass.

- [ ] **Step 3: Implement `Display`**

In `src/Millrace.Control/Alarm.cs`, replace

```csharp
                        outputs.Raise("ALARM_RAISED", string.Create(CultureInfo.InvariantCulture,
                            $"{limit.Kind}: {value} {(high ? "above" : "below")} {limit.Value}."));
```

with

```csharp
                        outputs.Raise("ALARM_RAISED", string.Create(CultureInfo.InvariantCulture,
                            $"{limit.Kind}: {Display(value, limit.Value, high ? 1 : -1)} {(high ? "above" : "below")} {limit.Value}."));
```

replace

```csharp
                outputs.Raise("ALARM_CLEARED", string.Create(CultureInfo.InvariantCulture,
                    $"{limit.Kind}: {value} back within limits."));
```

with

```csharp
                outputs.Raise("ALARM_CLEARED", string.Create(CultureInfo.InvariantCulture,
                    $"{limit.Kind}: {Display(value, limit.Value, 0)} back within limits."));
```

and replace the end of the class

```csharp
            outputs.Set(2 * i, TagValue.Bool(_active[i]));
            outputs.Set((2 * i) + 1, TagValue.Bool(_acked[i]));
        }
    }
}
```

with

```csharp
            outputs.Set(2 * i, TagValue.Bool(_active[i]));
            outputs.Set((2 * i) + 1, TagValue.Bool(_acked[i]));
        }
    }

    /// <summary>
    /// The value as an alarm message shows it (spec 6e criteria 2 and 3): with
    /// one more decimal than the limit's shortest round-trip form has, and at
    /// most six. <paramref name="direction"/> is +1 to round up (a Hi or HiHi
    /// raise), -1 to round down (a Lo or LoLo raise) and 0 to round to nearest
    /// (a clear). A directed rounding starts from the nearest <c>F</c> text and
    /// moves it one step only when that text lies on the wrong side of the
    /// value, so a value whose text parses back to the same double is never
    /// stepped and no binary-scaling artefact appears. A negative zero prints
    /// as zero.
    /// </summary>
    private static string Display(double value, double limit, int direction)
    {
        string shortest = limit.ToString("R", CultureInfo.InvariantCulture);
        int e = shortest.IndexOf('E', StringComparison.Ordinal);
        int exponent = e < 0 ? 0 : int.Parse(shortest.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mantissa = e < 0 ? shortest : shortest[..e];
        int dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        int limitDecimals = Math.Max(0, (dot < 0 ? 0 : mantissa.Length - dot - 1) - exponent);
        int decimals = Math.Min(limitDecimals + 1, 6);

        string format = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        string text = value.ToString(format, CultureInfo.InvariantCulture);
        double shown = double.Parse(text, CultureInfo.InvariantCulture);
        if ((direction > 0 && shown < value) || (direction < 0 && shown > value))
        {
            var step = new decimal(1, 0, 0, direction < 0, (byte)decimals);
            text = (decimal.Parse(text, CultureInfo.InvariantCulture) + step).ToString(format, CultureInfo.InvariantCulture);
        }

        return text.StartsWith('-') && text.AsSpan(1).IndexOfAnyExcept('0', '.') < 0 ? text[1..] : text;
    }
}
```

(`using System.Globalization;` is already at the top of the file; it brings
`NumberStyles`. Do not simplify the rounding to `Math.Ceiling(value * 10ⁿ) / 10ⁿ`
or to a `(decimal)value` conversion — R145 measures why both are wrong, and the
`1.1`/`4.35`/`8.600000000000001` rows catch them.)

- [ ] **Step 4: Run the alarm tests to verify they pass**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.AlarmTests"`
Expected: PASS, 43.

- [ ] **Step 5: See which goldens are now stale**

Run: `dotnet test Millrace.sln --nologo`
Expected: FAIL in exactly these (report the measured list if it differs):
`Millrace.Control.Tests.WorkedExampleTests.TheWorkedExampleMatchesItsGolden` and
`TheJsonWorkedExampleWritesTheCodeBuiltEventLogByteForByte`;
`Millrace.Cli.Tests.RunCommandTests.TheWorkedExampleRunsFromItsFilesAndMatchesTheControlGolden`;
`Millrace.Samples.Tests.MineConveyorTests.EveryScenarioMatchesItsGolden(name: "chute-blockage")`.
(Only `chute-blockage` has an alarm event among the sample goldens; no
`Millrace.Scenarios.Tests` golden has one.)

- [ ] **Step 6: Regenerate the two goldens and read them**

Run, one at a time:

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.WorkedExampleTests"
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"
```

Then `git status --short` — expect, besides this task's two source files,
exactly two logs:

```
 M samples/mine-conveyors/expected/chute-blockage.log
 M tests/Millrace.Control.Tests/Golden/conveyor-control.log
```

Read `git diff -- samples/mine-conveyors/expected/chute-blockage.log` and
`git diff -- tests/Millrace.Control.Tests/Golden/conveyor-control.log` in full and
quote them in the report. Expected changed lines, and nothing else:

```
-06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705084760820622 above 7.5.
-06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806537054771315 above 8.6.
+06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.71 above 7.5.
+06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.81 above 8.6.
-06:03:09.800  ALM_CV001  ALARM_CLEARED  Hi: 0 back within limits.
-06:03:09.800  ALM_CV001  ALARM_CLEARED  HiHi: 0 back within limits.
+06:03:09.800  ALM_CV001  ALARM_CLEARED  Hi: 0.00 back within limits.
+06:03:09.800  ALM_CV001  ALARM_CLEARED  HiHi: 0.00 back within limits.
```

```
-06:00:03.400  CUR01  ALARM_RAISED  HiHi: 10.000765680139894 above 8.
-06:00:03.700  CUR01  ALARM_CLEARED  HiHi: 7.40937291733113 back within limits.
-06:00:03.800  CUR01  ALARM_RAISED  Hi: 6.701381615037194 above 3.
+06:00:03.400  CUR01  ALARM_RAISED  HiHi: 10.1 above 8.
+06:00:03.700  CUR01  ALARM_CLEARED  HiHi: 7.4 back within limits.
+06:00:03.800  CUR01  ALARM_RAISED  Hi: 6.8 above 3.
-06:00:04.700  CUR01  ALARM_CLEARED  Hi: 2.707919679974949 back within limits.
+06:00:04.700  CUR01  ALARM_CLEARED  Hi: 2.7 back within limits.
```

(`10.000765680139894` over HiHi 8 prints `10.1`: the raise rounds away from the
limit, R145.)

- [ ] **Step 7: Remove the stray `.actual` file**

Step 5's red run made `tests/Shared/Golden.cs` write the new output beside
the control golden as `tests/Millrace.Control.Tests/Golden/conveyor-control.log.actual`
(git-ignored, so `git status` does not show it). It holds the old
full-precision lines and would trip Task 3's decimal grep. Run:

```bash
rm -f tests/Millrace.Control.Tests/Golden/conveyor-control.log.actual
```

- [ ] **Step 8: Write and run the criterion 6 check**

Create `.superpowers/sdd/6e/golden-shape.sh` with the Write tool:

```bash
#!/usr/bin/env bash
# Plan 6e, spec criterion 6: every event log that moved since BASE keeps its
# line count, and on every line its time, source and event; its message text
# differs only in numbers. Run from the repository root:
#   bash .superpowers/sdd/6e/golden-shape.sh 7041258
set -euo pipefail
base="$1"
files=$(git diff --name-only "$base" -- '*.log')
if [ -z "$files" ]; then
  echo "No event log moved since $base."
  exit 1
fi

status=0
for f in $files; do
  git show "$base:$f" > .superpowers/sdd/6e/before.log
  awk -v name="$f" '
    function fields(line, out,   i, p) {
      for (i = 1; i <= 3; i++) {
        p = index(line, "  ")
        if (p == 0) { out[i] = line; line = "" }
        else { out[i] = substr(line, 1, p - 1); line = substr(line, p + 2) }
      }
      out[4] = line
    }
    function shape(message) { gsub(/-?[0-9]+(\.[0-9]+)?/, "#", message); return message }
    NR == FNR { before[FNR] = $0; n = FNR; next }
    {
      m = FNR
      fields(before[FNR], a)
      fields($0, b)
      if (a[1] != b[1] || a[2] != b[2] || a[3] != b[3]) { printf "%s:%d: time, source or event differs\n", name, FNR; bad = 1 }
      else if (shape(a[4]) != shape(b[4])) { printf "%s:%d: message differs beyond its numbers\n", name, FNR; bad = 1 }
      else if (a[4] != b[4]) { changed++ }
    }
    END {
      if (m != n) { printf "%s: %d lines before, %d after\n", name, n, m; bad = 1 }
      if (!bad) { printf "OK  %s  %d lines, %d changed\n", name, m, changed }
      exit bad
    }' .superpowers/sdd/6e/before.log "$f" || status=1
done

rm -f .superpowers/sdd/6e/before.log
exit "$status"
```

Run: `bash .superpowers/sdd/6e/golden-shape.sh 7041258`
Expected (exit 0):

```
OK  samples/mine-conveyors/expected/chute-blockage.log  95 lines, 4 changed
OK  tests/Millrace.Control.Tests/Golden/conveyor-control.log  40 lines, 4 changed
```

Quote the output in the report.

- [ ] **Step 9: Bring the README's quotes to the regenerated golden**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo`
Expected: FAIL — 1 failed:
`SampleReadmeTests.EveryQuotedLineIsAWholeLineOfItsGolden(name: "chute-blockage")`
(the README still quotes the two old `ALARM_RAISED` lines; the build has now
copied the regenerated golden to `bin/`).

In `samples/mine-conveyors/README.md`, replace

```
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.705084760820622 above 7.5.
06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.806537054771315 above 8.6.
```

with

```
06:01:53.400  ALM_CV001  ALARM_RAISED  Hi: 7.71 above 7.5.
06:02:09.300  ALM_CV001  ALARM_RAISED  HiHi: 8.81 above 8.6.
```

- [ ] **Step 10: Run everything**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, 78.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1436**: 37 / 498 / 132 / 57 /
230 / 167 / 78 / 136 / 23 / 78.
Run: `git status --short` — expect exactly the five files of Step 11
(`.superpowers/` is ignored).

- [ ] **Step 11: Commit**

```bash
git add src/Millrace.Control/Alarm.cs tests/Millrace.Control.Tests/AlarmTests.cs tests/Millrace.Control.Tests/Golden/conveyor-control.log samples/mine-conveyors/expected/chute-blockage.log samples/mine-conveyors/README.md
git commit -F .superpowers/sdd/6e/msg-task2.txt
```

with `.superpowers/sdd/6e/msg-task2.txt`:

```
feat(control): print alarm values to one more decimal than the limit

ALARM_RAISED and ALARM_CLEARED printed the value as a full-precision
double. The value now shows one more decimal than the limit's shortest
round-trip form has (at most six), and a raise rounds away from the
limit, so a raised value never displays on or inside it: "Hi: 7.71 above
7.5." The rounding starts from the nearest F<n> text and steps it once
only when that text lies on the wrong side of the value, so a value whose
F<n> text parses back to the same double is never stepped (1.1 over 0.5
prints 1.10); a negative zero prints as zero.
Limits print as before.

The worked-example golden and the chute-blockage golden are regenerated
(alarm lines only, checked to differ only in numbers), and the sample
README quotes the new lines.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 3: Component messages print measured values to fixed decimals

**Model:** implementer sonnet; reviewer sonnet (give the reviewer the whole
`git diff` of the eleven logs and the check script's output).

**Files:**
- Modify: `src/Millrace.Components/Mechanical/Motor.cs` (`AT_SPEED`, `STALLED`)
- Modify: `src/Millrace.Components/Mechanical/MotorStarter.cs` (`OVERLOAD_TRIP`)
- Modify: `src/Millrace.Components/Flow/BulkProcessUnit.cs` (hold satisfied)
- Modify: `src/Millrace.Components/Flow/ItemProcessUnit.cs` (hold satisfied)
- Test: `tests/Millrace.Components.Tests/MotorTests.cs`, `MotorStarterTests.cs`, `BulkProcessUnitTests.cs`, `ItemProcessUnitTests.cs` (+5 in all)
- Test: `tests/Millrace.Control.Tests/DocumentationTests.cs` (made `partial`; +1 fact)
- Regenerate: all nine `samples/mine-conveyors/expected/*.log`, `tests/Millrace.Control.Tests/Golden/conveyor-control.log`, `tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log`
- Modify: `samples/mine-conveyors/README.md` (two quoted lines), `docs/control-blocks.md` (one quoted line)

**Interfaces:**
- Consumes: the Components test rigs (`MotorTests.Rig`, `MotorStarterTests.Build`,
  `BulkProcessUnitTests.Build`, `ItemProcessUnitTests.Build`), unchanged;
  `.superpowers/sdd/6e/golden-shape.sh` from Task 2.
- Produces (message texts; nothing else changes):
  `Reached {speed:F1} rad/s.`;
  `Torque demand {demand:F0} N·m exceeds breakdown torque {breakdown:F0} N·m.`;
  `Thermal state {thermal:F3} reached the trip level {TripLevel}.`;
  `Hold satisfied after {elapsed:F2} s; discharging {mass:F1} kg of {output}.`;
  `Hold satisfied after {elapsed:F2} s; discharging {count} items.`

- [ ] **Step 1: Write the failing tests**

In `tests/Millrace.Components.Tests/MotorTests.cs`, replace the end of the class

```csharp
        Assert.Equal(["bearing-friction", "thermal-bias"], motor.SupportedFaults.Select(f => f.Id));
    }
}
```

with

```csharp
        Assert.Equal(["bearing-friction", "thermal-bias"], motor.SupportedFaults.Select(f => f.Id));
    }

    [Fact]
    public void TheAtSpeedAndStalledMessagesPrintSpeedToOneDecimalAndTorqueToWholeNewtonMetres()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        rig.Demand.Value = 13.0;   // 2.6 × rated > 2.5 × 5 = 12.5 N·m breakdown
        rig.Run(10.0);

        // AT_SPEED at 95 % of 150 rad/s (measured 142.93480695413064). The breakdown
        // torque 12.5 is an exact binary midpoint, which F0 rounds to even: 12.
        Assert.Equal(
            ["Contactor closed.", "Reached 142.9 rad/s.", "Torque demand 13 N·m exceeds breakdown torque 12 N·m."],
            rig.Log.Records.Select(r => r.Message));
    }
}
```

In `tests/Millrace.Components.Tests/MotorStarterTests.cs`, replace the end of the
class

```csharp
        Assert.Throws<ArgumentException>(() => new MotorStarter("K1", tripLevel: 1.0, resetLevel: 1.0));
    }
}
```

with

```csharp
        Assert.Throws<ArgumentException>(() => new MotorStarter("K1", tripLevel: 1.0, resetLevel: 1.0));
    }

    [Theory]
    [InlineData(1.23456, "Thermal state 1.235 reached the trip level 1.1.")]
    [InlineData(1.1000357303409216, "Thermal state 1.100 reached the trip level 1.1.")]
    public void TheOverloadTripMessagePrintsTheThermalStateToThreeDecimals(double thermal, string message)
    {
        Rig rig = Build();
        rig.Command.Value = true;
        rig.Sim.Tick();

        rig.Thermal.Value = thermal;
        rig.Sim.Tick();

        Assert.Equal(message, Assert.Single(rig.Sim.Events.Records, r => r.Code == "OVERLOAD_TRIP").Message);
    }
}
```

(The second row is the chute-blockage golden's trip value; the trip level is
configured and prints as today.)

In `tests/Millrace.Components.Tests/BulkProcessUnitTests.cs`, replace the end of the
class

```csharp
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkProcessUnit(
            "M", [new RecipeLine("A", Flour, 1.0)], Hold.ForSeconds(1.0), Dough, yield: 1.5));
    }
}
```

with

```csharp
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkProcessUnit(
            "M", [new RecipeLine("A", Flour, 1.0)], Hold.ForSeconds(1.0), Dough, yield: 1.5));
    }

    [Fact]
    public void TheHoldSatisfiedMessagePrintsSecondsToTwoDecimalsAndMassToOne()
    {
        Plant plant = Build(Hold.ForSeconds(1.0), yield: 0.9);

        plant.Sim.RunFor(TimeSpan.FromSeconds(4));

        Assert.Equal(
            "Hold satisfied after 1.00 s; discharging 9.0 kg of Dough.",
            plant.Sim.Events.Records.First(r => r.Source == "Mixer" && r.Code == "DISCHARGING").Message);
    }
}
```

In `tests/Millrace.Components.Tests/ItemProcessUnitTests.cs`, replace the end of the
class

```csharp
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemProcessUnit("U", 1, Hold.ForSeconds(1.0), yield: 0.0));
    }
}
```

with

```csharp
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemProcessUnit("U", 1, Hold.ForSeconds(1.0), yield: 0.0));
    }

    [Fact]
    public void TheHoldSatisfiedMessagePrintsSecondsToTwoDecimals()
    {
        Plant plant = Build(batchSize: 3, Hold.ForSeconds(2.0));

        plant.Sim.RunFor(TimeSpan.FromSeconds(7));

        Assert.Equal(
            "Hold satisfied after 2.00 s; discharging 3 items.",
            Assert.Single(plant.Sim.Events.Records, r => r.Source == "Furnace" && r.Code == "DISCHARGING").Message);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Components.Tests --nologo --filter "FullyQualifiedName~Millrace.Components.Tests.MotorTests|FullyQualifiedName~Millrace.Components.Tests.MotorStarterTests|FullyQualifiedName~Millrace.Components.Tests.BulkProcessUnitTests|FullyQualifiedName~Millrace.Components.Tests.ItemProcessUnitTests"`
Expected: FAIL — 5 failed, 33 passed, 38 total: the five new tests (today's
texts are `Reached 142.93480695413064 rad/s.`, `… breakdown torque 12.5 N·m.`,
`Thermal state 1.23456 …`, `Thermal state 1.1000357303409216 …`,
`Hold satisfied after 1 s; discharging 9 kg of Dough.`,
`Hold satisfied after 2 s; discharging 3 items.`).

- [ ] **Step 3: Change the four formats**

In `src/Millrace.Components/Mechanical/Motor.cs`, replace

```csharp
                    $"Torque demand {demand} N·m exceeds breakdown torque {_rating.BreakdownTorqueMultiple * ratedTorque} N·m."));
```

with

```csharp
                    $"Torque demand {demand:F0} N·m exceeds breakdown torque {_rating.BreakdownTorqueMultiple * ratedTorque:F0} N·m."));
```

and replace

```csharp
                ctx.Log(Id, "AT_SPEED", string.Create(CultureInfo.InvariantCulture, $"Reached {_speed} rad/s."));
```

with

```csharp
                ctx.Log(Id, "AT_SPEED", string.Create(CultureInfo.InvariantCulture, $"Reached {_speed:F1} rad/s."));
```

In `src/Millrace.Components/Mechanical/MotorStarter.cs`, replace

```csharp
                $"Thermal state {thermal} reached the trip level {TripLevel}."));
```

with

```csharp
                $"Thermal state {thermal:F3} reached the trip level {TripLevel}."));
```

In `src/Millrace.Components/Flow/BulkProcessUnit.cs`, replace

```csharp
            string.Create(CultureInfo.InvariantCulture, $"Hold satisfied after {_elapsed} s; discharging {_batch.Mass} kg of {_output}."),
```

with

```csharp
            string.Create(CultureInfo.InvariantCulture, $"Hold satisfied after {_elapsed:F2} s; discharging {_batch.Mass:F1} kg of {_output}."),
```

In `src/Millrace.Components/Flow/ItemProcessUnit.cs`, replace

```csharp
            string.Create(CultureInfo.InvariantCulture, $"Hold satisfied after {_elapsed} s; discharging {_items.Count} items."),
```

with

```csharp
            string.Create(CultureInfo.InvariantCulture, $"Hold satisfied after {_elapsed:F2} s; discharging {_items.Count} items."),
```

(Each site already formats through `string.Create(CultureInfo.InvariantCulture, …)`,
so the `:Fn` holes are invariant.)

- [ ] **Step 4: Run the component tests to verify they pass**

Run the Step 2 command — expect PASS, 38.
Run: `dotnet test tests/Millrace.Components.Tests --nologo` — expect PASS, 137.

- [ ] **Step 5: Add the guard on the control-blocks page's quoted log lines**

In `tests/Millrace.Control.Tests/DocumentationTests.cs`, replace

```csharp
using System.Runtime.CompilerServices;

namespace Millrace.Control.Tests;

public class DocumentationTests
{
```

with

```csharp
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Millrace.Control.Tests;

public partial class DocumentationTests
{
    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}\.\d{3}  ")]
    private static partial Regex LogLine();

```

replace

```csharp
    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string Page([CallerFilePath] string callerFile = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "docs", "control-blocks.md"));
}
```

with

```csharp
    [Fact]
    public void EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden()
    {
        HashSet<string> golden = [.. File.ReadAllText(Golden()).ReplaceLineEndings("\n").Split('\n')];
        string[] quoted = Page().Split('\n').Where(l => LogLine().IsMatch(l)).ToArray();

        Assert.Equal(3, quoted.Length);
        Assert.All(quoted, line => Assert.True(golden.Contains(line), $"The page quotes a line the golden does not log: '{line}'."));
    }

    /// <summary>Two levels up from this file is the repository root.</summary>
    private static string Page([CallerFilePath] string callerFile = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", "docs", "control-blocks.md"));

    /// <summary>The worked example's golden, beside this file.</summary>
    private static string Golden([CallerFilePath] string callerFile = "") =>
        Path.Combine(Path.GetDirectoryName(callerFile)!, "Golden", "conveyor-control.log");
}
```

(The page's only lines that start with a log time are the worked example's
three, `docs/control-blocks.md` lines 220–222.)

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.DocumentationTests"`
Expected: PASS, 5 — the golden has not been regenerated yet, so the page and
the golden still agree. The guard goes red in Step 7.

- [ ] **Step 6: Regenerate the goldens and read them**

Run, one at a time:

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~Millrace.Control.Tests.WorkedExampleTests"
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"
MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Scenarios.Tests --nologo --filter "FullyQualifiedName~EveryValidScenarioRunsCleanAndMatchesItsGolden"
```

Then `git status --short -- '*.log'` — expect exactly eleven:

```
 M samples/mine-conveyors/expected/chute-blockage.log
 M samples/mine-conveyors/expected/e-stop.log
 M samples/mine-conveyors/expected/failed-zero-speed.log
 M samples/mine-conveyors/expected/feed-starve.log
 M samples/mine-conveyors/expected/normal-start-stop.log
 M samples/mine-conveyors/expected/overload.log
 M samples/mine-conveyors/expected/pull-key.log
 M samples/mine-conveyors/expected/start-while-tripped.log
 M samples/mine-conveyors/expected/welded-contactor.log
 M tests/Millrace.Control.Tests/Golden/conveyor-control.log
 M tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log
```

Read `git diff -U0 -- '*.log'` in full and quote it in the report. Against
Task 2's commit the changed lines are exactly: lines 56, 68, 77 of each sample
golden (`AT_SPEED`: `146.2`, `146.2`, `146.0`); `chute-blockage.log` line 83
(`Thermal state 1.100`); `overload.log` line 80 (`Thermal state 1.286`);
`start-while-tripped.log` lines 117 and 127 (`144.8`, `144.5`);
`conveyor-control.log` lines 27 (`Reached 140.0 rad/s.`) and 29
(`Thermal state 1.180`); `conveyor-start-and-fault.log` lines 9
(`Reached 139.8 rad/s.`) and 11 (`Thermal state 1.156`). The Measurements
section above gives every one of them in full.

Run: `bash .superpowers/sdd/6e/golden-shape.sh 7041258`
Expected (exit 0) — every log that moved since the spec commit, both tasks
together:

```
OK  samples/mine-conveyors/expected/chute-blockage.log  95 lines, 8 changed
OK  samples/mine-conveyors/expected/e-stop.log  92 lines, 3 changed
OK  samples/mine-conveyors/expected/failed-zero-speed.log  90 lines, 3 changed
OK  samples/mine-conveyors/expected/feed-starve.log  79 lines, 3 changed
OK  samples/mine-conveyors/expected/normal-start-stop.log  112 lines, 3 changed
OK  samples/mine-conveyors/expected/overload.log  104 lines, 4 changed
OK  samples/mine-conveyors/expected/pull-key.log  99 lines, 3 changed
OK  samples/mine-conveyors/expected/start-while-tripped.log  132 lines, 5 changed
OK  samples/mine-conveyors/expected/welded-contactor.log  117 lines, 3 changed
OK  tests/Millrace.Control.Tests/Golden/conveyor-control.log  40 lines, 6 changed
OK  tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log  16 lines, 2 changed
```

Quote the output in the report. Also run
`grep -rnE --include='*.log' '[0-9]\.[0-9]{4,}' samples/mine-conveyors/expected tests/Millrace.Control.Tests/Golden tests/Millrace.Scenarios.Tests/Golden`
— expect no output (exit status 1): no event log holds a number with four or
more decimals (R143).

- [ ] **Step 7: Bring the quotes to the regenerated goldens**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo` and
`dotnet test tests/Millrace.Control.Tests --nologo`.
Expected: FAIL — `SampleReadmeTests.EveryQuotedLineIsAWholeLineOfItsGolden`
rows `overload` and `chute-blockage`, and
`DocumentationTests.EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden`
(each quotes an old `OVERLOAD_TRIP` line). Nothing else.

In `samples/mine-conveyors/README.md`, replace

```
06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.285658707267615 reached the trip level 1.1.
```

with

```
06:01:20.000  CV003.Starter  OVERLOAD_TRIP  Thermal state 1.286 reached the trip level 1.1.
```

and replace

```
06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1000357303409216 reached the trip level 1.1.
```

with

```
06:03:09.770  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.100 reached the trip level 1.1.
```

In `docs/control-blocks.md`, replace

```
06:00:40.000  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.1796472378913028 reached the trip level 1.1.
```

with

```
06:00:40.000  CV001.Starter  OVERLOAD_TRIP  Thermal state 1.180 reached the trip level 1.1.
```

No surrounding prose quotes these numbers (measured by grep); no other line of
either document changes.

- [ ] **Step 8: Run everything**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, 78.
Run: `dotnet test tests/Millrace.Control.Tests --nologo` — expect PASS, 137.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1442**: 37 / 498 / 137 / 57 /
230 / 167 / 78 / 137 / 23 / 78.
Run: `git diff --stat 7041258 -- src/` — expect exactly the six files of the
Global Constraints.
Run: `git status --short` — expect exactly the twenty-two files of Step 9.

- [ ] **Step 9: Commit**

```bash
git add src/Millrace.Components/Mechanical/Motor.cs src/Millrace.Components/Mechanical/MotorStarter.cs src/Millrace.Components/Flow/BulkProcessUnit.cs src/Millrace.Components/Flow/ItemProcessUnit.cs tests/Millrace.Components.Tests/MotorTests.cs tests/Millrace.Components.Tests/MotorStarterTests.cs tests/Millrace.Components.Tests/BulkProcessUnitTests.cs tests/Millrace.Components.Tests/ItemProcessUnitTests.cs tests/Millrace.Control.Tests/DocumentationTests.cs tests/Millrace.Control.Tests/Golden/conveyor-control.log tests/Millrace.Scenarios.Tests/Golden/conveyor-start-and-fault.log samples/mine-conveyors/expected/chute-blockage.log samples/mine-conveyors/expected/e-stop.log samples/mine-conveyors/expected/failed-zero-speed.log samples/mine-conveyors/expected/feed-starve.log samples/mine-conveyors/expected/normal-start-stop.log samples/mine-conveyors/expected/overload.log samples/mine-conveyors/expected/pull-key.log samples/mine-conveyors/expected/start-while-tripped.log samples/mine-conveyors/expected/welded-contactor.log samples/mine-conveyors/README.md docs/control-blocks.md
git commit -F .superpowers/sdd/6e/msg-task3.txt
```

(Twenty-two paths: four `src/` files, five test files, eleven logs, the
sample README and `docs/control-blocks.md`. If `git status` in Step 8 listed a
different set, report it before committing.)

with `.superpowers/sdd/6e/msg-task3.txt`:

```
feat(components): print measured values in event messages to fixed decimals

The motor's AT_SPEED prints its speed to 0.1 rad/s and STALLED its
torques to whole N·m; the starter's OVERLOAD_TRIP prints the thermal
state to three decimals; a process unit's hold-satisfied message prints
the hold time to 0.01 s and a bulk batch's mass to 0.1 kg. Configured
values in those messages print as before, and no value or event time
changes.

All eleven event-log goldens are regenerated — the nine sample logs, the
control worked example and conveyor-start-and-fault — each checked to keep
its lines, times, sources and events and differ only in numbers. The
sample README and docs/control-blocks.md quote the new lines, and a new
documentation test pins the control-blocks quote to its golden.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

## Spec coverage

| Spec criterion / section | Task | Tests |
|---|---|---|
| 1. `AT_SPEED` F1, `STALLED` F0 (both torques) | 3 | `MotorTests.TheAtSpeedAndStalledMessagesPrintSpeedToOneDecimalAndTorqueToWholeNewtonMetres`; `MineConveyorTests.EveryScenarioMatchesItsGolden` (nine, regenerated) |
| 1. `OVERLOAD_TRIP` F3 | 3 | `MotorStarterTests.TheOverloadTripMessagePrintsTheThermalStateToThreeDecimals` (2 rows); `WorkedExampleTests.TheWorkedExampleMatchesItsGolden`; `CorpusTests.EveryValidScenarioRunsCleanAndMatchesItsGolden` |
| 1. hold satisfied F2 / F1 (bulk), F2 (item) | 3 | `BulkProcessUnitTests.TheHoldSatisfiedMessagePrintsSecondsToTwoDecimalsAndMassToOne`; `ItemProcessUnitTests.TheHoldSatisfiedMessagePrintsSecondsToTwoDecimals` |
| 1. `ALARM_RAISED` / `ALARM_CLEARED` F<d+1> | 2 | `AlarmTests.ARaisePrintsOneMoreDecimalThanTheLimitRoundedAwayFromIt` (14 rows), `AClearPrintsTheValueRoundedToNearest` (4 rows) |
| 2. *d* from the limit's `"R"` form; `min(d + 1, 6)`; 7.5 → 2, 100 → 1, 0.125 → 4 | 2 | `ARaisePrintsOneMoreDecimalThanTheLimitRoundedAwayFromIt` rows `7.5`, `100`, `0.125`, `0.1234567` (cap), `1E-05` (exponent, R144) |
| 3. raise rounds away (Hi up, Lo down); 7.5004 → 7.51; 1.9996 → 1.9; clear to nearest | 2 | same theory rows `7.5004`, `1.9996`, `8.600000000000001`, `1.9999999999999998`, `1.1`, `4.35`, `7.71`, the negatives; `AClearPrintsTheValueRoundedToNearest` |
| 4. configured values print as today | 2, 3 | every new message assertion includes the limit / trip level as today (`above 1E-05.`, `the trip level 1.1.`); existing `AlarmTests` unchanged (R144); `golden-shape.sh`'s per-file "N changed" counts plus the read `git diff -U0` (only the five sites' lines move; every `ZERO_SPEED`, `SEQUENCE_*` line is untouched) |
| 5. `WRITE`, tag values, frames, telemetry, other messages unchanged | 2, 3 | `golden-shape.sh`'s per-file "N changed" counts plus the read `git diff -U0` (only the five sites' lines move; all 422 `WRITE` lines in the eleven checked logs untouched); every existing test, unchanged but two (R150) |
| 6. every event log regenerated and checked mechanically | 2, 3 | `.superpowers/sdd/6e/golden-shape.sh 7041258` (output quoted); `EveryScenarioMatchesItsGolden`, `EveryScenarioReplaysByteForByteFromARecording`, `WorkedExampleTests.TheWorkedExampleMatchesItsGolden`, `TheJsonWorkedExampleWritesTheCodeBuiltEventLogByteForByte`, `RunCommandTests.TheWorkedExampleRunsFromItsFilesAndMatchesTheControlGolden`, `CorpusTests.EveryValidScenarioRunsCleanAndMatchesItsGolden` |
| 7. `MR016` hint: Writes first, then all tags; plain fix otherwise; matching unchanged | 1 | `ClaimValidationTests.AClaimNamingNoTagIsMr016`, `AClaimNearATagIsHintedWithTheNearestName` (3 rows), `AClaimNearNoTagKeepsThePlainFix` (2 rows), `TheBlocksOwnWritesAreSearchedBeforeEveryOtherTag`, `AWriteThePlantDoesNotHaveIsNeverTheHint`, `AClaimIsMatchedOrdinallyAndExactly` (unchanged) |
| §4 the split still holds; the hint on `millrace validate`'s `Fix:` line; a far-off claim gets none | 1 | `ClaimTests.TheMessageIsSplitIntoSymptomAndFix`, `AClaimNearNoTagKeepsThePlainFix`; `MineConveyorTests.MillraceValidateNamesTheNearestTagOnTheFixLineOfAMistypedClaim` (2 rows) |
| §3 one private static method; Hi and Lo just past, whole-number limit, 6-decimal cap, a clear | 2 | `Alarm.Display`; `AlarmTests` theories above |
| §5 README and doc quotes follow; their tests | 2, 3 | `SampleReadmeTests.EveryQuotedLineIsAWholeLineOfItsGolden`; `DocumentationTests.EveryLogLineThePageQuotesIsAWholeLineOfTheWorkedExampleGolden` |
| §5 diagnostics reference unchanged | 1 | `DiagnosticsReferenceTests.TheCommittedReferencePageIsCurrent` (unchanged, passes) |
| Review Focus 1–5 | 1, 2 | `ARaisePrintsOneMoreDecimalThanTheLimitRoundedAwayFromIt` rows named there; `AClearPrintsTheValueRoundedToNearest` row `-0.001`; `TheBlocksOwnWritesAreSearchedBeforeEveryOtherTag`, `AWriteThePlantDoesNotHaveIsNeverTheHint` |

## Test-count arithmetic

Baseline on `7041258` (measured): 1408 = 37 + 491 + 132 + 57 + 229 + 167 + 78 +
118 + 23 + 76.

| Task | Added | Project totals after | Suite |
|---|---|---|---|
| 1 | `ClaimValidationTests` theory 3 + theory 2 + 2 facts = 7 → Core +7; `ClaimTests` 1 fact → Configuration +1; `MineConveyorTests` theory 2 → Samples +2 (two existing tests change, none added or removed by that) | Core 498, Configuration 230, Samples 78 | 1418 |
| 2 | `AlarmTests` theory 14 + theory 4 = 18 → Control +18 | Control 136 | 1436 |
| 3 | `MotorTests` 1 + `MotorStarterTests` theory 2 + `BulkProcessUnitTests` 1 + `ItemProcessUnitTests` 1 = 5 → Components +5; `DocumentationTests` 1 → Control +1 | Components 137, Control 137 | 1442 |

Final: **1442** = 37 Io.Abstractions / 498 Core / 137 Components / 57 Realtime /
230 Configuration / 167 Scenarios / 78 Cli / 137 Control / 23 Control.Catalogue /
78 Samples (measured in the scratch run). Existing tests changed: two
(`ClaimValidationTests.AClaimNamingNoTagIsMr016`,
`ClaimTests.TheMessageIsSplitIntoSymptomAndFix`, R150); no existing test is
removed or renamed. Goldens regenerated: eleven event logs (two of them in
both Tasks 2 and 3); no other golden moves.
