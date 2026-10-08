# Block-Claimed Tags Implementation Plan (plan 6d)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a control block **claim** a plant tag it writes, so that the tag is
written by its claimant and by nothing else — external writes (`TagImage.Write`,
`Simulation.WriteAt`/`WriteIn`, the realtime `CommandBus`, a scenario) are
refused at the call site, another block that commands it fails validation
(`MR016`) — declare it in a plant file as `"claims": [ … ]` on a controller
entry, and close 6c's documented bypass by having the mine-conveyor sample's
four interlocks claim `CV001.Permit`, `CV002.Permit`, `CV003.Permit` and
`Feed.Permit`, with all nine sample goldens byte-identical.

**Architecture:** The claim lives in Core, not in the block. `SimulationBuilder.AddScanBlock(block, claims)`
stores the list; `Validate()` checks it after every block's owned tags exist
(`MR016`) and yields a tag-name → claimant map; `Build()` hands that map to the
`TagDirectory`, which publishes a claimed tag `ReadOnly` with a new
`TagDescriptor.ClaimedBy`, while the `TagBinding` stays writable. `TagImage.Check`
takes the write's origin and refuses a claimed tag to every origin but the
claimant (external writes have none). Every consumer that already honours
`ReadOnly` — `CommandBus`, the scenario binder, `millrace tags` — refuses it with no
change beyond a clearer message. `Millrace.Configuration` reads `claims`, passes it
through, and places Core's `MR016` on the `claims[j]` entry it is about through
a new `ValidationError.Tag` (R133). `IScanBlock` and every block type are
unchanged.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401), C#, xUnit 2.9.3. No package
under `src/`. JsonSchema.Net 8.0.5, test-only and pinned (unchanged).

**Spec:** `docs/superpowers/specs/2026-09-30-block-claimed-tags-design.md` (all of
it, as amended by this plan — R141 gives the amendment note the controller adds
to the spec with the plan commit), refining the tag image of plan 4, the
scan-block host of `2026-09-22-control-blocks-design.md` (5c), the controllers
of `2026-09-25-controllers-in-plant-json-design.md` (5d) and the run permit of
`2026-09-25-interlock-start-inhibit-design.md` (6c).

**Plan sequence:** This is plan 6d. Plans 1–5d, 6a, 6a.1 and 6c are merged on
`master`; this plan starts from `d26aae6` (the commit that added the 6d spec).
Measured on that commit with `dotnet test Millrace.sln`: **1348 tests**, all passing —
37 `Millrace.Io.Abstractions` / 461 `Millrace.Core` / 132 `Millrace.Components` / 56
`Millrace.Realtime` / 211 `Millrace.Configuration` / 163 `Millrace.Scenarios` / 76 `Millrace.Cli` /
117 `Millrace.Control` / 23 `Millrace.Control.Catalogue` / 72 `Millrace.Samples`. Release build
`0 Warning(s)`, `0 Error(s)`.

**Task shape.** Six tasks, sequential:

- **Core is two tasks.** Task 1 is the runtime seam (descriptor, directory,
  image) and is testable through the internal constructors alone; Task 2 is the
  builder (`AddScanBlock` claims, `MR016`) and the end-to-end behaviour through
  a built `Simulation`, including the realtime `CommandBus`. A reviewer can
  reject either without the other.
- **The diagnostics page goes with Configuration (Task 3):** it is a golden
  generated from `DiagnosticsReference`, and its trailer is where Core codes are
  documented (R140).
- **`millrace tags --format json` goes with the sample (Task 5):** both are "what a
  user sees of a claim on the command line"; the text form already prints the
  claimant after Task 1.
- **Prose docs last (Task 6):** `docs/control-blocks.md`, `docs/architecture.md`
  and the sample README, each pinned by a documentation test.

## Global Constraints

- **`src/` changes only in Tasks 1–5.** After Task 5,
  `git diff --stat d26aae6 -- src/` lists exactly
  `src/Millrace.Cli/CommandTable.cs`,
  `src/Millrace.Cli/Commands/Tags.cs`,
  `src/Millrace.Configuration/DiagnosticsReference.cs`,
  `src/Millrace.Configuration/Loading/BuildStage.cs`,
  `src/Millrace.Configuration/Loading/ControllerPass.cs`,
  `src/Millrace.Configuration/Loading/LoadState.cs`,
  `src/Millrace.Configuration/Loading/PlantSchemas.cs`,
  `src/Millrace.Configuration/Loading/StructureStage.cs`,
  `src/Millrace.Configuration/PlantSchema.cs`,
  `src/Millrace.Core/Control/ScanBlockRuntime.cs`,
  `src/Millrace.Core/Io/TagDirectory.cs`,
  `src/Millrace.Core/Io/TagImage.cs`,
  `src/Millrace.Core/Simulation.cs`,
  `src/Millrace.Core/SimulationBuilder.cs`,
  `src/Millrace.Core/Validation/ValidationError.cs`,
  `src/Millrace.Io.Abstractions/ITagWriter.cs`,
  `src/Millrace.Io.Abstractions/TagDescriptor.cs` and
  `src/Millrace.Scenarios/ScenarioRunner.cs`. No change under `src/Millrace.Control`,
  `src/Millrace.Control.Catalogue`, `src/Millrace.Components` or `src/Millrace.Realtime`:
  `IScanBlock` and every block type are unchanged (spec §1).
  `git grep -n PackageReference -- 'src/*.csproj'` prints nothing.
- **Unclaimed tags behave exactly as before** (criterion 9). Every event log in
  the solution stays byte-identical — the nine sample goldens included
  (criterion 8), the four `Millrace.Scenarios.Tests` goldens and
  `tests/Millrace.Control.Tests/Golden/conveyor-control.log`. Measured: with this
  plan's whole change applied, `MILLRACE_UPDATE_GOLDEN=1 dotnet test Millrace.sln` (in a
  scratch copy — never in the repository) rewrites exactly two files:
  `tests/Millrace.Configuration.Tests/Golden/plant.schema.json` (+35 −0) and
  `docs/configuration-diagnostics.md` (the trailer, R140). Nothing else moves.
- **Determinism.** No `System.Random`, no `string.GetHashCode()`, no
  `Dictionary`/`HashSet` iteration order reaching an assertion, an error order or
  an output: `CheckClaims` iterates the block list and each claim list in order
  and uses its dictionaries for lookup only. String comparisons are ordinal
  (claim names are matched exactly — R137). All formatting uses
  `CultureInfo.InvariantCulture`.
- **Warnings are errors** (`Directory.Build.props`: `TreatWarningsAsErrors`,
  `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `Nullable`). The
  Release build prints `0 Warning(s)` and `0 Error(s)` after every task:
  `dotnet build Millrace.sln -c Release --nologo`.
- **xUnit analyzers run under warnings-as-errors:** prefer `Assert.Single`,
  `Assert.Contains`, `Assert.DoesNotContain`, `Assert.Empty`, `Assert.All`;
  never `Assert.True(x.Any())` or `Assert.Equal(1, x.Count())`.
- **Test names** are long descriptive PascalCase sentences, like the existing
  ones (`AnExternalWriteToAClaimedTagIsRefusedByNameAndByIndexAndNothingIsQueued`).
- **Messages are verbatim.** Every refusal and diagnostic text in this plan is
  asserted exactly somewhere; copy them byte for byte. Core messages read
  "symptom. fix." — the loader splits at the first `". "` (R40), so no symptom
  sentence may contain `". "`.
- **Goldens are generated and read, never invented or hand-edited.** The two
  goldens that move (Task 3) use `tests/Shared/Golden.cs`: regenerate each with
  `MILLRACE_UPDATE_GOLDEN=1` and a `--filter` naming its one test, then read the whole
  `git diff` of that file and quote it in the task report. The nine sample
  goldens must **not** move: Task 5 runs
  `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
  — for this project and this theory only — and `git status --short samples/`
  must then list only `samples/mine-conveyors/plant.json`.
- **Report every measurement.** Where an expected value in this plan (a test
  count, a message, a line) disagrees with what the code produces, report the
  measured value in the task report; never adjust an assertion to fit without
  saying so.
- **JSON and Markdown files** are written exactly as this plan shows them: LF
  line endings, two-space indentation, a final newline, no tabs.
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
- **Commands**, from the repository root: `dotnet build Millrace.sln -c Release --nologo`
  (expect `0 Warning(s)`, `0 Error(s)`) and `dotnet test Millrace.sln --nologo`
  (expect the task's total). `.superpowers/` is git-ignored; scratch work goes
  under `.superpowers/sdd/6d/` and is never added.

## Review Focus

The five input classes or failure modes the spec implies but does not name,
most likely to bite first. Each has its pinning test in the owning task.

1. **A claim on a port that an explicit `Bind` renamed.** A plant file's
   `"tags"` entry (or `builder.Bind`) replaces a port's declared tag name; the
   author expects the claim to follow the name `millrace tags` prints, and the old
   name to be gone, not silently claimable. Test: Task 2,
   `ClaimValidationTests.AClaimFollowsTheNameAnExplicitBindGaveThePort`.
2. **A realtime client writing a claimed tag.** The HMI path is `CommandBus`,
   which never throws; the author expects `CommandOutcome.ReadOnly`, the
   `Rejected` counter to move and nothing to be queued — not an exception from
   `TagImage` escaping into the dispatcher. Test: Task 2,
   `ClaimedTagCommandTests.ACommandToAClaimedTagIsReadOnlyAndNeverQueued`.
3. **A recording made before the claim, replayed after it.** A scenario
   recorded on the unclaimed plant (6c's bypass, captured) must fail to bind on
   the claimed plant before tick 0, with the claimant named — never a partial
   log. Test: Task 4,
   `ClaimedTagScenarioTests.ARecordingThatWroteATagNowClaimedDoesNotReplay`.
4. **A claim name that is nearly right** — another case, a leading space, or
   empty. The author expects `MR016` "which the plant does not have", not a
   match by case-folding and not a crash from the tag-name rules. Test: Task 2,
   `ClaimValidationTests.AClaimIsMatchedOrdinallyAndExactly` (three rows).
5. **A claim on a block's command tag** — another block's (a sequence that alone
   may reset an interlock) or the block's own. The author expects it to work like
   any other claim: the command published `ReadOnly … claimed by`, the HMI
   refused, the claimant's write landing. Tests: Task 2,
   `ClaimValidationTests.ABlockMayClaimAnotherBlocksCommandAndThenOnlyItWritesIt`
   and `ABlockMayClaimItsOwnCommandWhenItCommandsIt`.

Also pinned, beyond the five: a claim duplicated within one entry
(`ATagClaimedTwiceByOneBlockIsOneMr016`; Task 3's `claims[1]` row) versus across
entries (`ATagClaimedByTwoBlocksIsOneMr016OnTheSecond`;
`ATagClaimedByTwoControllersIsMr016AtTheSecondClaim`), a claim on a writable
binding demoted by a signal link
(`AClaimOnAWritableTagDemotedByASignalLinkIsMr016`), the kind check running
before the claim check (`TheKindCheckRunsBeforeTheClaimCheck`), a repeated claim
reported at its own position (`ARepeatedClaimOnAMissingTagIsReportedOnceAtEachPosition`,
`AnotherBlocksWritePointsAtTheClaimantsAcceptedClaimNotItsRepeat`,
`ARepeatedClaimAndAnotherWriterAreEachReportedAtTheirOwnClaim`), and a claim
string holding `". "` (`ClaimTests` row `"FEED. Permit"`).

## Decisions settled here (rulings R133–R142)

These refine the spec where the code, or a measured run, forced a choice.
R141 gives the amendment note that records them in the spec.

- **R133 — How a `MR016` reaches `claims[j]` (measured mechanism).** Measured:
  `BuildStage.Run` calls `builder.Validate()` (once for the plant alone, again
  after `ControllerPass.Run` has added the blocks) and maps each Core
  `ValidationError` to a path in `BuildStage.PathOf`, which walks
  `error.ComponentIds`: a component id (or its first dotted segment) gives
  `$.components[i]`; a controller id gives `$.controllers[i]`, with one
  code-specific key, `MR013` → `.scanPeriodMs` (5d R92). The message is split
  at its first `". "` into message and fix. `ComponentIds` cannot say *which*
  claim an error is about, so the spec's "reported against `claims[i]`" is not
  reachable through the existing record alone. Chosen: `ValidationError` gains
  two non-positional init properties (source-compatible; every other error
  leaves them at their defaults): `public string Tag { get; init; } = ""`, the
  claim's tag as spelled, and `public int ClaimIndex { get; init; } = -1`, the
  claim's position in the list its block was added with — the block's own claim
  for checks (0)–(4) of R135, the **claimant's accepted** claim for "another
  block commands a claimed tag". `PathOf` gains a second code-specific key: for
  `MR016`, the first controller in `ComponentIds` whose `Claims[ClaimIndex]`
  equals `error.Tag` gives `$.controllers[i].claims[ClaimIndex]`; a controller
  that does not hold the claim there is skipped, and if none does the first
  controller's path is used. So every `MR016` lands on its own claim: a repeat
  on the repeat, each of two repeats of a missing tag on its own position, and
  "another block commands a claimed tag" on the claimant's accepted claim
  (`ComponentIds` is `[writer, claimant]`; the writer never holds that claim —
  a writer that claimed the tag itself is not reported, R135), even when the
  claimant also repeats it. **The message split also changes:** `BuildStage.Split`
  cut Core's text at its first `". "`, and a claim is any string (R137), so a
  claim such as `"FEED. Permit"` cut the symptom inside the quoted tag (measured:
  message `Block 'INT01' claims tag 'FEED.`, fix `Permit', which the plant does
  not have. …`). `Split` now takes the error and, when `Tag` is not empty,
  starts the search after the quoted tag; every other error splits exactly as
  before.
- **R134 — The directory gets claimants by name.** Spec §2 says the builder
  hands the directory "a claimant per binding index"; the builder does not know
  indices — the directory assigns them by sorting — so it passes
  `IReadOnlyDictionary<string, string>` (tag name → block id) to a new optional
  parameter of the internal `TagDirectory` constructor, which records a
  `string?` per index and exposes `internal string? ClaimantOf(int index)` for
  `TagImage.Check`. `PlantTags()` passes none (it has no blocks), so a plant
  file's controllers resolve against the unclaimed access, and another
  controller naming a claimed tag passes `MR115` and is reported by Core's
  `MR016` — measured, Task 3.
- **R135 — `MR016`: order, messages, one report per conflict.** For each block
  in add order, each claim in list order, the first failing check is reported:
  (0) the block already claimed this tag — `Block 'A' claims tag 'U.Enable', which it already claims. Claim each tag once.`
  (spec §2's "reported once"); (1) no tag — `Block 'A' claims tag 'U.Enabel', which the plant does not have. Check the name against 'millrace tags'; a block claims a tag it commands.`;
  (2) not read-write — `Block 'A' claims tag 'A.Q', which is read-only. Claim a read-write tag the block commands, not a measured value, a block's output or an input a signal link drives.`;
  (3) not in the block's `Writes` — `Block 'A' claims tag 'U.Setpoint', which it does not command. Add the tag to the block's writes, or remove the claim.`;
  (4) claimed by an earlier block — `Block 'B' claims tag 'U.Enable', which 'A' already claims. A tag has one claimant; remove one of the claims.`
  (`ComponentIds` `[B, A]`). A claim that passes all five is recorded, with its
  position. A repeat is reported once, as a repeat, and skips checks (1)–(4): so
  `["U.Enabel", "U.Enabel"]` is two `MR016`s, "does not have" at position 0 and
  "already claims" at position 1. Then, for
  each block, each `Writes` pin naming a tag another block claims —
  `Block 'B' commands tag 'U.Enable', which 'A' claims. Only the claiming block writes a claimed tag; remove the write from 'B', or this claim.`
  (`ComponentIds` `[B, A]`; the fix names both ends because a plant file reports
  it at the claimant's claim, R133) — once per tag, **skipping a tag the block itself
  listed in its claims**, so two blocks claiming one tag is one `MR016`, not
  two. The check is order-independent: a writer added before its claimant is
  still reported. "Read-write" and "writable" are the **binding's** access, as
  every other builder check (`MR014`'s commanded-tag check and `MR002`'s
  writable-port set) — so a claimed tag still satisfies its claimant's `Writes`
  and a required input (spec §2), and a writable declaration demoted by a signal
  link (R23) is read-only for check (2).
- **R136 — What may be claimed.** Any read-write tag in the claimant's `Writes`
  (criterion 1), including a block's command tag — another block's (a sequence
  that alone resets an interlock) or the block's own, when it lists it in its
  `Writes`. Spec §8's "no change to … block commands" is read as *unclaimed*
  commands, which are untouched. A claim names a tag by the name the directory
  publishes, so after an explicit `Bind` the claim uses the bound name and the
  declared name no longer exists (Review Focus 1).
- **R137 — Claim names.** Matched ordinally and exactly: `u.enable`,
  ` U.Enable` and `""` are "which the plant does not have" (`MR016`), not
  case-folded, trimmed or rejected by `TagNameRules` — a plant file must not be
  able to crash the loader through `AddScanBlock`. A `null` entry in the list is
  a programming error: `AddScanBlock` throws `ArgumentException`
  (`Block 'A' has a null claim. Name each claimed tag.`, parameter `claims`),
  which a plant file cannot reach (a non-string claim is `MR103`, R138). Because
  any string reaches Core's message, a claim containing `". "` would have been
  split inside its own quotes by the loader; R133's `Split` fix covers it
  (`ClaimTests` row `"FEED. Permit"`).
- **R138 — The schema's `claims`, and "unique within the entry".** Every block
  branch of the plant schema gains, after `scanPeriodMs`, an optional
  `"claims": { "description": …, "type": "array", "items": { "type": "string" } }`;
  `PlantSchemas.ControllerKeys` becomes `["id", "type", "scanPeriodMs", "claims", "parameters"]`
  in the same order (a new `PlantSchemaTests` fact pins the agreement). Spec §3's
  "strings, unique within the entry" is enforced by Core's `MR016` ("which it
  already claims", spec §2), **not** by `"uniqueItems"`: with `uniqueItems` the
  schema would reject a plant the loader reports as `MR016`, and
  `SchemaAgreementTests.TheSchemaRejectsStructuralErrorsAndOnlyThose` requires
  the schema to accept every non-structural (non-`MR101`–`MR103`) fixture.
  Likewise no `minLength`. A `claims` that is not an array, or an entry that is
  not a string, is structural: `MR103` from the loader at `…claims` /
  `…claims[j]`, rejected by the schema too (two new `BothValidatorsRejectTheSameControllerMistakes`
  rows). Measured: `plant.schema.json` +35 −0 (7 lines × 5 block branches).
- **R139 — `millrace tags --format json` names the claimant.** The spec specifies
  only the text form (`ToText()` appends `  claimed by <id>`). The JSON array
  gains `"claimedBy": "<id>"` after `"description"` on a claimed tag only — as
  `rangeLow`/`rangeHigh` appear only when a range exists — so every unclaimed
  tag's JSON is byte-identical and a tool generating an HMI can tell a claimed
  tag from a measured value. The command's help line in
  `src/Millrace.Cli/CommandTable.cs` becomes "Load and build a plant, then list its
  tags: name, kind, access, unit, range, description and claimant." (measured:
  no test, golden or doc pins the old help text).
- **R140 — Where `MR016` is documented, and what does not change.** Core codes
  have no rows in `ConfigDiagnostics.All`; `docs/configuration-diagnostics.md`
  documents them in the trailer rendered by `DiagnosticsReference`. The trailer's
  heading becomes `## MR001–MR016 — plant validation`, it gains the summary
  and fix for `MR016` and says where the loader puts it; the one existing test
  that pins the heading (`DiagnosticsReferenceTests.ThePlantValidationTrailerCoversTheBlockCodes`)
  changes with it. `docs/control-blocks.md`'s block-check table gains a `MR016`
  row (Task 6). Unchanged: `CommandOutcome` (a claimed tag is `ReadOnly`, the
  spec's decision), `CommandBus`, and the `MR206` explanation in
  `docs/scenario-diagnostics.md` ("writes a tag that is read-only" already
  covers a claimed tag, whose access is `ReadOnly`).
- **R141 — Documentation scope and the spec amendment.** Measured locations:
  the plant-file documentation of controller entries is
  `docs/control-blocks.md` § *In the plant file* (no other doc lists controller
  keys); Task 6 names `claims` in its key sentence, adds a § *Claiming a tag*
  after it, a `MR016` row to the *Attaching a block* table, rewrites the
  run-permit caveat, adds one paragraph to `docs/architecture.md` (the
  scan-block host) and brings its two `MR013`–`MR015` mentions to `MR016`,
  adds "unless a block claims one" to the root `README.md`'s sentence on
  scenarios writing block commands, and rewrites the sample README's "ordinary,
  writable tags" sentence and its "permit is not protected" limit. Three doc comments in `src/` that list the write refusals gain the claim
  (`ITagWriter`, `Simulation.WriteAt`, `ScanBlockRuntime`'s double-coil remark;
  Tasks 1–2). The controller adds this note to the spec, under its title, with
  the plan commit (as 6c did; no task edits the spec):

  ```markdown
  **Amended 2026-09-30 by the plan**
  (`docs/superpowers/plans/2026-09-30-block-claimed-tags.md`, rulings
  R133–R142), where the code forced a choice:

  - **A `MR016` lands on `claims[j]` through `ValidationError.Tag` and
    `ClaimIndex` (R133).** Core errors carry only ids, so `ValidationError`
    gains two init properties; the loader places every `MR016` on the claim it
    is about — for another block's write, on the claimant's accepted claim; for
    a repeat, on the repeat. A repeated claim is reported once, as a repeat
    (R135).
  - **The loader splits a `MR016` after the quoted tag (R133)**, so a claim
    containing `". "` does not cut its own message in two.
  - **The directory is given claimants by tag name (R134)**, not by index.
  - **Two blocks claiming one tag is one `MR016` (R135)**, not also a
    "commands a claimed tag" error for the second.
  - **"Unique within the entry" is `MR016`, not the schema's `uniqueItems`
    (R138)**, so the schema accepts every plant only the loader can reject.
  - **A block may claim a block command, another block's or its own (R136);**
    spec §8's "no change to … block commands" means unclaimed commands.
  - **Claim names match ordinally and exactly; a null claim throws
    `ArgumentException` from `AddScanBlock` (R137).**
  - **`millrace tags --format json` gains `"claimedBy"` on a claimed tag (R139)**,
    and `millrace tags`' help says it lists the claimant.
  ```
- **R142 — Existing tests that change, and new fixtures.** Measured by grep over
  `tests/` for `Permit`, `ReadWrite`, `ReadOnly`, `ToText`, tag counts, the
  controller keys and the `MR0xx` trailer: no existing test asserts a sample
  permit's access, and no directory text or tag count moves (the claim changes a
  tag's access and appends a suffix; it adds no tag). Exactly one existing test
  changes: `DiagnosticsReferenceTests.ThePlantValidationTrailerCoversTheBlockCodes`
  (heading `MR001–MR015` → `MR001–MR016`, plus two `Contains`). Two existing
  theories gain rows (`SchemaAgreementTests.BothValidatorsRejectTheSameControllerMistakes`,
  +2) or cases through a new corpus file
  (`CorpusTests.EveryInvalidPlantYieldsExactlyTheCodeInItsName` and
  `SchemaAgreementTests.TheSchemaRejectsStructuralErrorsAndOnlyThose`, +1 each,
  from `Plants/invalid/MR016-claim-on-a-tag-the-block-does-not-command.json`).
  Two goldens are regenerated (Task 3). New fixture files:
  `tests/Millrace.Configuration.Tests/Plants/invalid/MR016-claim-on-a-tag-the-block-does-not-command.json`
  and `tests/Millrace.Cli.Tests/Plants/claimed-permit.json` (the CLI's `Plants/`
  folder is copied by glob and no test enumerates it).

## Measurements

Scratch runs on `d26aae6` plus this plan's whole change, in a throwaway `git
worktree` (removed after).

**Suite:** 1408 tests, all passing; Release build `0 Warning(s)`, `0 Error(s)`.
Per project: 37 Io.Abstractions / 491 Core / 132 Components / 57 Realtime /
229 Configuration / 167 Scenarios / 78 Cli / 118 Control / 23 Control.Catalogue /
76 Samples.

**Criterion 8, the nine goldens.** With the sample's four `claims` added (and
nothing else in the sample), `EveryScenarioMatchesItsGolden` passes all nine
through `millrace run --expect`, `EveryScenarioReplaysByteForByteFromARecording`
passes all nine, and regenerating the nine with `MILLRACE_UPDATE_GOLDEN=1` leaves
`git status` clean under `samples/mine-conveyors/expected/`. Nothing moves
because no scenario and no other block writes a permit: each is written only by
its interlock (`… by INT_CVn.` / `… by INT_FEED.`), whose writes pass the
claimant check unchanged.

**`millrace tags samples/mine-conveyors/plant.json`**: still 124 lines; exactly four
end in a claimant:

```
CV001.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV001
CV002.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV002
CV003.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV003
Feed.Permit  Bool  ReadOnly  Run permit; false stops the feeder  claimed by INT_FEED
```

**Goldens that move:** `plant.schema.json` +35 −0 (one 7-line `claims` object in
each of `block.alarm`, `block.interlock`, `block.permissive`, `block.sequencer`,
`block.timer`, after `scanPeriodMs`); `docs/configuration-diagnostics.md`, the
trailer only (heading and one paragraph, shown in Task 3).

## File structure

```
src/Millrace.Io.Abstractions/TagDescriptor.cs         + ClaimedBy init property (Task 1)
src/Millrace.Io.Abstractions/ITagWriter.cs            doc comment: claimed tags refused (Task 1)
src/Millrace.Core/Io/TagDirectory.cs                  claimants parameter, ClaimantOf, ToText suffix (Task 1)
src/Millrace.Core/Io/TagImage.cs                      Check takes the origin; claim refusal (Task 1)
src/Millrace.Core/Simulation.cs                       WriteAt doc comment (Task 1)
src/Millrace.Core/Validation/ValidationError.cs       + Tag, ClaimIndex init properties (Task 2)
src/Millrace.Core/SimulationBuilder.cs                AddScanBlock(block, claims), CheckClaims (MR016), Build passes claimants (Task 2)
src/Millrace.Core/Control/ScanBlockRuntime.cs         double-coil remark (Task 2)
src/Millrace.Configuration/Loading/PlantSchemas.cs    ControllerKeys + "claims" (Task 3)
src/Millrace.Configuration/Loading/LoadState.cs       ControllerEntry.Claims (Task 3)
src/Millrace.Configuration/Loading/StructureStage.cs  ReadClaims (Task 3)
src/Millrace.Configuration/Loading/ControllerPass.cs  AddScanBlock(block, entry.Claims) (Task 3)
src/Millrace.Configuration/Loading/BuildStage.cs      Split after the quoted claim; PathOf: MR016 → claims[j] (Task 3)
src/Millrace.Configuration/PlantSchema.cs             "claims" in every block branch (Task 3)
src/Millrace.Configuration/DiagnosticsReference.cs    trailer MR001–MR016 (Task 3)
src/Millrace.Scenarios/ScenarioRunner.cs              claimed-tag bind message (Task 4)
src/Millrace.Cli/Commands/Tags.cs                     JSON "claimedBy" (Task 5)
src/Millrace.Cli/CommandTable.cs                      tags help line (Task 5)
tests/Millrace.Core.Tests/ClaimedTagTests.cs          new, 8 facts (Task 1)
tests/Millrace.Core.Tests/ClaimValidationTests.cs     new, 22 tests (Task 2)
tests/Millrace.Realtime.Tests/ClaimedTagCommandTests.cs   new, 1 fact (Task 2)
tests/Millrace.Configuration.Tests/ClaimTests.cs      new, 13 tests (Task 3)
tests/Millrace.Configuration.Tests/Plants/invalid/MR016-claim-on-a-tag-the-block-does-not-command.json   new (Task 3)
tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs   + 2 rows (Task 3)
tests/Millrace.Configuration.Tests/PlantSchemaTests.cs       + 1 fact (Task 3)
tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs   heading changes (Task 3)
tests/Millrace.Configuration.Tests/Golden/plant.schema.json  regenerated (Task 3)
docs/configuration-diagnostics.md                regenerated (Task 3)
tests/Millrace.Scenarios.Tests/ClaimedTagScenarioTests.cs   new, 4 facts (Task 4)
samples/mine-conveyors/plant.json                four interlocks gain "claims" (Task 5)
tests/Millrace.Samples.Tests/MineConveyorTests.cs     + 1 fact, + 1 theory of 2 (Task 5)
tests/Millrace.Cli.Tests/Plants/claimed-permit.json   new (Task 5)
tests/Millrace.Cli.Tests/TagsCommandTests.cs          + 2 facts (Task 5)
docs/control-blocks.md                           MR016 row, § Claiming a tag, permit caveat (Task 6)
docs/architecture.md                             one paragraph; MR016 in two lists (Task 6)
README.md                                        "unless a block claims one" (Task 6)
samples/mine-conveyors/README.md                 run-permit sentence, limits bullet (Task 6)
tests/Millrace.Control.Tests/DocumentationTests.cs    + 1 fact (Task 6)
tests/Millrace.Samples.Tests/SampleReadmeTests.cs     + 1 fact (Task 6)
```

## Task map

| # | Task | Implementer | Reviewer | Tests after |
|---|---|---|---|---|
| 1 | Descriptor `ClaimedBy`; directory claimants and `ToText`; image refuses all but the claimant | sonnet | opus (Core seam) | 1356 |
| 2 | `AddScanBlock(block, claims)`; `MR016`; end to end through `Simulation` and `CommandBus` | sonnet | opus (Core seam) | 1379 |
| 3 | Plant-file `claims`; schema; `MR016` at `claims[j]`; diagnostics page | sonnet | sonnet | 1397 |
| 4 | Scenario bind message for a claimed tag | sonnet | sonnet | 1401 |
| 5 | The sample's interlocks claim their permits; `millrace tags` JSON `claimedBy` and help | sonnet | sonnet | 1406 |
| 6 | `docs/control-blocks.md`, `docs/architecture.md`, root and sample READMEs | sonnet | sonnet | 1408 |

Every task's brief contains its complete code; Sonnet is named throughout
because every task edits existing files and the commit trailer must be right
first time (Haiku substituted its own model name in plans 3 and 4). Tasks are
sequential: each consumes the previous task's names.

---

### Task 1: The directory publishes a claim and the image enforces it

**Model:** implementer sonnet; reviewer opus.

**Files:**
- Modify: `src/Millrace.Io.Abstractions/TagDescriptor.cs` (Access param doc; new `ClaimedBy`)
- Modify: `src/Millrace.Io.Abstractions/ITagWriter.cs` (summary)
- Modify: `src/Millrace.Core/Io/TagDirectory.cs` (constructor, `ClaimantOf`, `ToText`)
- Modify: `src/Millrace.Core/Io/TagImage.cs` (`Write` ×2, `CheckWritable`, `Check`)
- Modify: `src/Millrace.Core/Simulation.cs` (`WriteAt` summary)
- Test: `tests/Millrace.Core.Tests/ClaimedTagTests.cs` (new, 8 facts)

**Interfaces:**
- Consumes: `TagBinding.Read/Write`, `TagImage.Prime`, `TagBinding.BindExternal`
  (existing, internal to `Millrace.Core`, visible to `Millrace.Core.Tests`).
- Produces:
  - `public string ClaimedBy { get; init; } = "";` on `Millrace.Io.TagDescriptor`.
  - `internal TagDirectory(IEnumerable<TagBinding> fullyNamed, IReadOnlyDictionary<string, string>? claimants = null)`
    — `claimants` maps tag name → claiming block id; a claimed tag's descriptor
    has `Access` `ReadOnly` and `ClaimedBy` the id.
  - `internal string? ClaimantOf(int index)` on `TagDirectory`.
  - `TagImage.Write(int, TagValue)`, `Write(string, TagValue)` and
    `CheckWritable(string, TagValue)` throw `InvalidOperationException`
    `Tag '<name>' is claimed by <id>; only that block writes it.` for a claimed
    tag; `internal Write(int, TagValue, string origin)` throws the same unless
    `origin` is the claimant. The read-only and kind checks run first, unchanged.
  - `TagDirectory.ToText()` appends `  claimed by <id>` after the description.

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Core.Tests/ClaimedTagTests.cs`:

```csharp
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

/// <summary>
/// Spec 6d §2, the runtime half: the directory publishes a claimed tag
/// read-only and names its claimant, and the image refuses every write to it
/// but the claimant's. Built straight from bindings, as <see cref="TagImageTests"/> is.
/// </summary>
public class ClaimedTagTests
{
    private sealed class Ports
    {
        public OutputPort<bool> Running { get; } = new("Running", "CV001");

        public InputPort<bool> Start { get; } = new("Start", "CV001", defaultValue: false, isRequired: false);

        public InputPort<bool> Permit { get; } = new("Permit", "CV001", defaultValue: true, isRequired: false);

        public InputPort<double> Rate { get; } = new("Rate", "Feed", defaultValue: 5.0, isRequired: false);

        /// <summary>CV001.Permit is claimed by INT01; nothing else is.</summary>
        public TagDirectory Directory(IReadOnlyDictionary<string, string>? claimants = null) => new(
            [
                TagBinding.Read("CV001.Running", Running, "Contactor closed"),
                TagBinding.Write("CV001.Start", Start, "Start command"),
                TagBinding.Write("CV001.Permit", Permit, "Run permit"),
                TagBinding.Write("Feed.Rate", Rate, "kg/s", 0.0, 20.0, "Feed rate"),
            ],
            claimants ?? new Dictionary<string, string>(StringComparer.Ordinal) { ["CV001.Permit"] = "INT01" });

        public TagImage Image()
        {
            TagDirectory directory = Directory();
            foreach (TagBinding binding in directory.Bindings)
            {
                binding.BindExternal();
            }

            var image = new TagImage(directory);
            image.Prime();
            return image;
        }
    }

    [Fact]
    public void AClaimedTagIsPublishedReadOnlyNamingItsClaimantAndEveryOtherTagNamesNone()
    {
        TagDirectory directory = new Ports().Directory();

        TagDescriptor permit = directory.Find("CV001.Permit");
        Assert.Equal((TagAccess.ReadOnly, "INT01"), (permit.Access, permit.ClaimedBy));
        Assert.Equal((TagKind.Bool, "Run permit"), (permit.Kind, permit.Description));

        Assert.Equal(
            new[] { ("CV001.Running", TagAccess.ReadOnly), ("CV001.Start", TagAccess.ReadWrite), ("Feed.Rate", TagAccess.ReadWrite) },
            directory.Tags.Where(t => t.ClaimedBy.Length == 0).Select(t => (t.Name, t.Access)));
    }

    [Fact]
    public void ADirectoryWithoutClaimsPublishesEveryTagAsItsBindingSays()
    {
        TagDirectory directory = new Ports().Directory(new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.All(directory.Tags, t => Assert.Equal(string.Empty, t.ClaimedBy));
        Assert.Equal(TagAccess.ReadWrite, directory.Find("CV001.Permit").Access);
    }

    [Fact]
    public void ToTextAppendsTheClaimantAfterTheDescription()
    {
        string[] lines = new Ports().Directory().ToText().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("CV001.Permit  Bool  ReadOnly  Run permit  claimed by INT01", lines[0]);
        Assert.Equal("CV001.Start  Bool  ReadWrite  Start command", lines[2]);
        Assert.Single(lines, l => l.Contains("claimed by", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExternalWriteToAClaimedTagIsRefusedByNameAndByIndexAndNothingIsQueued()
    {
        TagImage image = new Ports().Image();
        int index = image.Directory.Find("CV001.Permit").Index;

        InvalidOperationException byName = Assert.Throws<InvalidOperationException>(
            () => image.Write("CV001.Permit", TagValue.Bool(true)));
        InvalidOperationException byIndex = Assert.Throws<InvalidOperationException>(
            () => image.Write(index, TagValue.Bool(true)));
        InvalidOperationException check = Assert.Throws<InvalidOperationException>(
            () => image.CheckWritable("CV001.Permit", TagValue.Bool(true)));

        Assert.Equal("Tag 'CV001.Permit' is claimed by INT01; only that block writes it.", byName.Message);
        Assert.Equal(byName.Message, byIndex.Message);
        Assert.Equal(byName.Message, check.Message);
        Assert.Equal(0, image.PendingWrites);
    }

    [Fact]
    public void ABlockWriteFromAnotherOriginIsRefusedWithTheSameMessage()
    {
        TagImage image = new Ports().Image();
        int index = image.Directory.Find("CV001.Permit").Index;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => image.Write(index, TagValue.Bool(true), "SEQ01"));

        Assert.Equal("Tag 'CV001.Permit' is claimed by INT01; only that block writes it.", error.Message);
        Assert.Equal(0, image.PendingWrites);
    }

    [Fact]
    public void TheClaimantsWriteIsQueued()
    {
        TagImage image = new Ports().Image();
        int index = image.Directory.Find("CV001.Permit").Index;

        image.Write(index, TagValue.Bool(false), "INT01");

        Assert.Equal(1, image.PendingWrites);
    }

    [Fact]
    public void TheKindCheckRunsBeforeTheClaimCheck()
    {
        TagImage image = new Ports().Image();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => image.Write("CV001.Permit", TagValue.Double(1.0)));

        Assert.Equal("Tag 'CV001.Permit' is a Bool tag; cannot write a Double.", error.Message);
    }

    [Fact]
    public void UnclaimedTagsAcceptWritesExactlyAsBefore()
    {
        TagImage image = new Ports().Image();

        image.Write("CV001.Start", TagValue.Bool(true));
        image.Write(image.Directory.Find("Feed.Rate").Index, TagValue.Double(12.0), "INT01");

        Assert.Equal(2, image.PendingWrites);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => image.Write("CV001.Running", TagValue.Bool(true)));
        Assert.Equal("Tag 'CV001.Running' is read-only.", error.Message);
    }
}
```

(The directory sorts ordinally: `CV001.Permit`, `CV001.Running`, `CV001.Start`,
`Feed.Rate` — so `lines[0]` is the permit and `lines[2]` the start.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter "FullyQualifiedName~Millrace.Core.Tests.ClaimedTagTests"`
Expected: build FAILS — `CS1729` (`TagDirectory` does not contain a constructor
that takes 2 arguments) and `CS1061` (`TagDescriptor` does not contain a
definition for `ClaimedBy`).

- [ ] **Step 3: Add `ClaimedBy` to the descriptor**

In `src/Millrace.Io.Abstractions/TagDescriptor.cs`, replace

```csharp
/// <param name="Access">Whether external writes are accepted.</param>
```

with

```csharp
/// <param name="Access">Whether external writes are accepted. A tag a block claims is published <see cref="TagAccess.ReadOnly"/>.</param>
```

and replace

```csharp
    /// <summary>True when both range bounds are numbers.</summary>
```

with

```csharp
    /// <summary>
    /// The id of the control block that claims this tag — the only writer it
    /// accepts — or empty when no block claims it. A claimed tag's
    /// <see cref="Access"/> is <see cref="TagAccess.ReadOnly"/>.
    /// </summary>
    public string ClaimedBy { get; init; } = "";

    /// <summary>True when both range bounds are numbers.</summary>
```

In `src/Millrace.Io.Abstractions/ITagWriter.cs`, replace

```csharp
/// of the next tick; it is never applied on the caller's thread. Implementations
/// reject an unknown tag, a kind mismatch and a read-only tag by throwing at
/// the call site, so a caller never queues something that cannot land.
```

with

```csharp
/// of the next tick; it is never applied on the caller's thread. Implementations
/// reject an unknown tag, a kind mismatch, a read-only tag and a tag a control
/// block claims by throwing at the call site, so a caller never queues
/// something that cannot land.
```

- [ ] **Step 4: Give the directory its claimants**

In `src/Millrace.Core/Io/TagDirectory.cs`, replace the two fields and the constructor

```csharp
    private readonly TagDescriptor[] _descriptors;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    internal TagDirectory(IEnumerable<TagBinding> fullyNamed)
    {
        ArgumentNullException.ThrowIfNull(fullyNamed);

        Bindings = fullyNamed.OrderBy(b => b.Name, StringComparer.Ordinal).ToArray();
        _descriptors = new TagDescriptor[Bindings.Length];
        for (int i = 0; i < Bindings.Length; i++)
        {
            TagBinding b = Bindings[i];
            if (!_indexByName.TryAdd(b.Name, i))
            {
                throw new ArgumentException(
                    $"Tag '{b.Name}' is bound twice ('{Bindings[_indexByName[b.Name]].Port.QualifiedName}' and " +
                    $"'{b.Port.QualifiedName}'). Names must be unique.",
                    nameof(fullyNamed));
            }

            _descriptors[i] = new TagDescriptor(i, b.Name, b.Kind, b.Access, b.Unit, b.RangeLow, b.RangeHigh, b.Description);
        }
    }
```

with

```csharp
    private readonly TagDescriptor[] _descriptors;
    private readonly string?[] _claimants;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    /// <summary>
    /// Sorts and indexes the bindings. <paramref name="claimants"/> maps a tag
    /// name to the block that claims it; a claimed tag is published
    /// <see cref="TagAccess.ReadOnly"/> with <see cref="TagDescriptor.ClaimedBy"/>
    /// set, while its binding stays writable for the claimant.
    /// </summary>
    internal TagDirectory(IEnumerable<TagBinding> fullyNamed, IReadOnlyDictionary<string, string>? claimants = null)
    {
        ArgumentNullException.ThrowIfNull(fullyNamed);

        Bindings = fullyNamed.OrderBy(b => b.Name, StringComparer.Ordinal).ToArray();
        _descriptors = new TagDescriptor[Bindings.Length];
        _claimants = new string?[Bindings.Length];
        for (int i = 0; i < Bindings.Length; i++)
        {
            TagBinding b = Bindings[i];
            if (!_indexByName.TryAdd(b.Name, i))
            {
                throw new ArgumentException(
                    $"Tag '{b.Name}' is bound twice ('{Bindings[_indexByName[b.Name]].Port.QualifiedName}' and " +
                    $"'{b.Port.QualifiedName}'). Names must be unique.",
                    nameof(fullyNamed));
            }

            string? claimant = claimants is not null && claimants.TryGetValue(b.Name, out string? id) ? id : null;
            _claimants[i] = claimant;
            _descriptors[i] = new TagDescriptor(
                i, b.Name, b.Kind, claimant is null ? b.Access : TagAccess.ReadOnly, b.Unit, b.RangeLow, b.RangeHigh, b.Description)
            {
                ClaimedBy = claimant ?? string.Empty,
            };
        }
    }
```

Replace

```csharp
    /// <inheritdoc/>
    public int Count => _descriptors.Length;
```

with

```csharp
    /// <summary>The block that claims the tag at <paramref name="index"/>, or null when none does.</summary>
    internal string? ClaimantOf(int index) => _claimants[index];

    /// <inheritdoc/>
    public int Count => _descriptors.Length;
```

In `ToText()`, replace

```csharp
    /// <summary>One line per tag: name, kind, access, unit, range, description.</summary>
```

with

```csharp
    /// <summary>One line per tag: name, kind, access, unit, range, description and, for a claimed tag, its claimant.</summary>
```

and replace

```csharp
            if (tag.Description.Length > 0)
            {
                builder.Append("  ").Append(tag.Description);
            }
```

with

```csharp
            if (tag.Description.Length > 0)
            {
                builder.Append("  ").Append(tag.Description);
            }

            if (tag.ClaimedBy.Length > 0)
            {
                builder.Append("  claimed by ").Append(tag.ClaimedBy);
            }
```

- [ ] **Step 5: Make the image check the origin**

In `src/Millrace.Core/Io/TagImage.cs`, in `public void Write(int index, TagValue value)`
replace `Check(index, value);` with `Check(index, value, origin: null);`; in
`internal void Write(int index, TagValue value, string origin)` replace
`Check(index, value);` with `Check(index, value, origin);`; in `CheckWritable`
replace `Check(index, value);` with `Check(index, value, origin: null);`. Then
replace the whole `Check` method

```csharp
    private void Check(int index, TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _bindings.Length);

        TagBinding binding = _bindings[index];
        if (binding.Access != TagAccess.ReadWrite)
        {
            throw new InvalidOperationException($"Tag '{binding.Name}' is read-only.");
        }

        if (binding.Kind != value.Kind)
        {
            throw new InvalidOperationException(
                $"Tag '{binding.Name}' is a {binding.Kind} tag; cannot write a {value.Kind}.");
        }
    }
```

with

```csharp
    /// <summary>
    /// Refuses a write that cannot land: an index out of range, a read-only
    /// binding, a kind mismatch — and a claimed tag written by anything but its
    /// claimant. <paramref name="origin"/> is the writing block's id, or null
    /// for an external write.
    /// </summary>
    private void Check(int index, TagValue value, string? origin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _bindings.Length);

        TagBinding binding = _bindings[index];
        if (binding.Access != TagAccess.ReadWrite)
        {
            throw new InvalidOperationException($"Tag '{binding.Name}' is read-only.");
        }

        if (binding.Kind != value.Kind)
        {
            throw new InvalidOperationException(
                $"Tag '{binding.Name}' is a {binding.Kind} tag; cannot write a {value.Kind}.");
        }

        string? claimant = Directory.ClaimantOf(index);
        if (claimant is not null && !string.Equals(origin, claimant, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Tag '{binding.Name}' is claimed by {claimant}; only that block writes it.");
        }
    }
```

In `src/Millrace.Core/Simulation.cs`, in the `WriteAt` summary replace

```csharp
    /// The tag is resolved and checked now — unknown tag, read-only tag, kind
    /// mismatch — so a mistake fails here rather than mid-run, and the value
```

with

```csharp
    /// The tag is resolved and checked now — unknown tag, read-only tag, kind
    /// mismatch, a tag a block claims — so a mistake fails here rather than mid-run, and the value
```

(`WriteAt`/`WriteIn` reach the claim check through `WriteEvent.Create` →
`CheckWritable`; nothing else changes there.)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter "FullyQualifiedName~Millrace.Core.Tests.ClaimedTagTests"`
Expected: PASS, 8 tests.

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1356** (Core 469; every other
project unchanged). Nothing claims a tag yet outside the new test, so every
existing test is untouched.

- [ ] **Step 7: Commit**

```bash
git add src/Millrace.Io.Abstractions/TagDescriptor.cs src/Millrace.Io.Abstractions/ITagWriter.cs src/Millrace.Core/Io/TagDirectory.cs src/Millrace.Core/Io/TagImage.cs src/Millrace.Core/Simulation.cs tests/Millrace.Core.Tests/ClaimedTagTests.cs
git commit -F .superpowers/sdd/6d/msg-task1.txt
```

with `.superpowers/sdd/6d/msg-task1.txt` (written with the Write tool) holding:

```
feat(core): publish a claimed tag read-only and refuse every other writer

A tag directory built with claimants publishes each claimed tag ReadOnly,
with the new TagDescriptor.ClaimedBy naming the block, while the binding
stays writable for that block. TagImage.Check now takes the write's
origin: an external write, or a block write from anyone but the claimant,
is refused at the call site with "Tag 'X' is claimed by B; only that
block writes it." The read-only and kind checks run first, unchanged.
ToText appends "  claimed by B" after the description.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'` and check the subject, the blank
line and the trailer.

---

### Task 2: `AddScanBlock` takes claims; `MR016`

**Model:** implementer sonnet; reviewer opus.

**Files:**
- Modify: `src/Millrace.Core/Validation/ValidationError.cs` (+ `Tag`, `ClaimIndex`)
- Modify: `src/Millrace.Core/SimulationBuilder.cs` (field, `AddScanBlock`, `Validate` ×2, `Build`, new `CheckClaims`)
- Modify: `src/Millrace.Core/Control/ScanBlockRuntime.cs` (remark)
- Test: `tests/Millrace.Core.Tests/ClaimValidationTests.cs` (new, 22 tests)
- Test: `tests/Millrace.Realtime.Tests/ClaimedTagCommandTests.cs` (new, 1 fact)

**Interfaces:**
- Consumes (Task 1): `internal TagDirectory(IEnumerable<TagBinding>, IReadOnlyDictionary<string, string>?)`,
  `TagDescriptor.ClaimedBy`, the image's claim refusal.
- Produces:
  - `public SimulationBuilder AddScanBlock(IScanBlock block, IReadOnlyList<string>? claims = null)`
    — the one-argument call compiles unchanged; a `null` entry throws
    `ArgumentException` (`Block '<id>' has a null claim. Name each claimed tag.`, `ParamName` `claims`).
  - `public string Tag { get; init; } = "";` and `public int ClaimIndex { get; init; } = -1;`
    on `Millrace.Core.Validation.ValidationError` — set on every `MR016` to the
    claimed tag's name as spelled and to the claim's position in its block's
    list (the claimant's accepted claim for "another block commands"; R133).
  - `Validate()` reports `MR016` with the five messages of R135;
    `ComponentIds` is `[block]` for a claim's own failure and `[second, first]` /
    `[writer, claimant]` for the two conflicts.
  - `Build()` publishes each accepted claim (Task 1's descriptor and refusal).

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Core.Tests/ClaimValidationTests.cs`:

```csharp
using Millrace.Core.Contexts;
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Core.Logging;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Time;
using Millrace.Core.Validation;
using Millrace.Io;
using Xunit;

namespace Millrace.Core.Tests;

/// <summary>
/// Spec 6d §2, the builder half: <c>AddScanBlock(block, claims)</c>, MR016,
/// and a claimed tag seen end to end through a built <see cref="Simulation"/>.
/// </summary>
public class ClaimValidationTests
{
    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    /// <summary>Two thermostats and a fuse: T.Enable is read and U.Enable commanded; a fuse's Ok drives U.Enable in the demoted-link test.</summary>
    private static SimulationBuilder Plant() =>
        new SimulationBuilder(Options).Add(new Thermostat("T")).Add(new Thermostat("U")).Add(new Fuse("F"));

    /// <summary>Reads T.Enable, commands U.Enable, owns the command Cmd; every other tick.</summary>
    private static EchoBlock Block(string id) =>
        new EchoBlock(id, TimeSpan.FromMilliseconds(20)).Reads("T.Enable").MayWrite("U.Enable").Accepts("Cmd");

    private static ValidationError OnlyMr016(SimulationBuilder builder)
    {
        ValidationResult result = builder.Validate();
        Assert.False(result.IsValid);
        return Assert.Single(result.Errors, e => e.Code == "MR016");
    }

    /// <summary>A component with one required Bool input and a writable tag on it.</summary>
    private sealed class Needy : ComponentBase, ITagProvider
    {
        public Needy(string id)
            : base(id)
        {
            Go = AddInput<bool>("Go", required: true);
        }

        public InputPort<bool> Go { get; }

        public override void Evaluate(in TickContext ctx)
        {
        }

        public IEnumerable<TagBinding> DescribeTags() => [TagBinding.Write("Go", Go, "Run request")];
    }

    [Fact]
    public void AClaimOnATagTheBlockCommandsPublishesItReadOnlyNamingTheBlock()
    {
        Simulation sim = Plant().AddScanBlock(Block("A"), ["U.Enable"]).Build();

        TagDescriptor enable = sim.IO.Directory.Find("U.Enable");
        Assert.Equal((TagAccess.ReadOnly, "A"), (enable.Access, enable.ClaimedBy));
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("U.Setpoint").Access);
        Assert.Single(sim.IO.Directory.Tags, t => t.ClaimedBy.Length > 0);
    }

    [Fact]
    public void WithoutClaimsNoTagIsClaimed()
    {
        Simulation sim = Plant().AddScanBlock(Block("A")).Build();

        Assert.All(sim.IO.Directory.Tags, t => Assert.Equal(string.Empty, t.ClaimedBy));
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("U.Enable").Access);
    }

    [Fact]
    public void TheClaimantsWriteLandsAndLogsExactlyAsBefore()
    {
        EchoBlock block = Block("A");
        block.WriteOnce = true;
        Simulation sim = Plant().AddScanBlock(block, ["U.Enable"]).Build();

        sim.Tick();                                   // tick 0: the scan queues the write
        sim.Tick();                                   // tick 1: phase 1 applies it

        SimEventRecord record = Assert.Single(sim.Events.Records);
        Assert.Equal((1L, "U.Enable", "WRITE", "Set to true by A."), (record.Tick, record.Source, record.Code, record.Message));
        Assert.True(sim.IO.ReadBool("U.Enable"));
    }

    [Fact]
    public void AnExternalWriteAndAScheduledWriteToAClaimedTagAreRefusedAndNothingLands()
    {
        Simulation sim = Plant().AddScanBlock(Block("A"), ["U.Enable"]).Build();
        int index = sim.IO.Directory.Find("U.Enable").Index;
        const string Refusal = "Tag 'U.Enable' is claimed by A; only that block writes it.";

        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(() => sim.IO.Write("U.Enable", TagValue.Bool(true))).Message);
        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(() => sim.IO.Write(index, TagValue.Bool(true))).Message);
        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(
            () => sim.WriteAt(TimeSpan.FromMilliseconds(30), "U.Enable", TagValue.Bool(true))).Message);
        Assert.Equal(Refusal, Assert.Throws<InvalidOperationException>(
            () => sim.WriteIn(TimeSpan.FromMilliseconds(30), "U.Enable", TagValue.Bool(true))).Message);
        Assert.Equal(0, sim.IO.PendingWrites);

        sim.RunFor(TimeSpan.FromMilliseconds(100));

        Assert.Empty(sim.Events.Records);
        Assert.False(sim.IO.ReadBool("U.Enable"));
    }

    [Fact]
    public void AClaimedRequiredInputStillCountsAsDriven()
    {
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Needy("N"))
            .AddScanBlock(new EchoBlock("A", TimeSpan.FromMilliseconds(20)).MayWrite("N.Go"), ["N.Go"]);

        ValidationResult result = builder.Validate();

        Assert.True(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Equal("A", builder.Build().IO.Directory.Find("N.Go").ClaimedBy);
    }

    [Fact]
    public void AClaimNamingNoTagIsMr016()
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), ["U.Enabel"]));

        Assert.Equal(
            "Block 'A' claims tag 'U.Enabel', which the plant does not have. Check the name against 'millrace tags'; a block claims " +
            "a tag it commands.",
            error.Message);
        Assert.Equal(["A"], error.ComponentIds);
        Assert.Equal(("U.Enabel", 0), (error.Tag, error.ClaimIndex));
    }

    [Theory]
    [InlineData("u.enable")]
    [InlineData(" U.Enable")]
    [InlineData("")]
    public void AClaimIsMatchedOrdinallyAndExactly(string claim)
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), [claim]));

        Assert.StartsWith($"Block 'A' claims tag '{claim}', which the plant does not have.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClaimOnAReadOnlyTagIsMr016()
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A").MayWrite("A.Q").Publishes("Q"), ["A.Q"]));

        Assert.Equal(
            "Block 'A' claims tag 'A.Q', which is read-only. Claim a read-write tag the block commands, not a measured value, " +
            "a block's output or an input a signal link drives.",
            error.Message);
    }

    [Fact]
    public void AClaimOnAWritableTagDemotedByASignalLinkIsMr016()
    {
        var fuse = new Fuse("F");
        var heater = new Thermostat("U");
        fuse.Ok.ConnectTo(heater.Enable);
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T")).Add(heater).Add(fuse)
            .AddScanBlock(Block("A"), ["U.Enable"]);

        ValidationError error = OnlyMr016(builder);

        Assert.StartsWith("Block 'A' claims tag 'U.Enable', which is read-only.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClaimOnATagTheBlockDoesNotCommandIsMr016()
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), ["U.Setpoint"]));

        Assert.Equal(
            "Block 'A' claims tag 'U.Setpoint', which it does not command. Add the tag to the block's writes, or remove the claim.",
            error.Message);
    }

    [Fact]
    public void ATagClaimedTwiceByOneBlockIsOneMr016()
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), ["U.Enable", "U.Enable"]));

        Assert.Equal("Block 'A' claims tag 'U.Enable', which it already claims. Claim each tag once.", error.Message);
        Assert.Equal(("U.Enable", 1), (error.Tag, error.ClaimIndex));
    }

    [Fact]
    public void ARepeatedClaimOnAMissingTagIsReportedOnceAtEachPosition()
    {
        ValidationResult result = Plant().AddScanBlock(Block("A"), ["U.Enabel", "U.Enabel"]).Validate();

        Assert.Equal(
            new[]
            {
                ("Block 'A' claims tag 'U.Enabel', which the plant does not have.", 0),
                ("Block 'A' claims tag 'U.Enabel', which it already claims.", 1),
            },
            result.Errors.Where(e => e.Code == "MR016").Select(e => (e.Message[..(e.Message.IndexOf(". ", StringComparison.Ordinal) + 1)], e.ClaimIndex)));
    }

    [Fact]
    public void ATagClaimedByTwoBlocksIsOneMr016OnTheSecond()
    {
        ValidationError error = OnlyMr016(Plant().AddScanBlock(Block("A"), ["U.Enable"]).AddScanBlock(Block("B"), ["U.Enable"]));

        Assert.Equal(
            "Block 'B' claims tag 'U.Enable', which 'A' already claims. A tag has one claimant; remove one of the claims.",
            error.Message);
        Assert.Equal(["B", "A"], error.ComponentIds);
        Assert.Equal(0, error.ClaimIndex);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnotherBlockCommandingAClaimedTagIsMr016WhicheverWasAddedFirst(bool claimantFirst)
    {
        SimulationBuilder builder = Plant();
        if (claimantFirst)
        {
            builder.AddScanBlock(Block("A"), ["U.Enable"]).AddScanBlock(Block("B"));
        }
        else
        {
            builder.AddScanBlock(Block("B")).AddScanBlock(Block("A"), ["U.Enable"]);
        }

        ValidationError error = OnlyMr016(builder);

        Assert.Equal(
            "Block 'B' commands tag 'U.Enable', which 'A' claims. Only the claiming block writes a claimed tag; remove the " +
            "write from 'B', or this claim.",
            error.Message);
        Assert.Equal(["B", "A"], error.ComponentIds);
        Assert.Equal(("U.Enable", 0), (error.Tag, error.ClaimIndex));
    }

    [Fact]
    public void AnotherBlocksWritePointsAtTheClaimantsAcceptedClaimNotItsRepeat()
    {
        ValidationResult result = Plant()
            .AddScanBlock(Block("A"), ["U.Setpoint", "U.Enable", "U.Enable"]).AddScanBlock(Block("B")).Validate();

        Assert.Equal(
            new[] { ("A", 0), ("A", 2), ("B", 1) },
            result.Errors.Where(e => e.Code == "MR016").Select(e => (e.ComponentIds[0], e.ClaimIndex)));
    }

    [Fact]
    public void ABlockMayClaimAnotherBlocksCommandAndThenOnlyItWritesIt()
    {
        EchoBlock resetter = new EchoBlock("R", TimeSpan.FromMilliseconds(20)).MayWrite("A.Cmd");
        resetter.WriteOnce = true;
        Simulation sim = Plant().AddScanBlock(Block("A")).AddScanBlock(resetter, ["A.Cmd"]).Build();

        Assert.Equal((TagAccess.ReadOnly, "R"), (sim.IO.Directory.Find("A.Cmd").Access, sim.IO.Directory.Find("A.Cmd").ClaimedBy));
        Assert.Throws<InvalidOperationException>(() => sim.IO.Write("A.Cmd", TagValue.Bool(true)));

        sim.RunFor(TimeSpan.FromMilliseconds(20));

        Assert.Contains(sim.Events.Records, r => r.Source == "A.Cmd" && r.Message == "Set to true by R.");
    }

    [Fact]
    public void ABlockMayClaimItsOwnCommandWhenItCommandsIt()
    {
        Simulation sim = Plant().AddScanBlock(Block("A").MayWrite("A.Cmd"), ["A.Cmd"]).Build();

        Assert.Equal("A", sim.IO.Directory.Find("A.Cmd").ClaimedBy);
        Assert.Throws<InvalidOperationException>(() => sim.IO.Write("A.Cmd", TagValue.Bool(true)));
    }

    [Fact]
    public void AClaimFollowsTheNameAnExplicitBindGaveThePort()
    {
        var heater = new Thermostat("U");
        SimulationBuilder builder = new SimulationBuilder(Options)
            .Add(new Thermostat("T")).Add(heater)
            .Bind("Heater.Run", TagBinding.Write("Run", heater.Enable, "Heater run"))
            .AddScanBlock(new EchoBlock("A", TimeSpan.FromMilliseconds(20)).MayWrite("Heater.Run"), ["Heater.Run"]);

        Simulation sim = builder.Build();

        Assert.Equal("A", sim.IO.Directory.Find("Heater.Run").ClaimedBy);
        Assert.False(sim.IO.Directory.TryFind("U.Enable", out _));
    }

    [Fact]
    public void ANullClaimIsRefusedWhenTheBlockIsAdded()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Plant().AddScanBlock(Block("A"), ["U.Enable", null!]));

        Assert.Equal("claims", error.ParamName);
        Assert.StartsWith("Block 'A' has a null claim. Name each claimed tag.", error.Message, StringComparison.Ordinal);
    }
}
```

(22 tests: 17 facts, a 3-row theory and a 2-row theory. Timing, from the 5c
rule: `EchoBlock` scans at ticks 0, 2, …; `WriteOnce` queues its write on tick
0's scan and it lands at phase 1 of tick 1. In
`ABlockMayClaimAnotherBlocksCommandAndThenOnlyItWritesIt`, `RunFor(20 ms)` runs
ticks 0 and 1. `AClaimOnAReadOnlyTagIsMr016` and the demoted-link test also
produce a `MR014` — the block commands a read-only tag — which is why
`OnlyMr016` filters by code.)

Create `tests/Millrace.Realtime.Tests/ClaimedTagCommandTests.cs`:

```csharp
using Millrace.Components.Mechanical;
using Millrace.Core;
using Millrace.Core.Time;
using Millrace.Io;

namespace Millrace.Realtime.Tests;

/// <summary>Spec 6d: a remote client writing a tag a block claims is refused like any read-only tag.</summary>
public class ClaimedTagCommandTests
{
    /// <summary>Commands K1.Permit and nothing else; never writes it, which is all this test needs.</summary>
    private sealed class PermitHolder : IScanBlock
    {
        public string Id => "INT01";

        public TimeSpan ScanPeriod => TimeSpan.FromMilliseconds(100);

        public IReadOnlyList<TagRef> Inputs { get; } = [];

        public IReadOnlyList<TagRef> Writes { get; } = [new TagRef("K1.Permit", TagKind.Bool)];

        public IReadOnlyList<TagSpec> Outputs { get; } = [];

        public IReadOnlyList<TagSpec> Commands { get; } = [];

        public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
        {
        }
    }

    [Fact]
    public void ACommandToAClaimedTagIsReadOnlyAndNeverQueued()
    {
        Simulation sim = new SimulationBuilder(new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(10),
        }).Add(new MotorStarter("K1")).AddScanBlock(new PermitHolder(), ["K1.Permit"]).Build();
        var bus = new CommandBus(sim.IO);

        Assert.Equal(CommandOutcome.ReadOnly, bus.WriteBool("K1.Permit", true));
        Assert.Equal(CommandOutcome.Accepted, bus.WriteBool("K1.Command", true));

        Assert.Equal((1L, 1L), (bus.Accepted, bus.Rejected));
        Assert.Equal(1, sim.IO.PendingWrites);
        Assert.Equal("INT01", bus.Directory.Find("K1.Permit").ClaimedBy);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter "FullyQualifiedName~Millrace.Core.Tests.ClaimValidationTests"`
Expected: build FAILS — `CS1501` (no overload for method `AddScanBlock` takes 2
arguments) and `CS1061` (`ValidationError` does not contain a definition for `Tag`).

Run: `dotnet test tests/Millrace.Realtime.Tests --nologo --filter "FullyQualifiedName~ClaimedTagCommandTests"`
Expected: build FAILS — `CS1501` on `AddScanBlock`.

- [ ] **Step 3: Give `ValidationError` a tag and a claim position**

Replace the body of `src/Millrace.Core/Validation/ValidationError.cs` after its
summary,

```csharp
public sealed record ValidationError(
    string Code,
    string Message,
    IReadOnlyList<string> ComponentIds);
```

with

```csharp
public sealed record ValidationError(
    string Code,
    string Message,
    IReadOnlyList<string> ComponentIds)
{
    /// <summary>
    /// The claimed tag an MR016 is about, exactly as the claim spelled it, so a
    /// plant file can report it at the claim; empty for every other error.
    /// </summary>
    public string Tag { get; init; } = "";

    /// <summary>
    /// For an MR016, the position of the claim it is about in the list its
    /// block was added with — the block's own claim, or, when another block
    /// commands the tag, the claimant's; -1 for every other error.
    /// </summary>
    public int ClaimIndex { get; init; } = -1;
}
```

- [ ] **Step 4: Store claims and check them in the builder**

In `src/Millrace.Core/SimulationBuilder.cs`:

Replace

```csharp
    private readonly List<IScanBlock> _blocks = [];
```

with

```csharp
    private readonly List<IScanBlock> _blocks = [];
    private readonly List<string[]> _claims = [];
```

Replace the whole `AddScanBlock` method and its summary

```csharp
    /// <summary>
    /// Attaches a control block (spec 5c §3). The block is checked at
    /// <see cref="Build"/>: its period against the time step (MR013), its
    /// inputs and writes against the tag directory (MR014), and its id and
    /// owned tag names against everything else in the plant (MR015). Its
    /// outputs and commands become ordinary tags.
    /// </summary>
    public SimulationBuilder AddScanBlock(IScanBlock block)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(block);
        TagNameRules.Check(block.Id, nameof(block));
        _blocks.Add(block);
        return this;
    }
```

with

```csharp
    /// <summary>
    /// Attaches a control block (spec 5c §3). The block is checked at
    /// <see cref="Build"/>: its period against the time step (MR013), its
    /// inputs and writes against the tag directory (MR014), its id and
    /// owned tag names against everything else in the plant (MR015), and its
    /// claims (MR016). Its outputs and commands become ordinary tags.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="claims">
    /// Tags this block alone may write, by full name: each a read-write tag in
    /// the block's <see cref="IScanBlock.Writes"/>. A claimed tag is published
    /// read-only, naming the block; an external write to it is refused, and so
    /// is any other block that commands it (MR016).
    /// </param>
    public SimulationBuilder AddScanBlock(IScanBlock block, IReadOnlyList<string>? claims = null)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(block);
        TagNameRules.Check(block.Id, nameof(block));
        string[] claimed = claims is null ? [] : [.. claims];
        if (claimed.Any(c => c is null))
        {
            throw new ArgumentException($"Block '{block.Id}' has a null claim. Name each claimed tag.", nameof(claims));
        }

        _blocks.Add(block);
        _claims.Add(claimed);
        return this;
    }
```

Replace

```csharp
    public ValidationResult Validate() => Validate(out _, out _);
```

with

```csharp
    public ValidationResult Validate() => Validate(out _, out _, out _);
```

In `Build()`, replace

```csharp
        ValidationResult result = Validate(out List<TagBinding> tags, out List<ScanBlockPlan> blocks);
```

with

```csharp
        ValidationResult result = Validate(
            out List<TagBinding> tags, out List<ScanBlockPlan> blocks, out Dictionary<string, string> claimants);
```

and replace

```csharp
        var image = new TagImage(new TagDirectory(tags));
```

with

```csharp
        var image = new TagImage(new TagDirectory(tags, claimants));
```

Replace the private `Validate` signature

```csharp
    private ValidationResult Validate(out List<TagBinding> tags, out List<ScanBlockPlan> blocks)
    {
```

with

```csharp
    private ValidationResult Validate(
        out List<TagBinding> tags, out List<ScanBlockPlan> blocks, out Dictionary<string, string> claimants)
    {
```

and, in its body, replace

```csharp
        blocks = CollectBlocks(seen, tags, errors);
```

with

```csharp
        blocks = CollectBlocks(seen, tags, errors);
        claimants = CheckClaims(tags, errors);
```

Finally, insert this method immediately before
`/// <summary>The flow nodes among the added leaves, in registration order.</summary>`:

```csharp
    /// <summary>
    /// MR016 (spec 6d): each claim names a read-write tag its block commands
    /// and no other block has claimed; then no other block commands a claimed
    /// tag. Runs after every block's owned tags have joined
    /// <paramref name="tags"/>, so a block may claim another block's command.
    /// Every error carries the claim's tag and its position in its block's list.
    /// Returns the claims that passed, tag name to block id.
    /// </summary>
    private Dictionary<string, string> CheckClaims(List<TagBinding> tags, List<ValidationError> errors)
    {
        var claimants = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_blocks.Count == 0)
        {
            return claimants;
        }

        var byName = new Dictionary<string, TagBinding>(StringComparer.Ordinal);
        foreach (TagBinding binding in tags)
        {
            byName.TryAdd(binding.Name, binding);
        }

        // Where each accepted claim sits in its claimant's list, for the errors that point at it.
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int b = 0; b < _blocks.Count; b++)
        {
            IScanBlock block = _blocks[b];
            var own = new HashSet<string>(StringComparer.Ordinal);
            for (int j = 0; j < _claims[b].Length; j++)
            {
                string claim = _claims[b][j];
                ValidationError? error = null;
                if (!own.Add(claim))
                {
                    // A repeat is reported once, as a repeat, whatever else is wrong with the tag.
                    error = Claim(block.Id, claim, j, "which it already claims. Claim each tag once.");
                }
                else if (!byName.TryGetValue(claim, out TagBinding? binding))
                {
                    error = Claim(
                        block.Id, claim, j, "which the plant does not have. Check the name against 'millrace tags'; a block claims a tag it commands.");
                }
                else if (binding.Access != TagAccess.ReadWrite)
                {
                    error = Claim(
                        block.Id, claim, j, "which is read-only. Claim a read-write tag the block commands, not a measured value, " +
                        "a block's output or an input a signal link drives.");
                }
                else if (!block.Writes.Any(w => string.Equals(w.Name, claim, StringComparison.Ordinal)))
                {
                    error = Claim(
                        block.Id, claim, j, "which it does not command. Add the tag to the block's writes, or remove the claim.");
                }
                else if (claimants.TryGetValue(claim, out string? first))
                {
                    error = Claim(
                        block.Id, claim, j, $"which '{first}' already claims. A tag has one claimant; remove one of the claims.") with
                    {
                        ComponentIds = [block.Id, first],
                    };
                }
                else
                {
                    claimants[claim] = block.Id;
                    positions[claim] = j;
                }

                if (error is not null)
                {
                    errors.Add(error);
                }
            }
        }

        for (int b = 0; b < _blocks.Count; b++)
        {
            IScanBlock block = _blocks[b];

            // A block that claimed the tag itself was reported above if the claim failed; once is enough.
            var reported = new HashSet<string>(_claims[b], StringComparer.Ordinal);
            foreach (TagRef pin in block.Writes)
            {
                if (claimants.TryGetValue(pin.Name, out string? claimant)
                    && !string.Equals(claimant, block.Id, StringComparison.Ordinal)
                    && reported.Add(pin.Name))
                {
                    errors.Add(new ValidationError(
                        "MR016",
                        $"Block '{block.Id}' commands tag '{pin.Name}', which '{claimant}' claims. Only the claiming block " +
                        $"writes a claimed tag; remove the write from '{block.Id}', or this claim.",
                        [block.Id, claimant])
                    {
                        Tag = pin.Name,
                        ClaimIndex = positions[pin.Name],
                    });
                }
            }
        }

        return claimants;

        static ValidationError Claim(string blockId, string claim, int index, string rest) =>
            new("MR016", $"Block '{blockId}' claims tag '{claim}', {rest}", [blockId]) { Tag = claim, ClaimIndex = index };
    }

```

(The `MR002` writable-port set and `MR014`'s commanded-tag check read the
**bindings**, whose access a claim does not change — so a claimed tag still
satisfies a required input and its claimant's `Writes` (R135). `PlantTags()`
builds its directory without claimants and is unchanged.)

In `src/Millrace.Core/Control/ScanBlockRuntime.cs`, replace

```csharp
/// double coil — but, as on a PLC, it is usually a mistake. Every block write
```

with

```csharp
/// double coil — but, as on a PLC, it is usually a mistake, and a claimed tag
/// (MR016) has exactly one writer. Every block write
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Core.Tests --nologo --filter "FullyQualifiedName~Millrace.Core.Tests.ClaimValidationTests"`
Expected: PASS, 22 tests.

Run: `dotnet test tests/Millrace.Realtime.Tests --nologo --filter "FullyQualifiedName~ClaimedTagCommandTests"`
Expected: PASS, 1 test.

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1379** (Core 491, Realtime 57).

- [ ] **Step 6: Commit**

```bash
git add src/Millrace.Core/Validation/ValidationError.cs src/Millrace.Core/SimulationBuilder.cs src/Millrace.Core/Control/ScanBlockRuntime.cs tests/Millrace.Core.Tests/ClaimValidationTests.cs tests/Millrace.Realtime.Tests/ClaimedTagCommandTests.cs
git commit -F .superpowers/sdd/6d/msg-task2.txt
```

with `.superpowers/sdd/6d/msg-task2.txt`:

```
feat(core): let AddScanBlock claim tags and report MR016

AddScanBlock takes an optional list of tags the block alone may write.
Validate checks each claim after every block's owned tags exist: it must
name a read-write tag in the block's Writes, once, that no earlier block
claims; and no other block may command a claimed tag. Each breach is
MR016, carrying the claimed tag and the claim's position in the new
ValidationError.Tag and ClaimIndex, so a plant file can report it at the
claim; a repeated claim is reported once, as a repeat. Build hands the accepted claims to the
directory, so the claimant's writes land as before and every other writer,
the realtime CommandBus included, is refused.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 3: `claims` in the plant file, the schema and the diagnostics page

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `src/Millrace.Configuration/Loading/PlantSchemas.cs` (`ControllerKeys`)
- Modify: `src/Millrace.Configuration/Loading/LoadState.cs` (`ControllerEntry`)
- Modify: `src/Millrace.Configuration/Loading/StructureStage.cs` (`ReadController`, new `ReadClaims`)
- Modify: `src/Millrace.Configuration/Loading/ControllerPass.cs` (the add loop)
- Modify: `src/Millrace.Configuration/Loading/BuildStage.cs` (`Split`, `PathOf`)
- Modify: `src/Millrace.Configuration/PlantSchema.cs` (`WriteBlock`)
- Modify: `src/Millrace.Configuration/DiagnosticsReference.cs` (`ConfigurationTrailer`)
- Create: `tests/Millrace.Configuration.Tests/Plants/invalid/MR016-claim-on-a-tag-the-block-does-not-command.json`
- Test: `tests/Millrace.Configuration.Tests/ClaimTests.cs` (new, 13 tests)
- Test: `tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs` (+2 rows)
- Test: `tests/Millrace.Configuration.Tests/PlantSchemaTests.cs` (+1 fact)
- Test: `tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs` (one fact changes, R142)
- Regenerate: `tests/Millrace.Configuration.Tests/Golden/plant.schema.json`, `docs/configuration-diagnostics.md`

**Interfaces:**
- Consumes (Task 2): `AddScanBlock(IScanBlock, IReadOnlyList<string>?)`,
  `ValidationError.Tag`, `ValidationError.ClaimIndex`, the `MR016` messages of R135.
- Produces: a controller entry's optional `"claims"`, an array of strings,
  passed through; `MR016` at `$.controllers[i].claims[j]` (R133); `MR103` at
  `$.controllers[i].claims` (`"claims" must be an array of tag names.`) or
  `$.controllers[i].claims[j]` (`A claim is a tag's full name, as a string.`);
  `public IReadOnlyList<string> Claims { get; }` on the internal class
  `ControllerEntry` (as its other members are declared);
  `PlantSchemas.ControllerKeys` = `["id", "type", "scanPeriodMs", "claims", "parameters"]`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Configuration.Tests/ClaimTests.cs`:

```csharp
using Millrace.Core;
using Millrace.Io;

namespace Millrace.Configuration.Tests;

/// <summary>
/// Spec 6d §3: a controller entry's <c>claims</c> array is read in the
/// structure stage, handed to <c>AddScanBlock</c>, and every MR016 Core
/// reports lands on the claim it is about (R133).
/// </summary>
public class ClaimTests
{
    private const string Base = """
        {
          "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
          "materials": [ { "name": "ore", "kind": "bulk" } ],
          "components": [
            { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
            { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
            { "id": "PILE", "type": "bulk-sink" }
          ],
          "flows": [ { "from": "FEED.Out", "to": "CHUTE.In" }, { "from": "CHUTE.Out", "to": "PILE.In" } ],
          "controllers": [
            CONTROLLERS
          ]
        }
        """;

    /// <summary>An interlock that commands FEED.Enabled and FEED.Permit; <c>CLAIMS</c> is replaced by its claims entry.</summary>
    private const string Int01 =
        """{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100, CLAIMS"parameters": { "conditions": [ { "tag": "CHUTE.Full", "normal": false } ], "trip": [ { "tag": "FEED.Enabled", "value": false }, { "tag": "FEED.Permit", "value": false } ] } }""";

    /// <summary>A second interlock that commands FEED.Enabled only.</summary>
    private const string Int02 =
        """{ "id": "INT02", "type": "interlock", "scanPeriodMs": 100, CLAIMS"parameters": { "conditions": [ { "tag": "PILE.Full", "normal": false } ], "trip": [ { "tag": "FEED.Enabled", "value": false } ] } }""";

    private static string Claiming(string entry, string claims) =>
        entry.Replace("CLAIMS", claims.Length == 0 ? string.Empty : $"\"claims\": {claims}, ", StringComparison.Ordinal);

    private static string Plant(params string[] controllers) =>
        Base.Replace("CONTROLLERS", string.Join(",\n    ", controllers), StringComparison.Ordinal);

    [Fact]
    public void AClaimIsPassedToTheBuilderAndTheTagIsPublishedReadOnly()
    {
        LoadResult result = Plants.Load(Plant(Claiming(Int01, """[ "FEED.Permit" ]""")));

        Assert.True(result.IsValid, result.ToText());
        Simulation simulation = result.Builder!.Build();
        TagDescriptor permit = simulation.IO.Directory.Find("FEED.Permit");
        Assert.Equal((TagAccess.ReadOnly, "INT01"), (permit.Access, permit.ClaimedBy));
        Assert.Equal(string.Empty, simulation.IO.Directory.Find("FEED.Enabled").ClaimedBy);
    }

    [Fact]
    public void AnEntryWithoutClaimsAndOneWithAnEmptyListClaimNothing()
    {
        LoadResult result = Plants.Load(Plant(Claiming(Int01, string.Empty), Claiming(Int02, "[]")));

        Assert.True(result.IsValid, result.ToText());
        Assert.All(result.Builder!.Build().IO.Directory.Tags, t => Assert.Equal(string.Empty, t.ClaimedBy));
    }

    [Theory]
    [InlineData("""[ "FEED.Permt" ]""", "$.controllers[0].claims[0]", "Block 'INT01' claims tag 'FEED.Permt', which the plant does not have.")]
    [InlineData("""[ "FEED.Permit", "CHUTE.Level" ]""", "$.controllers[0].claims[1]", "Block 'INT01' claims tag 'CHUTE.Level', which is read-only.")]
    [InlineData("""[ "FEED.Rate" ]""", "$.controllers[0].claims[0]", "Block 'INT01' claims tag 'FEED.Rate', which it does not command.")]
    [InlineData("""[ "FEED.Permit", "FEED.Permit" ]""", "$.controllers[0].claims[1]", "Block 'INT01' claims tag 'FEED.Permit', which it already claims.")]
    [InlineData("""[ "FEED. Permit" ]""", "$.controllers[0].claims[0]", "Block 'INT01' claims tag 'FEED. Permit', which the plant does not have.")]
    public void AClaimCoreRefusesIsMr016AtTheClaim(string claims, string path, string message)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, claims)));

        Assert.Equal(("MR016", path, message), (d.Code, d.Path, d.Message));
    }

    [Fact]
    public void TheMessageIsSplitIntoSymptomAndFix()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, """[ "FEED.Permt" ]""")));
        ConfigDiagnostic spaced = Plants.Only(Plant(Claiming(Int01, """[ "FEED. Permit" ]""")));

        Assert.Equal("Check the name against 'millrace tags'; a block claims a tag it commands.", d.Fix);
        Assert.Equal(d.Fix, spaced.Fix);
    }

    [Fact]
    public void ATagClaimedByTwoControllersIsMr016AtTheSecondClaim()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, """[ "FEED.Enabled" ]"""), Claiming(Int02, """[ "FEED.Enabled" ]""")));

        Assert.Equal(("MR016", "$.controllers[1].claims[0]"), (d.Code, d.Path));
        Assert.Equal("Block 'INT02' claims tag 'FEED.Enabled', which 'INT01' already claims.", d.Message);
    }

    [Fact]
    public void AnotherControllerCommandingAClaimedTagIsMr016AtTheClaim()
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int02, string.Empty), Claiming(Int01, """[ "FEED.Permit", "FEED.Enabled" ]""")));

        Assert.Equal(("MR016", "$.controllers[1].claims[1]"), (d.Code, d.Path));
        Assert.Equal("Block 'INT02' commands tag 'FEED.Enabled', which 'INT01' claims.", d.Message);
        Assert.Equal("Only the claiming block writes a claimed tag; remove the write from 'INT02', or this claim.", d.Fix);
    }

    [Fact]
    public void ARepeatedClaimAndAnotherWriterAreEachReportedAtTheirOwnClaim()
    {
        LoadResult result = Plants.Load(Plant(Claiming(Int02, string.Empty), Claiming(Int01, """[ "FEED.Enabled", "FEED.Enabled" ]""")));

        Assert.Equal(
            new[]
            {
                ("$.controllers[1].claims[1]", "Block 'INT01' claims tag 'FEED.Enabled', which it already claims."),
                ("$.controllers[1].claims[0]", "Block 'INT02' commands tag 'FEED.Enabled', which 'INT01' claims."),
            },
            result.Diagnostics.Select(d => (d.Path, d.Message)));
    }

    [Theory]
    [InlineData("\"FEED.Permit\"", "$.controllers[0].claims", "\"claims\" must be an array of tag names.")]
    [InlineData("[ \"FEED.Permit\", 7 ]", "$.controllers[0].claims[1]", "A claim is a tag's full name, as a string.")]
    public void AClaimsListThatIsNotAnArrayOfStringsIsMr103(string claims, string path, string message)
    {
        ConfigDiagnostic d = Plants.Only(Plant(Claiming(Int01, claims)));

        Assert.Equal(("MR103", path, message), (d.Code, d.Path, d.Message));
    }
}
```

(13 tests: 6 facts, a 5-row theory and a 2-row theory. `FEED.Rate` is the bulk
source's writable rate — read-write, but not in `INT01`'s writes;
`CHUTE.Level` is a read-only output. In the two "another controller" facts
`INT02` is listed first and claims nothing, so R133's walk skips it and lands
on `INT01`'s accepted claim — `claims[0]` in the last fact, not the repeat at
`claims[1]`. The `"FEED. Permit"` row and the fix assertion beside it pin R133's
split: the symptom ends after the quoted claim, not inside it.)

Create `tests/Millrace.Configuration.Tests/Plants/invalid/MR016-claim-on-a-tag-the-block-does-not-command.json`:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
  "materials": [ { "name": "ore", "kind": "bulk" } ],
  "components": [
    { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
    { "id": "PILE", "type": "bulk-sink" }
  ],
  "flows": [ { "from": "FEED.Out", "to": "CHUTE.In" }, { "from": "CHUTE.Out", "to": "PILE.In" } ],
  "controllers": [
    { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
      "claims": [ "FEED.Permit" ],
      "parameters": {
        "conditions": [ { "tag": "CHUTE.Full", "normal": false } ],
        "trip": [ { "tag": "FEED.Enabled", "value": false } ] } }
  ]
}
```

(It feeds `CorpusTests.EveryInvalidPlantYieldsExactlyTheCodeInItsName` and
`SchemaAgreementTests.TheSchemaRejectsStructuralErrorsAndOnlyThose`, which
requires the schema to **accept** it: `MR016` is semantic, R138.)

In `tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs`, replace

```csharp
    [InlineData("\"id\": \"TMR01\"", "\"id\": \"TMR.01\"")]
    public void BothValidatorsRejectTheSameControllerMistakes(string from, string to)
```

with

```csharp
    [InlineData("\"id\": \"TMR01\"", "\"id\": \"TMR.01\"")]
    [InlineData("\"scanPeriodMs\": 100,", "\"scanPeriodMs\": 100, \"claims\": \"TMR01.Start\",")]
    [InlineData("\"scanPeriodMs\": 100,", "\"scanPeriodMs\": 100, \"claims\": [ 1 ],")]
    public void BothValidatorsRejectTheSameControllerMistakes(string from, string to)
```

In `tests/Millrace.Configuration.Tests/PlantSchemaTests.cs`, insert immediately
before `[Fact] public void ABlockBranchRequiresItsScanPeriodAndClosesItsParameters()`:

```csharp
    [Fact]
    public void EveryBlockBranchDeclaresTheControllerKeysAndAnOptionalListOfClaims()
    {
        using JsonDocument document = JsonDocument.Parse(Text);

        Assert.All(Plants.Catalogue.Blocks, block =>
        {
            JsonElement branch = Defs(document).GetProperty($"block.{block.Type}");
            JsonElement claims = branch.GetProperty("properties").GetProperty("claims");

            Assert.Equal(PlantSchemas.ControllerKeys, branch.GetProperty("properties").EnumerateObject().Select(p => p.Name));
            Assert.DoesNotContain(branch.GetProperty("required").EnumerateArray(), r => r.GetString() == "claims");
            Assert.Equal("array", claims.GetProperty("type").GetString());
            Assert.Equal("string", claims.GetProperty("items").GetProperty("type").GetString());
            Assert.False(claims.TryGetProperty("uniqueItems", out _));
        });
    }

```

In `tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs`, in
`ThePlantValidationTrailerCoversTheBlockCodes`, replace

```csharp
        Assert.Contains("## MR001–MR015 — plant validation\n", page, StringComparison.Ordinal);
        Assert.Contains("MR013", page, StringComparison.Ordinal);
        Assert.Contains("MR014", page, StringComparison.Ordinal);
        Assert.Contains("MR015", page, StringComparison.Ordinal);
```

with

```csharp
        Assert.Contains("## MR001–MR016 — plant validation\n", page, StringComparison.Ordinal);
        Assert.Contains("MR013", page, StringComparison.Ordinal);
        Assert.Contains("MR014", page, StringComparison.Ordinal);
        Assert.Contains("MR015", page, StringComparison.Ordinal);
        Assert.Contains("MR016", page, StringComparison.Ordinal);
        Assert.Contains("the `claims` entry it is about", page, StringComparison.Ordinal);
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo`
Expected: builds; **FAILS** — the 13 `ClaimTests` (the loader reports `MR101`,
unknown key `claims`, for every one), `CorpusTests.EveryInvalidPlantYieldsExactlyTheCodeInItsName("MR016-…")`
(`MR101`), `SchemaAgreementTests.TheSchemaRejectsStructuralErrorsAndOnlyThose("MR016-…")`
(the schema rejects the unknown key), `PlantSchemaTests.EveryBlockBranchDeclaresTheControllerKeysAndAnOptionalListOfClaims`
(`KeyNotFoundException`: no `claims`) and
`DiagnosticsReferenceTests.ThePlantValidationTrailerCoversTheBlockCodes`.
The two new `BothValidatorsRejectTheSameControllerMistakes` rows already
**pass** (both validators reject an unknown key today); they pin agreement after
the change. Report the failure count you see (measured on the prototype: 17
failed).

- [ ] **Step 3: Read `claims` in the structure stage**

In `src/Millrace.Configuration/Loading/PlantSchemas.cs`, replace

```csharp
    public static readonly string[] ControllerKeys = ["id", "type", "scanPeriodMs", "parameters"];
```

with

```csharp
    public static readonly string[] ControllerKeys = ["id", "type", "scanPeriodMs", "claims", "parameters"];
```

In `src/Millrace.Configuration/Loading/LoadState.cs`, replace

```csharp
internal sealed class ControllerEntry(
    int index, string id, BlockDescriptor descriptor, TimeSpan scanPeriod, JsonElement parameters, ParameterValues checkedValues)
{
```

with

```csharp
internal sealed class ControllerEntry(
    int index,
    string id,
    BlockDescriptor descriptor,
    TimeSpan scanPeriod,
    JsonElement parameters,
    ParameterValues checkedValues,
    IReadOnlyList<string> claims)
{
```

and, in the same class, replace

```csharp
    public string Path => $"$.controllers[{Index}]";

    public string ParametersPath => $"{Path}.parameters";
}

internal sealed record LinkEntry(string From, string To, string Path);
```

with

```csharp
    /// <summary>The <c>claims</c> array, in file order; empty when the entry has none.</summary>
    public IReadOnlyList<string> Claims { get; } = claims;

    public string Path => $"$.controllers[{Index}]";

    public string ParametersPath => $"{Path}.parameters";
}

internal sealed record LinkEntry(string From, string To, string Path);
```

In `src/Millrace.Configuration/Loading/StructureStage.cs`, in `ReadController`,
replace

```csharp
        TimeSpan? period = ReadScanPeriod(state, element, path);

        JsonElement parameters = default;
```

with

```csharp
        TimeSpan? period = ReadScanPeriod(state, element, path);
        string[]? claims = ReadClaims(state, element, path);

        JsonElement parameters = default;
```

and replace

```csharp
        if (id is not null && period is { } scanPeriod && values is not null)
        {
            state.Controllers.Add(new ControllerEntry(index, id, descriptor, scanPeriod, parameters, values));
        }
    }
```

with

```csharp
        if (id is not null && period is { } scanPeriod && values is not null && claims is not null)
        {
            state.Controllers.Add(new ControllerEntry(index, id, descriptor, scanPeriod, parameters, values, claims));
        }
    }

    /// <summary>
    /// Spec 6d: an optional array of tag names, read as written. Whether each
    /// names a tag the block commands, and is claimed once, is Core's MR016.
    /// </summary>
    private static string[]? ReadClaims(LoadState state, JsonElement element, string path)
    {
        const string Fix = "Write \"claims\": [ \"CV001.Permit\" ], naming tags the block commands by their full names.";
        if (!element.TryGetProperty("claims", out JsonElement claims))
        {
            return [];
        }

        if (claims.ValueKind != JsonValueKind.Array)
        {
            state.Error(ConfigDiagnostics.BadParameter, $"{path}.claims", "\"claims\" must be an array of tag names.", Fix);
            return null;
        }

        var names = new List<string>();
        bool ok = true;
        int index = 0;
        foreach (JsonElement claim in claims.EnumerateArray())
        {
            if (claim.ValueKind == JsonValueKind.String)
            {
                names.Add(claim.GetString()!);
            }
            else
            {
                state.Error(
                    ConfigDiagnostics.BadParameter,
                    string.Create(CultureInfo.InvariantCulture, $"{path}.claims[{index}]"),
                    "A claim is a tag's full name, as a string.",
                    Fix);
                ok = false;
            }

            index++;
        }

        return ok ? [.. names] : null;
    }
```

(`ConfigDiagnostics.BadParameter` is `MR103`; `System.Globalization` is
already imported. `ControllerEntry` is constructed nowhere else — measured.)

- [ ] **Step 4: Pass the claims to Core and place its `MR016`**

In `src/Millrace.Configuration/Loading/ControllerPass.cs`, replace

```csharp
        var blocks = new List<IScanBlock>(bound.Count);
        foreach ((ControllerEntry entry, ParameterValues values) in bound)
        {
            if (TryBuild(state, entry, values, out IScanBlock? block) && PinsMatch(state, entry, block, declared[entry]))
            {
                blocks.Add(block);
            }
        }
```

with

```csharp
        var blocks = new List<(ControllerEntry Entry, IScanBlock Block)>(bound.Count);
        foreach ((ControllerEntry entry, ParameterValues values) in bound)
        {
            if (TryBuild(state, entry, values, out IScanBlock? block) && PinsMatch(state, entry, block, declared[entry]))
            {
                blocks.Add((entry, block));
            }
        }
```

and replace

```csharp
        // File order is scan order, and the later block wins a same-tick write.
        foreach (IScanBlock block in blocks)
        {
            builder.AddScanBlock(block);
        }
```

with

```csharp
        // File order is scan order, and the later block wins a same-tick write. Claims are Core's to check (MR016).
        foreach ((ControllerEntry entry, IScanBlock block) in blocks)
        {
            builder.AddScanBlock(block, entry.Claims);
        }
```

In `src/Millrace.Configuration/Loading/BuildStage.cs`, add `using System.Globalization;`
as the first line (before `using Millrace.Core;`); in `Run`, replace

```csharp
            (string message, string fix) = Split(error.Message);
```

with

```csharp
            (string message, string fix) = Split(error);
```

and replace everything from `    /// <summary>Core messages read "symptom. fix." (R40).</summary>`
to the end of the file (the `Split` and `PathOf` methods and the class's closing
brace) with

```csharp
    /// <summary>
    /// Core messages read "symptom. fix." (R40). An MR016 quotes its claim's tag
    /// in the symptom, and a claim is any string (R137), so the search for the
    /// cut starts after the quoted tag.
    /// </summary>
    private static (string Message, string Fix) Split(ValidationError error)
    {
        string text = error.Message;
        int from = 0;
        if (error.Tag.Length > 0)
        {
            int quoted = text.IndexOf($"'{error.Tag}'", StringComparison.Ordinal);
            from = quoted < 0 ? 0 : quoted + error.Tag.Length + 2;
        }

        int cut = text.IndexOf(". ", from, StringComparison.Ordinal);
        return cut < 0
            ? (text, "Correct the plant so that this check passes.")
            : (text[..(cut + 1)], text[(cut + 2)..]);
    }

    private static string PathOf(LoadState state, ValidationError error)
    {
        string? fallback = null;
        foreach (string id in error.ComponentIds)
        {
            int dot = id.IndexOf('.', StringComparison.Ordinal);
            string top = dot < 0 ? id : id[..dot];
            ComponentEntry? entry = state.Components.FirstOrDefault(c => string.Equals(c.Id, top, StringComparison.Ordinal));
            if (entry is not null)
            {
                return entry.Path;
            }

            ControllerEntry? controller = state.Controllers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (controller is null)
            {
                continue;
            }

            // R92: a scan period off the step is a block check with a key of its own.
            if (string.Equals(error.Code, "MR013", StringComparison.Ordinal))
            {
                return $"{controller.Path}.scanPeriodMs";
            }

            // R133: a claim's diagnostic lands on the claim — the entry of the controller whose list holds the tag at ClaimIndex.
            if (string.Equals(error.Code, "MR016", StringComparison.Ordinal))
            {
                int claim = error.ClaimIndex;
                if (claim >= 0 && claim < controller.Claims.Count && string.Equals(controller.Claims[claim], error.Tag, StringComparison.Ordinal))
                {
                    return string.Create(CultureInfo.InvariantCulture, $"{controller.Path}.claims[{claim}]");
                }

                fallback ??= controller.Path;
                continue;
            }

            return controller.Path;
        }

        return fallback ?? "$";
    }
}
```

(Every other code keeps its old path and its old split: for an error with an
empty `Tag` the search starts at 0 as before, and a controller match still
returns at once.)

- [ ] **Step 5: Declare `claims` in the schema and document `MR016`**

In `src/Millrace.Configuration/PlantSchema.cs`, in `WriteBlock`, replace

```csharp
        writer.WriteNumber("maximum", 86_400_000);
        writer.WriteEndObject();

        writer.WritePropertyName("parameters");
        WriteParameterObject(writer, block.Parameters, description: null, typeConst: null);
```

with

```csharp
        writer.WriteNumber("maximum", 86_400_000);
        writer.WriteEndObject();

        writer.WriteStartObject("claims");
        writer.WriteString(
            "description",
            "Tags only this block may write, by full name: each a read-write tag the block commands. A client, a scenario or another block that writes one is refused.");
        writer.WriteString("type", "array");
        writer.WriteStartObject("items");
        writer.WriteString("type", "string");
        writer.WriteEndObject();
        writer.WriteEndObject();

        writer.WritePropertyName("parameters");
        WriteParameterObject(writer, block.Parameters, description: null, typeConst: null);
```

In `src/Millrace.Configuration/DiagnosticsReference.cs`, replace the whole
`ConfigurationTrailer` constant

```csharp
    private const string ConfigurationTrailer =
        "## MR001–MR015 — plant validation\n\n" +
        "Codes below MR100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n" +
        "duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n" +
        "flow links, tag conflicts — and, for a control block declared under `controllers` or attached with\n" +
        "`AddScanBlock`, a scan period that is not a positive whole number of time steps (MR013), a pin naming a\n" +
        "tag the plant does not have, publishes with another kind or will not accept a command (MR014), and a\n" +
        "block id or owned tag name that collides with something the plant already has (MR015). The numbering\n" +
        "skips 012. The loader passes them through with the path of the first component involved, or of the\n" +
        "controller (for MR013, its `scanPeriodMs`); their message is split at its first sentence into message\n" +
        "and fix. A plant file's controllers are resolved before the blocks are validated, so MR113–MR115\n" +
        "report what MR014 would. See `docs/architecture.md` and `docs/control-blocks.md`.\n";
```

with

```csharp
    private const string ConfigurationTrailer =
        "## MR001–MR016 — plant validation\n\n" +
        "Codes below MR100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:\n" +
        "duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible\n" +
        "flow links, tag conflicts — and, for a control block declared under `controllers` or attached with\n" +
        "`AddScanBlock`, a scan period that is not a positive whole number of time steps (MR013), a pin naming a\n" +
        "tag the plant does not have, publishes with another kind or will not accept a command (MR014), a\n" +
        "block id or owned tag name that collides with something the plant already has (MR015), and a claim\n" +
        "that cannot stand (MR016): it names no tag, a read-only tag or a tag its block does not command, it\n" +
        "repeats, another block already claims the tag, or another block commands a claimed tag. Fix an MR016 by\n" +
        "correcting the claim's tag name, or by removing the claim or the other block's write. The numbering\n" +
        "skips 012. The loader passes them through with the path of the first component involved, or of the\n" +
        "controller (for MR013, its `scanPeriodMs`; for MR016, the `claims` entry it is about); their message\n" +
        "is split at its first sentence into message and fix. A plant file's controllers are resolved before the\n" +
        "blocks are validated, so MR113–MR115 report what MR014 would. See `docs/architecture.md` and\n" +
        "`docs/control-blocks.md`.\n";
```

- [ ] **Step 6: Regenerate the two goldens and read them**

Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~PlantSchemaTests.MatchesTheGoldenFile"`
Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Configuration.Tests --nologo --filter "FullyQualifiedName~DiagnosticsReferenceTests.TheCommittedReferencePageIsCurrent"`

Then read `git diff --stat` and the whole `git diff` of both files. Expected,
and quote it in the report:
- `tests/Millrace.Configuration.Tests/Golden/plant.schema.json` — **+35 −0**: five
  identical hunks, one per block branch (`alarm`, `interlock`, `permissive`,
  `sequencer`, `timer`), each inserting, between the `scanPeriodMs` object and
  `"parameters"`:

  ```json
          "claims": {
            "description": "Tags only this block may write, by full name: each a read-write tag the block commands. A client, a scenario or another block that writes one is refused.",
            "type": "array",
            "items": {
              "type": "string"
            }
          },
  ```
- `docs/configuration-diagnostics.md` — the trailer only: the heading line
  `## MR001–MR015 — plant validation` becomes `## MR001–MR016 — plant
  validation`, and the paragraph becomes the new constant's text (from "tag the
  plant does not have, publishes with another kind…" to the end). No line above
  the trailer changes.

If either diff shows anything else, stop and report it.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Configuration.Tests --nologo`
Expected: PASS, **229** (211 + 13 `ClaimTests` + 2 corpus cases + 2 schema
rows + 1 `PlantSchemaTests` fact).

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1397**.

- [ ] **Step 8: Commit**

```bash
git add src/Millrace.Configuration/Loading/PlantSchemas.cs src/Millrace.Configuration/Loading/LoadState.cs src/Millrace.Configuration/Loading/StructureStage.cs src/Millrace.Configuration/Loading/ControllerPass.cs src/Millrace.Configuration/Loading/BuildStage.cs src/Millrace.Configuration/PlantSchema.cs src/Millrace.Configuration/DiagnosticsReference.cs tests/Millrace.Configuration.Tests/ClaimTests.cs tests/Millrace.Configuration.Tests/Plants/invalid/MR016-claim-on-a-tag-the-block-does-not-command.json tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs tests/Millrace.Configuration.Tests/PlantSchemaTests.cs tests/Millrace.Configuration.Tests/DiagnosticsReferenceTests.cs tests/Millrace.Configuration.Tests/Golden/plant.schema.json docs/configuration-diagnostics.md
git commit -F .superpowers/sdd/6d/msg-task3.txt
```

with `.superpowers/sdd/6d/msg-task3.txt`:

```
feat(config): read a controller's claims and report MR016 at the claim

A controller entry takes an optional "claims" array of tag names, which
the loader passes to AddScanBlock. The schema declares it on every block
branch as an array of strings; a claims value that is not one is MR103
from both validators. Core's MR016 lands on $.controllers[i].claims[j],
the claim it is about, through ValidationError.Tag and ClaimIndex, and is
split into message and fix after the quoted claim, so a claim holding ". "
cannot cut its own message. The diagnostics page documents MR016 in its
plant-validation trailer.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 4: A scenario that writes a claimed tag is told who owns it

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `src/Millrace.Scenarios/ScenarioRunner.cs` (`Bind`)
- Test: `tests/Millrace.Scenarios.Tests/ClaimedTagScenarioTests.cs` (new, 4 facts)

**Interfaces:**
- Consumes (Tasks 1–3): `TagDescriptor.ClaimedBy`; a plant file's `claims`.
- Produces: `MR206` at `$.timeline[i].write` with message
  `Tag '<name>' is claimed by <id>; a scenario cannot write it.` and fix
  `Write the claiming block's inputs instead — for an interlock's permit, its reset.`,
  checked before the read-only check, before tick 0.

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Scenarios.Tests/ClaimedTagScenarioTests.cs`:

```csharp
using Millrace.Components;
using Millrace.Configuration;
using Millrace.Control.Catalogue;
using Millrace.Core;
using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Scenarios.Tests;

/// <summary>Spec 6d §4: a scenario cannot write a tag a block claims, and is told which block and what to write instead.</summary>
public class ClaimedTagScenarioTests
{
    private static readonly ComponentCatalogue Catalogue =
        new CatalogueBuilder().Add<ComponentsModule>().Add<ControlModule>().Build();

    /// <summary>A feeder whose interlock takes its permit away when the chute fills; <c>CLAIMS</c> is its claims entry.</summary>
    private const string Plant = """
        {
          "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
          "materials": [ { "name": "ore", "kind": "bulk" } ],
          "components": [
            { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
            { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
            { "id": "PILE", "type": "bulk-sink" }
          ],
          "flows": [ { "from": "FEED.Out", "to": "CHUTE.In" }, { "from": "CHUTE.Out", "to": "PILE.In" } ],
          "controllers": [
            { "id": "INT01", "type": "interlock", "scanPeriodMs": 100, CLAIMS
              "parameters": {
                "conditions": [ { "tag": "CHUTE.Full", "normal": false } ],
                "trip": [ { "tag": "FEED.Permit", "value": false } ],
                "reset": [ { "tag": "FEED.Permit", "value": true } ] } }
          ]
        }
        """;

    private static string Claimed => Plant.Replace("CLAIMS", "\"claims\": [ \"FEED.Permit\" ],", StringComparison.Ordinal);

    private static string Unclaimed => Plant.Replace("CLAIMS", string.Empty, StringComparison.Ordinal);

    private static ScenarioRunResult Run(string plantJson, string timeline)
    {
        ScenarioParseResult parsed = ScenarioLoader.Parse(
            $$"""{ "plant": "plant.json", "duration": 10, "timeline": [ {{timeline}} ] }""");
        Assert.NotNull(parsed.Scenario);
        return ScenarioRunner.Run(parsed.Scenario, plantJson, Catalogue);
    }

    [Fact]
    public void WritingAClaimedTagDoesNotBindAndNamesTheClaimant()
    {
        ScenarioRunResult result = Run(Claimed, """{ "at": 1, "write": "FEED.Permit", "value": true }""");

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("MR206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal("Tag 'FEED.Permit' is claimed by INT01; a scenario cannot write it.", d.Message);
        Assert.Equal("Write the claiming block's inputs instead — for an interlock's permit, its reset.", d.Fix);
        Assert.Null(result.Events);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void TheSameWriteBindsOnThePlantWithoutTheClaim()
    {
        ScenarioRunResult result = Run(Unclaimed, """{ "at": 1, "write": "FEED.Permit", "value": true }""");

        Assert.True(result.IsValid, result.ToText());
        Assert.Contains("FEED.Permit  WRITE  Set to true.", result.Events!.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclaimedTagBesideAClaimedOneStillBinds()
    {
        ScenarioRunResult result = Run(Claimed, """{ "at": 1, "write": "FEED.Enabled", "value": false }""");

        Assert.True(result.IsValid, result.ToText());
    }

    [Fact]
    public void ARecordingThatWroteATagNowClaimedDoesNotReplay()
    {
        LoadResult load = PlantLoader.Load(Unclaimed, Catalogue);
        Simulation live = load.Builder!.Build();
        var recorder = new ScenarioRecorder();
        live.AttachActionRecorder(recorder);
        live.WriteAt(TimeSpan.FromSeconds(1), "FEED.Permit", TagValue.Bool(true));
        live.RunFor(TimeSpan.FromSeconds(10));
        Scenario recorded = recorder.ToScenario("plant.json", load.Options!, TimeSpan.FromSeconds(10));

        ScenarioRunResult replay = ScenarioRunner.Run(recorded, Claimed, Catalogue);

        ConfigDiagnostic d = Assert.Single(replay.Diagnostics);
        Assert.Equal("Tag 'FEED.Permit' is claimed by INT01; a scenario cannot write it.", d.Message);
        Assert.Null(replay.Events);
    }
}
```

(The corpus helper `Corpus.Run` uses a components-only catalogue, so this class
carries its own, as `ControlledRecordAndReplayTests` does.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo --filter "FullyQualifiedName~ClaimedTagScenarioTests"`
Expected: FAIL, 2 of 4 — `WritingAClaimedTagDoesNotBindAndNamesTheClaimant`
and `ARecordingThatWroteATagNowClaimedDoesNotReplay`, each with the message
`Tag 'FEED.Permit' is read-only; a scenario cannot write it.` (the descriptor is
already `ReadOnly` after Task 3). The other two pass. (Measured.)

- [ ] **Step 3: Name the claimant in the bind check**

In `src/Millrace.Scenarios/ScenarioRunner.cs`, in `Bind`, replace

```csharp
        if (tag.Access != TagAccess.ReadWrite)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
```

with

```csharp
        if (tag.ClaimedBy.Length > 0)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
                ScenarioDiagnostics.DoesNotBind,
                $"{path}.write",
                $"Tag '{tag.Name}' is claimed by {tag.ClaimedBy}; a scenario cannot write it.",
                "Write the claiming block's inputs instead — for an interlock's permit, its reset."));
            return;
        }

        if (tag.Access != TagAccess.ReadWrite)
        {
            diagnostics.Add(ScenarioDiagnostics.Error(
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Scenarios.Tests --nologo`
Expected: PASS, **167**.

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1401**.

- [ ] **Step 5: Commit**

```bash
git add src/Millrace.Scenarios/ScenarioRunner.cs tests/Millrace.Scenarios.Tests/ClaimedTagScenarioTests.cs
git commit -F .superpowers/sdd/6d/msg-task4.txt
```

with `.superpowers/sdd/6d/msg-task4.txt`:

```
feat(scenarios): name the claimant when a scenario writes a claimed tag

A claimed tag is already read-only to a scenario; the bind check now says
which block claims it and what to write instead ("for an interlock's
permit, its reset"). It still fails before tick 0, so a recording made
before a tag was claimed does not replay against the claimed plant and
leaves no partial log.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 5: The sample's interlocks claim their permits; `millrace tags` JSON names the claimant

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `samples/mine-conveyors/plant.json` (four interlock entries)
- Modify: `src/Millrace.Cli/Commands/Tags.cs` (JSON `claimedBy`, R139)
- Modify: `src/Millrace.Cli/CommandTable.cs` (the `tags` help line, R139)
- Create: `tests/Millrace.Cli.Tests/Plants/claimed-permit.json`
- Test: `tests/Millrace.Cli.Tests/TagsCommandTests.cs` (+2 facts)
- Test: `tests/Millrace.Samples.Tests/MineConveyorTests.cs` (+1 fact, +1 theory of 2 rows)

**Interfaces:**
- Consumes (Tasks 1–4): the plant file's `claims`, `ToText`'s `  claimed by`,
  the scenario bind message.
- Produces: the sample's `CV001.Permit`, `CV002.Permit`, `CV003.Permit`,
  `Feed.Permit` claimed by `INT_CV001`, `INT_CV002`, `INT_CV003`, `INT_FEED`;
  `millrace tags --format json` objects gain `"claimedBy"` on a claimed tag only;
  `millrace tags`' help names the description and the claimant.

- [ ] **Step 1: Write the failing tests**

Create `tests/Millrace.Cli.Tests/Plants/claimed-permit.json`:

```json
{
  "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
  "materials": [
    { "name": "ore", "kind": "bulk", "properties": { "density": 2000, "moisture": 0.03, "temperature": 15 } }
  ],
  "components": [
    { "id": "FEED", "type": "bulk-source", "parameters": { "material": "ore", "rateKgPerS": 20 } },
    { "id": "CHUTE", "type": "transfer-chute", "parameters": { "capacityKg": 200 } },
    { "id": "PILE", "type": "bulk-sink" }
  ],
  "flows": [
    { "from": "FEED.Out", "to": "CHUTE.In" },
    { "from": "CHUTE.Out", "to": "PILE.In" }
  ],
  "controllers": [
    { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
      "claims": [ "FEED.Permit" ],
      "parameters": {
        "conditions": [ { "tag": "CHUTE.Full", "normal": false } ],
        "trip": [ { "tag": "FEED.Permit", "value": false } ],
        "reset": [ { "tag": "FEED.Permit", "value": true } ] } }
  ]
}
```

In `tests/Millrace.Cli.Tests/TagsCommandTests.cs`, insert immediately before
`[Fact] public void AnEmptyPathIsExitThreeNotACrash()`:

```csharp
    [Fact]
    public void AClaimedTagIsListedReadOnlyWithItsClaimant()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("claimed-permit.json"));

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Empty(run.Err);
        Assert.Contains("FEED.Permit  Bool  ReadOnly  Run permit; false stops the feeder  claimed by INT01\n", run.Out, StringComparison.Ordinal);
        Assert.Single(run.Out.Split('\n'), line => line.Contains("claimed by", StringComparison.Ordinal));
    }

    [Fact]
    public void JsonFormatNamesTheClaimantOnlyOnAClaimedTag()
    {
        CliRun run = Cli.Run("tags", Cli.Plant("claimed-permit.json"), "--format", "json");

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        using JsonDocument document = JsonDocument.Parse(run.Out);
        JsonElement permit = document.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "FEED.Permit");
        Assert.Equal("readOnly", permit.GetProperty("access").GetString());
        Assert.Equal("INT01", permit.GetProperty("claimedBy").GetString());

        JsonElement enabled = document.RootElement.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "FEED.Enabled");
        Assert.Equal("readWrite", enabled.GetProperty("access").GetString());
        Assert.False(enabled.TryGetProperty("claimedBy", out _));
    }

```

In `tests/Millrace.Samples.Tests/MineConveyorTests.cs`, insert immediately before
`[Fact] public void ATraceCannotSampleFasterThanTheTimeStep()`:

```csharp
    [Fact]
    public void EveryPermitIsReadOnlyAndClaimedByItsInterlock()
    {
        CliRun run = Cli.Run("tags", Sample.Plant);

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        string[] claimed = run.Out.Split('\n').Where(l => l.Contains("  claimed by ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(
            [
                "CV001.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV001",
                "CV002.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV002",
                "CV003.Permit  Bool  ReadOnly  Run permit; false holds the contactor open  claimed by INT_CV003",
                "Feed.Permit  Bool  ReadOnly  Run permit; false stops the feeder  claimed by INT_FEED",
            ],
            claimed);
    }

    [Theory]
    [InlineData("CV001.Permit", "INT_CV001")]
    [InlineData("Feed.Permit", "INT_FEED")]
    public void AScenarioThatWritesAPermitIsRefusedBeforeTickZero(string permit, string interlock)
    {
        Scenario scenario = ScenarioLoader.Parse(
            $$"""{ "plant": "../plant.json", "duration": 150, "timeline": [ { "at": 100, "write": "{{permit}}", "value": true } ] }""").Scenario!;

        ScenarioRunResult result = ScenarioRunner.Run(scenario, File.ReadAllText(Sample.Plant), Sample.Catalogue);

        ConfigDiagnostic d = Assert.Single(result.Diagnostics);
        Assert.Equal(("MR206", "$.timeline[0].write"), (d.Code, d.Path));
        Assert.Equal($"Tag '{permit}' is claimed by {interlock}; a scenario cannot write it.", d.Message);
        Assert.Null(result.Events);
    }

```

(`MineConveyorTests.cs` already imports `Millrace.Cli`, `Millrace.Configuration` and
`Millrace.Scenarios`. The scenario writes at 100 s, the pull-key scenario's refused
start time — 6c's bypass — but no new scenario file is added: a refused scenario
has no log to keep as a golden, spec §5.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Cli.Tests --nologo --filter "FullyQualifiedName~TagsCommandTests"`
Expected: FAIL, 1 — `JsonFormatNamesTheClaimantOnlyOnAClaimedTag`
(`KeyNotFoundException` on `claimedBy`). `AClaimedTagIsListedReadOnlyWithItsClaimant`
already passes (Tasks 1 and 3 make the text form).

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~EveryPermitIsReadOnlyAndClaimedByItsInterlock|FullyQualifiedName~AScenarioThatWritesAPermitIsRefusedBeforeTickZero"`
Expected: FAIL, 3 — no line is claimed yet, and each scenario binds and runs
(`Assert.Single` on an empty collection).

- [ ] **Step 3: Claim the four permits and add `claimedBy` to the JSON**

In `samples/mine-conveyors/plant.json`, add one line after each of the four
interlock headers — replace

```json
    { "id": "INT_CV003", "type": "interlock", "scanPeriodMs": 100,
      "parameters": {
```

with

```json
    { "id": "INT_CV003", "type": "interlock", "scanPeriodMs": 100,
      "claims": [ "CV003.Permit" ],
      "parameters": {
```

and likewise `INT_CV002` → `"claims": [ "CV002.Permit" ],`, `INT_CV001` →
`"claims": [ "CV001.Permit" ],`, `INT_FEED` → `"claims": [ "Feed.Permit" ],`,
each on its own line, indented six spaces, between the header line and
`      "parameters": {`. Nothing else in the file changes (`git diff --stat` on it:
`4 insertions(+)`).

In `src/Millrace.Cli/Commands/Tags.cs`, replace

```csharp
                writer.WriteString("description", tag.Description);
                writer.WriteEndObject();
```

with

```csharp
                writer.WriteString("description", tag.Description);
                if (tag.ClaimedBy.Length > 0)
                {
                    writer.WriteString("claimedBy", tag.ClaimedBy);
                }

                writer.WriteEndObject();
```

In `src/Millrace.Cli/CommandTable.cs`, in the `tags` entry, replace

```csharp
"Load and build a plant, then list its tags: name, kind, access, unit, range."
```

with

```csharp
"Load and build a plant, then list its tags: name, kind, access, unit, range, description and claimant."
```

(Measured: no test, golden or document pins the old help text —
`grep -rn "list its tags" tests docs README.md` finds nothing.)

- [ ] **Step 4: Prove the nine goldens do not move**

Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~EveryScenarioMatchesItsGolden"`
Then run `git status --short samples/`. Expected: exactly
` M samples/mine-conveyors/plant.json` — no golden under `expected/` is
modified (measured: all nine regenerate byte-identical, criterion 8). If any
golden changed, stop: read its `git diff`, report it, and do not commit.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Cli.Tests --nologo` — expect PASS, **78**.
Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, **75**
(`EveryScenarioMatchesItsGolden`, `EveryScenarioTellsItsStory` and
`EveryScenarioReplaysByteForByteFromARecording` all pass on the claimed plant;
`TheLoaderReportsNothingAtAllNotEvenAWarning` and
`TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt` pass with `claims`).

Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1406**.

- [ ] **Step 6: Commit**

```bash
git add samples/mine-conveyors/plant.json src/Millrace.Cli/Commands/Tags.cs src/Millrace.Cli/CommandTable.cs tests/Millrace.Cli.Tests/Plants/claimed-permit.json tests/Millrace.Cli.Tests/TagsCommandTests.cs tests/Millrace.Samples.Tests/MineConveyorTests.cs
git commit -F .superpowers/sdd/6d/msg-task5.txt
```

with `.superpowers/sdd/6d/msg-task5.txt`:

```
feat(samples): claim each run permit for its interlock

INT_CV001, INT_CV002, INT_CV003 and INT_FEED claim CV001.Permit,
CV002.Permit, CV003.Permit and Feed.Permit. millrace tags lists each as
ReadOnly, claimed by its interlock, and a scenario that writes one is
refused before tick 0, closing the bypass 6c documented. Nothing else
writes a permit, so all nine goldens are unchanged, byte for byte.
millrace tags --format json names the claimant as "claimedBy" on a claimed
tag only, and the command's help says it lists the claimant.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

### Task 6: Documentation

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `docs/control-blocks.md` (key sentence; table row; new § *Claiming a tag*; run-permit caveat)
- Modify: `docs/architecture.md` (one paragraph; `MR016` in two lists)
- Modify: `README.md` (the sentence on scenarios writing block commands)
- Modify: `samples/mine-conveyors/README.md` (run-permit sentence; limits bullet)
- Test: `tests/Millrace.Control.Tests/DocumentationTests.cs` (+1 fact)
- Test: `tests/Millrace.Samples.Tests/SampleReadmeTests.cs` (+1 fact)

**Interfaces:**
- Consumes: every name and message from Tasks 1–5, verbatim.
- Produces: documentation only.

- [ ] **Step 1: Write the failing tests**

In `tests/Millrace.Control.Tests/DocumentationTests.cs`, insert immediately before
`/// <summary>Two levels up from this file is the repository root.</summary>`:

```csharp
    [Fact]
    public void TheControlBlocksPageDescribesClaimedTags()
    {
        string page = Page();

        foreach (string token in new[]
                 {
                     "### Claiming a tag", "\"claims\": [ \"CV001.Permit\" ]", "AddScanBlock(interlock, [\"CV001.Permit\"])",
                     "ClaimedBy", "claimed by INT01", "Tag 'CV001.Permit' is claimed by INT01; only that block writes it.",
                     "MR016", "duplicate-coil", "it may also list `claims` (*Claiming a tag*, below)",
                 })
        {
            Assert.Contains(token, page, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("The permit is an ordinary, writable tag", page, StringComparison.Ordinal);
    }

```

In `tests/Millrace.Samples.Tests/SampleReadmeTests.cs`, insert immediately before
`[Theory] [MemberData(nameof(Sample.Scenarios), MemberType = typeof(Sample))] public void EveryQuotedLineIsAWholeLineOfItsGolden(string name)`:

```csharp
    [Fact]
    public void TheReadmeSaysEachPermitIsClaimedByItsInterlock()
    {
        string readme = Readme;

        Assert.Contains("each interlock claims its device's permit", readme, StringComparison.Ordinal);
        Assert.Contains("Tag 'CV001.Permit' is claimed by INT_CV001; a scenario cannot write it.", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("ordinary, writable tags", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("The permit is not protected", readme, StringComparison.Ordinal);
    }

```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Millrace.Control.Tests --nologo --filter "FullyQualifiedName~DocumentationTests"`
Expected: FAIL, 1 — `TheControlBlocksPageDescribesClaimedTags` (no
`### Claiming a tag`).

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~SampleReadmeTests"`
Expected: FAIL, 1 — `TheReadmeSaysEachPermitIsClaimedByItsInterlock`.

- [ ] **Step 3: `docs/control-blocks.md`**

In § *In the plant file*, replace the key sentence

```markdown
Each entry has the component envelope — `id`, `type`, `parameters` — plus
`scanPeriodMs`, which is required: a PLC task period has no sensible default.
```

with

```markdown
Each entry has the component envelope — `id`, `type`, `parameters` — plus
`scanPeriodMs`, which is required: a PLC task period has no sensible default;
it may also list `claims` (*Claiming a tag*, below).
```

In the *Attaching a block* table, replace

```markdown
| MR015 | The block id is unique across components and blocks, and no owned tag name collides with an existing tag. |
```

with

```markdown
| MR015 | The block id is unique across components and blocks, and no owned tag name collides with an existing tag. |
| MR016 | Every claim names a read-write tag in the block's `Writes`, once; no two blocks claim one tag; no other block's `Writes` names a claimed tag. |
```

At the end of § *In the plant file*, replace

```markdown
**File order is scan order.** Blocks due on the same tick scan in the order the
`controllers` array lists them, and when two write one tag on one tick the later
one wins.

## The timing rule
```

with

````markdown
**File order is scan order.** Blocks due on the same tick scan in the order the
`controllers` array lists them, and when two write one tag on one tick the later
one wins.

### Claiming a tag

A block may **claim** a plant tag it writes: a claimed tag is written by that
block and by nothing else, as a PLC program's permit bit is written by its own
rung and never by the HMI. A controller entry lists its claims in an optional
`"claims"` array of full tag names; in code, `AddScanBlock` takes the same list.

```json
{ "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
  "claims": [ "CV001.Permit" ],
  "parameters": {
    "conditions": [ { "tag": "CV001.Tripped", "normal": false } ],
    "trip":  [ { "tag": "CV001.Start", "value": false }, { "tag": "CV001.Permit", "value": false } ],
    "reset": [ { "tag": "CV001.Permit", "value": true }, { "tag": "CV001.Start", "value": false } ] } }
```

```csharp
builder.AddScanBlock(interlock, ["CV001.Permit"]);
```

The claimant's own writes land and log exactly as before — `Set to true by
INT01.` Every other writer is refused before anything is queued. The directory
publishes the tag `ReadOnly` with `ClaimedBy` set to the block's id, and
`millrace tags` prints it as `CV001.Permit  Bool  ReadOnly  …  claimed by INT01`, so an
OPC UA server or an HMI generator sees an ordinary read-only tag. `TagImage.Write`,
`Simulation.WriteAt` and `WriteIn` throw
`Tag 'CV001.Permit' is claimed by INT01; only that block writes it.`; the
realtime `CommandBus` answers `ReadOnly`; a scenario that writes it does not bind
(`MR206`, naming the claimant). Another block whose writes name a claimed tag
fails validation, like a PLC's duplicate-coil check. There is no force or
override. A claim must name a read-write tag the block commands — not a measured
value, another block's output, or an input a signal link drives — and each tag
has one claimant; every breach is `MR016`, reported in a plant file at the
`claims` entry it is about. A block may claim another block's command (a
sequence that alone resets an interlock), or its own.

## The timing rule
````

In § *Holding a device off: the run permit*, replace

```markdown
`Permit` and `Set to false by INT01.` for the command. The mine-conveyor sample
uses this pattern on every interlock. The permit is an ordinary, writable tag
like any other: nothing stops another block from writing it true while the
interlock is still tripped, which defeats the inhibit until the interlock trips
again.
```

with

```markdown
`Permit` and `Set to false by INT01.` for the command. Claim the permit for the
interlock (*Claiming a tag*, above): unclaimed, it is an ordinary writable tag,
and anything that writes it true while the interlock is still tripped defeats
the inhibit until the interlock trips again. The mine-conveyor sample uses this
pattern on every interlock, and each interlock claims its device's permit.
```

- [ ] **Step 4: `docs/architecture.md` and the root `README.md`**

In the controllers paragraph of the configuration section, replace

```markdown
are then added in file order and pass `Validate()`'s `MR013`–`MR015` like a
```

with

```markdown
are then added in file order and pass `Validate()`'s `MR013`–`MR016` like a
```

In the scan-block host section, replace

```markdown
the period, `MR014` for the pins, `MR015` for the names — and turns its
declared outputs and commands into ordinary tags over ordinary ports: an output
is an `OutputPort<T>` behind a read-only binding, a command an `InputPort<T>`
behind a writable one. Nothing in `Millrace.Realtime` or the scenario recorder had to
```

with

```markdown
the period, `MR014` for the pins, `MR015` for the names, `MR016` for its
claims — and turns its declared outputs and commands into ordinary tags over
ordinary ports: an output is an `OutputPort<T>` behind a read-only binding, a
command an `InputPort<T>` behind a writable one. Nothing in `Millrace.Realtime` or
the scenario recorder had to
```

and, a few lines further on, replace

```markdown
attributes a block's write and the recorder skips it. `Simulation` then
schedules one
self-rescheduling `ScanEvent` per block, first due at tick 0, drained in phase 1
in schedule order.
```

with

```markdown
attributes a block's write and the recorder skips it. A block added with
claims (`AddScanBlock(block, claims)`, `MR016`) is its claimed tags' only
writer: the binding stays writable, the directory publishes the tag `ReadOnly`
with `ClaimedBy` set, and `TagImage` refuses a write whose origin is not the
claimant — so every consumer that already honours `ReadOnly` refuses it too.
`Simulation` then schedules one self-rescheduling `ScanEvent` per block, first
due at tick 0, drained in phase 1 in schedule order.
```

In the root `README.md`, replace

```markdown
A plant adds control blocks under `controllers`; a scenario may write their
commands (`SEQ01.Start`, `INT01.Reset`) like any other tag:
```

with

```markdown
A plant adds control blocks under `controllers`; a scenario may write their
commands (`SEQ01.Start`, `INT01.Reset`) like any other tag, unless a block
claims one:
```

- [ ] **Step 5: `samples/mine-conveyors/README.md`**

In **Run permits.**, replace

```markdown
`Start` or `Enabled`, from anywhere, starts its device — `CVn.Permit` and
`Feed.Permit` are themselves ordinary, writable tags, but only the interlock
should ever write them. The reset
```

with

```markdown
`Start` or `Enabled`, from anywhere, starts its device. Nor can anything else
give the permit back: each interlock claims its device's permit
(`"claims": [ "CV001.Permit" ]`), so `millrace tags` lists `CVn.Permit` and
`Feed.Permit` as `ReadOnly … claimed by INT_…`, and a write from an HMI, a
scenario or another block is refused — as an HMI has no write access to a PLC
program's permit bit. The reset
```

In the limits list, replace the last bullet

```markdown
- The permit is not protected from other writers: `CVn.Permit` and
  `Feed.Permit` are ordinary tags, and nothing here stops another block or
  scenario from writing one true while its interlock is still tripped. Doing
  so defeats the inhibit, like forcing a permit bit in a PLC, and the
  interlock will not take it away again until it trips again. On a real
  system an HMI would have no write access to it; block-owned, write-protected
  tags would be an engine change.
```

with

```markdown
- A claimed permit cannot be forced: there is no override path for
  commissioning or fault-finding, as a PLC's force table would give. A
  scenario that writes `CV001.Permit` is refused before tick 0
  (`Tag 'CV001.Permit' is claimed by INT_CV001; a scenario cannot write it.`),
  so the way to move a device during a trip is the one the plant offers: put
  the fault right and reset its interlock.
```

(No quoted log block moves — the goldens did not change — so
`EveryQuotedLineIsAWholeLineOfItsGolden` is unaffected.)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Millrace.Control.Tests --nologo` — expect PASS, **118**.
Run: `dotnet test tests/Millrace.Samples.Tests --nologo` — expect PASS, **76**.
Run: `dotnet build Millrace.sln -c Release --nologo` — expect `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — expect **1408**: 37 / 491 / 132 / 57 /
229 / 167 / 78 / 118 / 23 / 76.

- [ ] **Step 7: Commit**

```bash
git add docs/control-blocks.md docs/architecture.md README.md samples/mine-conveyors/README.md tests/Millrace.Control.Tests/DocumentationTests.cs tests/Millrace.Samples.Tests/SampleReadmeTests.cs
git commit -F .superpowers/sdd/6d/msg-task6.txt
```

with `.superpowers/sdd/6d/msg-task6.txt`:

```
docs: describe claimed tags and say each sample permit is claimed

docs/control-blocks.md gains "Claiming a tag" (the plant-file "claims"
array, AddScanBlock's list, what is refused and how) and an MR016 row,
and the run-permit caveat now says to claim the permit. The architecture
page says how the scan-block host enforces a claim and lists MR016 with
the other block checks; the root README says a scenario cannot write a
claimed block command. The sample README
replaces "ordinary, writable tags" and the "permit is not protected"
limit: each interlock claims its permit, and a claimed permit cannot be
forced.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Then `git log -1 --format='%h%n%s%n--%n%b'`.

---

## Spec coverage

| Spec criterion / section | Task | Tests |
|---|---|---|
| 1. A block may claim a read-write tag in its `Writes`; its writes land and log as before | 2, 5 | `ClaimValidationTests.AClaimOnATagTheBlockCommandsPublishesItReadOnlyNamingTheBlock`, `TheClaimantsWriteLandsAndLogsExactlyAsBefore`; `ClaimedTagTests.TheClaimantsWriteIsQueued`; `MineConveyorTests.EveryScenarioMatchesItsGolden` (nine, unchanged) |
| 2. `TagImage.Write(int\|string)` refuses a claimed tag with the message, nothing queued; `WriteAt`/`WriteIn` via `CheckWritable` | 1, 2 | `ClaimedTagTests.AnExternalWriteToAClaimedTagIsRefusedByNameAndByIndexAndNothingIsQueued`; `ClaimValidationTests.AnExternalWriteAndAScheduledWriteToAClaimedTagAreRefusedAndNothingLands` |
| 3. A block write whose origin is not the claimant is refused (internal API) | 1 | `ClaimedTagTests.ABlockWriteFromAnotherOriginIsRefusedWithTheSameMessage` |
| 4. `MR016`: no tag; not read-write (incl. demoted); not in `Writes`; claimed by two; another block writes it; a repeat reported once | 2, 3 | `ClaimValidationTests.AClaimNamingNoTagIsMr016`, `AClaimIsMatchedOrdinallyAndExactly`, `AClaimOnAReadOnlyTagIsMr016`, `AClaimOnAWritableTagDemotedByASignalLinkIsMr016`, `AClaimOnATagTheBlockDoesNotCommandIsMr016`, `ATagClaimedByTwoBlocksIsOneMr016OnTheSecond`, `AnotherBlockCommandingAClaimedTagIsMr016WhicheverWasAddedFirst`, `ATagClaimedTwiceByOneBlockIsOneMr016`, `ARepeatedClaimOnAMissingTagIsReportedOnceAtEachPosition`, `AnotherBlocksWritePointsAtTheClaimantsAcceptedClaimNotItsRepeat`; `ClaimTests.AClaimCoreRefusesIsMr016AtTheClaim`, `ATagClaimedByTwoControllersIsMr016AtTheSecondClaim`, `AnotherControllerCommandingAClaimedTagIsMr016AtTheClaim`, `ARepeatedClaimAndAnotherWriterAreEachReportedAtTheirOwnClaim`, `TheMessageIsSplitIntoSymptomAndFix` (a claim holding `". "`) |
| 5. Directory `Access` `ReadOnly` + `ClaimedBy`; others `""`; `ToText` / `millrace tags` `  claimed by <id>` | 1, 2, 5 | `ClaimedTagTests.AClaimedTagIsPublishedReadOnlyNamingItsClaimantAndEveryOtherTagNamesNone`, `ADirectoryWithoutClaimsPublishesEveryTagAsItsBindingSays`, `ToTextAppendsTheClaimantAfterTheDescription`; `ClaimValidationTests.WithoutClaimsNoTagIsClaimed`; `TagsCommandTests.AClaimedTagIsListedReadOnlyWithItsClaimant`, `JsonFormatNamesTheClaimantOnlyOnAClaimedTag` |
| 6. A scenario writing a claimed tag is `DoesNotBind` with the message, before tick 0 | 4, 5 | `ClaimedTagScenarioTests.WritingAClaimedTagDoesNotBindAndNamesTheClaimant`, `ARecordingThatWroteATagNowClaimedDoesNotReplay`; `MineConveyorTests.AScenarioThatWritesAPermitIsRefusedBeforeTickZero` |
| 7. Plant file `"claims"`; schema accepts it; `MR016` at `claims[i]` | 3 | `ClaimTests.AClaimIsPassedToTheBuilderAndTheTagIsPublishedReadOnly`, `AnEntryWithoutClaimsAndOneWithAnEmptyListClaimNothing`, `AClaimCoreRefusesIsMr016AtTheClaim`, `TheMessageIsSplitIntoSymptomAndFix`, `AClaimsListThatIsNotAnArrayOfStringsIsMr103`; `PlantSchemaTests.EveryBlockBranchDeclaresTheControllerKeysAndAnOptionalListOfClaims`, `MatchesTheGoldenFile`; `SchemaAgreementTests.BothValidatorsRejectTheSameControllerMistakes` (+2 rows), `TheSchemaRejectsStructuralErrorsAndOnlyThose` (MR016 fixture); `CorpusTests.EveryInvalidPlantYieldsExactlyTheCodeInItsName` (MR016 fixture) |
| 8. The sample's four interlocks claim their permits; nine goldens byte-identical | 5 | `MineConveyorTests.EveryPermitIsReadOnlyAndClaimedByItsInterlock`, `EveryScenarioMatchesItsGolden`, `EveryScenarioReplaysByteForByteFromARecording`, `TheLoaderReportsNothingAtAllNotEvenAWarning`, `TheGeneratedSchemaAcceptsThePlantAndRejectsAMistakeInIt` |
| 9. Unclaimed tags, block outputs and commands unchanged | 1, 2, 4 | `ClaimedTagTests.UnclaimedTagsAcceptWritesExactlyAsBefore`; `ClaimValidationTests.WithoutClaimsNoTagIsClaimed`; `ClaimedTagScenarioTests.AnUnclaimedTagBesideAClaimedOneStillBinds`, `TheSameWriteBindsOnThePlantWithoutTheClaim`; every existing test, unchanged but one (R142) |
| §2 read-only and kind checks first | 1 | `ClaimedTagTests.TheKindCheckRunsBeforeTheClaimCheck`, `UnclaimedTagsAcceptWritesExactlyAsBefore` |
| §2 a claimed required input still satisfies `MR002` | 2 | `ClaimValidationTests.AClaimedRequiredInputStillCountsAsDriven` |
| §2 `AddScanBlock` one-argument call unchanged; null claim | 2 | every existing `AddScanBlock(block)` caller; `ClaimValidationTests.ANullClaimIsRefusedWhenTheBlockIsAdded` |
| §3 `MR016` in the diagnostics reference | 3 | `DiagnosticsReferenceTests.ThePlantValidationTrailerCoversTheBlockCodes`, `TheCommittedReferencePageIsCurrent` |
| §4 claimant named; the fix | 4 | `ClaimedTagScenarioTests.WritingAClaimedTagDoesNotBindAndNamesTheClaimant` |
| §7 docs: sample README, `control-blocks.md`, plant-file docs, diagnostics reference | 3, 6 | `DocumentationTests.TheControlBlocksPageDescribesClaimedTags`; `SampleReadmeTests.TheReadmeSaysEachPermitIsClaimedByItsInterlock`; `DiagnosticsReferenceTests.TheCommittedReferencePageIsCurrent` |
| Review Focus 1–5 | 2, 4 | `ClaimValidationTests.AClaimFollowsTheNameAnExplicitBindGaveThePort`; `ClaimedTagCommandTests.ACommandToAClaimedTagIsReadOnlyAndNeverQueued`; `ClaimedTagScenarioTests.ARecordingThatWroteATagNowClaimedDoesNotReplay`; `ClaimValidationTests.AClaimIsMatchedOrdinallyAndExactly`; `ABlockMayClaimAnotherBlocksCommandAndThenOnlyItWritesIt`, `ABlockMayClaimItsOwnCommandWhenItCommandsIt` |

## Test-count arithmetic

Baseline on `d26aae6` (measured): 1348 = 37 + 461 + 132 + 56 + 211 + 163 + 76 +
117 + 23 + 72.

| Task | Added | Project totals after | Suite |
|---|---|---|---|
| 1 | `ClaimedTagTests` 8 facts → Core +8 | Core 469 | 1356 |
| 2 | `ClaimValidationTests` 17 facts + theory 3 + theory 2 = 22 → Core +22; `ClaimedTagCommandTests` 1 → Realtime +1 | Core 491, Realtime 57 | 1379 |
| 3 | `ClaimTests` 6 facts + theory 5 + theory 2 = 13; MR016 fixture → +1 `CorpusTests` case, +1 `SchemaAgreementTests` case; +2 `BothValidatorsRejectTheSameControllerMistakes` rows; +1 `PlantSchemaTests` fact → Configuration +18 | Configuration 229 | 1397 |
| 4 | `ClaimedTagScenarioTests` 4 facts → Scenarios +4 | Scenarios 167 | 1401 |
| 5 | `TagsCommandTests` +2 → Cli +2; `MineConveyorTests` 1 fact + theory 2 → Samples +3 | Cli 78, Samples 75 | 1406 |
| 6 | `DocumentationTests` +1 → Control +1; `SampleReadmeTests` +1 → Samples +1 | Control 118, Samples 76 | 1408 |

Final: **1408** = 37 Io.Abstractions / 491 Core / 132 Components / 57 Realtime /
229 Configuration / 167 Scenarios / 78 Cli / 118 Control / 23 Control.Catalogue /
76 Samples (measured in the scratch run). Existing tests changed: one
(`DiagnosticsReferenceTests.ThePlantValidationTrailerCoversTheBlockCodes`, R142);
no existing test is removed or renamed.
