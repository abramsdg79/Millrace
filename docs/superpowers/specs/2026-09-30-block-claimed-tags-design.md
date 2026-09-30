# Block-Claimed Tags — Design (plan 6d)

Date: 2026-09-30. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining the tag image of plan 4, the scan-block host of
`2026-09-22-control-blocks-design.md` (5c), the controllers of
`2026-09-25-controllers-in-plant-json-design.md` (5d) and the run permit of
`2026-09-25-interlock-start-inhibit-design.md` (6c). All merged.

**Amended 2026-09-30 by the plan**
(`docs/superpowers/plans/2026-09-30-block-claimed-tags.md`, rulings
R133–R142), where the code forced a choice:

- **A `DSE016` lands on `claims[j]` through `ValidationError.Tag` and
  `ClaimIndex` (R133).** Core errors carry only ids, so `ValidationError`
  gains two init properties; the loader places every `DSE016` on the claim it
  is about — for another block's write, on the claimant's accepted claim; for
  a repeat, on the repeat. A repeated claim is reported once, as a repeat
  (R135).
- **The loader splits a `DSE016` after the quoted tag (R133)**, so a claim
  containing `". "` does not cut its own message in two.
- **The directory is given claimants by tag name (R134)**, not by index.
- **Two blocks claiming one tag is one `DSE016` (R135)**, not also a
  "commands a claimed tag" error for the second.
- **"Unique within the entry" is `DSE016`, not the schema's `uniqueItems`
  (R138)**, so the schema accepts every plant only the loader can reject.
- **A block may claim a block command, another block's or its own (R136);**
  spec §8's "no change to … block commands" means unclaimed commands.
- **Claim names match ordinally and exactly; a null claim throws
  `ArgumentException` from `AddScanBlock` (R137).**
- **`dse tags --format json` gains `"claimedBy"` on a claimed tag (R139)**,
  and `dse tags`' help says it lists the claimant.

## 1. Scope

6c gave every starter, conveyor and bulk source a run `Permit` that its
interlock writes false on a trip and true on an accepted reset. The permit is an
ordinary read-write tag, so anything else may write it: an HMI, a scenario, a
realtime adapter, or another block. Writing `CV001.Permit` true while
`INT_CV001` is tripped defeats the inhibit until the interlock trips again. The
sample README and `docs/control-blocks.md` say so (255fb04). On a real system
the permit bit belongs to the PLC program; the HMI has no write access to it,
and a second rung driving the same coil is a programming error.

This plan lets a block **claim** a plant tag it writes. A claimed tag is written
by its claimant and by nothing else. Writes from outside the simulation are
refused at the call site; another block that names it in its writes fails
validation. The mine-conveyor sample claims every permit for its interlock, and
the bypass is gone.

"Claim" is used, not "own": Core already calls a block's own outputs and
commands its *owned tags* (`OwnedTag`, `ControllerPass.OwnedTags`). A claim is
about a tag that belongs to someone else — usually a component input — whose
write access one block takes for itself.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| Which writers does a claim keep out? | **Every writer but the claimant.** External writes (scenario, HMI, adapters, `Simulation.WriteAt`/`WriteIn`) are refused at the call site; another block writing the tag is a validation error, like a PLC's duplicate-coil check. No force or override path. |
| Where is the claim declared? | **On the block's plant entry, not in the block.** A controller entry takes an optional `"claims"` array; `SimulationBuilder.AddScanBlock` takes the list. `IScanBlock` and every block type are unchanged. (Rejected: an `IScanBlock.Owns` list each type fills in — a public interface change for every block; wiring the component's `Permit` input to the interlock's `Ok` output — a scan→physics graph edge that breaks R66 and changes 6c's semantics.) |
| How does a claimed tag appear in the directory? | **`ReadOnly`, with the claimant named.** `TagDescriptor.Access` publishes `ReadOnly` — exactly right for an OPC UA AccessLevel and for every existing check — and a new `ClaimedBy` property names the block. (Rejected: a third `TagAccess` value every consumer would have to learn.) |

### Success criteria

1. A block may claim any read-write tag in its own `Writes`. Its writes to that
   tag land and log exactly as before (`Set to … by INT_CV001.`).
2. `TagImage.Write(int|string, …)` refuses a claimed tag, throwing at the call
   site as it does for a read-only tag, with
   `Tag 'CV001.Permit' is claimed by INT_CV001; only that block writes it.`
   Nothing is queued. `Simulation.WriteAt`/`WriteIn` refuse it the same way,
   through `CheckWritable`.
3. A block write whose origin is not the claimant is refused with the same
   message. Validation makes this unreachable; it is a defence, and a Core test
   reaches it through the internal API.
4. `Validate()` reports **DSE016** for each of: a claim naming no tag; a claim
   on a tag that is not read-write (including a writable binding demoted
   because its input is wired); a claim on a tag not in the claimant's
   `Writes`; a tag claimed by two blocks; another block whose `Writes` names a
   claimed tag. Each message names the block and the tag and says what to do.
5. The directory publishes a claimed tag with `Access` `ReadOnly` and
   `ClaimedBy` the block id; every other tag has `ClaimedBy` `""`.
   `TagDirectory.ToText()` (and so `dse tags`) appends `  claimed by <id>`
   after the description.
6. A scenario that writes a claimed tag fails to bind (`DoesNotBind`) with
   `Tag 'CV001.Permit' is claimed by INT_CV001; a scenario cannot write it.`
   — before tick 0, so it never produces a partial log.
7. A plant file declares claims as `"claims": ["CV001.Permit"]` on a
   controller entry. The plant schema accepts it; a DSE016 is reported against
   the controller's `claims[i]`.
8. The mine-conveyor sample: `INT_CV001`, `INT_CV002`, `INT_CV003` claim
   `CV001.Permit`, `CV002.Permit`, `CV003.Permit`; `INT_FEED` claims
   `Feed.Permit`. All nine expected logs are unchanged, byte for byte.
9. Unclaimed tags, block outputs and block commands behave exactly as before.

## 2. Core (`Dse.Io.Abstractions`, `Dse.Core`)

**`TagDescriptor`** gains a non-positional `init` property
`string ClaimedBy { get; init; } = ""`. The positional constructor is unchanged,
so every existing call site compiles.

**`SimulationBuilder.AddScanBlock(IScanBlock block, IReadOnlyList<string>? claims = null)`.**
The existing one-argument call keeps working. Claims are stored per block and
checked in `Validate()` after tags and blocks are collected, alongside the
existing DSE014 pin checks, in the order of section 1 criterion 4. A block that
claims the same tag twice is reported once, as a claim on a tag it already
claims (DSE016).

**Where claims live at runtime.** `TagBinding` keeps `Access` `ReadWrite` — the
claimant still writes through it. The builder hands the directory a claimant
per binding index (null when unclaimed). The directory builds each descriptor
with `Access` `ReadOnly` and `ClaimedBy` set when claimed. `TagImage.Check`
takes the write's origin: an external write (no origin) to a claimed index is
refused; a block write whose origin differs from the claimant is refused. The
read-only and kind checks are unchanged and run first. `CheckWritable` passes
no origin.

**Builder checks that read access.** `SimulationBuilder` decides "writable" from
bindings, not descriptors (its DSE014 commanded-tag check and the
writable-port set used for DSE002), so a claimed tag still satisfies the
claimant's `Writes` and still satisfies a required input. The other-block check
is DSE016's, not DSE014's.

## 3. Configuration (`Dse.Configuration`)

The controller entry gains an optional `claims` array of tag names (strings,
unique within the entry). The plant JSON schema and `PlantSchema` agree on it
(`SchemaAgreementTests`). `ControllerPass` passes the list to
`AddScanBlock`. A DSE016 from Core is reported against the controller entry's
`claims[i]` path, the way Core's other validation errors reach a plant file;
the plan measures and follows that existing mapping. DSE016 is added to the
diagnostics reference with a summary and a fix.

## 4. Scenarios (`Dse.Scenarios`)

`ScenarioRunner`'s bind check already refuses a tag whose `Access` is not
`ReadWrite`. When the descriptor's `ClaimedBy` is not empty, the message names
the claimant (criterion 6) and the fix reads: "Write the claiming block's
inputs instead — for an interlock's permit, its reset."

## 5. The mine-conveyor sample

`plant.json` adds `"claims"` to the four interlocks (criterion 8). Nothing else
in the plant writes a permit, so the plant validates unchanged otherwise. No
scenario writes a permit; the nine goldens do not move.

A test shows the bypass is closed: a scenario writing `CV001.Permit` true is
refused before tick 0 with the criterion 6 message, and `dse tags` on the sample
lists each permit as `ReadOnly … claimed by INT_…`. No new scenario file is
added — a refused scenario has no log to keep as a golden.

## 6. Testing

- **Core:** each DSE016 case; an external `Write` by name and by index, and a
  `WriteAt`, refused with nothing queued; a foreign-origin block write refused;
  the claimant's writes landing and logging as before; descriptor `Access` and
  `ClaimedBy`; `ToText`; a claimed required input still satisfies DSE002.
- **Configuration:** `claims` parsed and passed through; schema agreement; a
  DSE016 reported at `claims[i]`.
- **Scenarios:** writing a claimed tag is `DoesNotBind` with the new message.
- **Samples:** every permit is `ReadOnly` and claimed by its interlock; a
  scenario writing a permit is refused; the nine goldens unchanged.
- **CLI:** `dse tags` prints `claimed by` for a claimed tag.
- Existing tests that assert a sample permit's access as `ReadWrite`, if any,
  change to `ReadOnly` and nothing else.

## 7. Documentation

- `samples/mine-conveyors/README.md`: the "permit is not protected" bullet and
  the "ordinary, writable tags" sentence are rewritten — each permit is claimed
  by its interlock; an HMI or scenario write is refused, as an HMI has no write
  access to a PLC program's permit bit.
- `docs/control-blocks.md`: the run-permit pattern's closing caveat becomes a
  description of `claims`, with the plant-JSON snippet.
- The plant-file documentation of controller entries gains `claims`.
- The diagnostics reference gains DSE016.

## 8. Out of scope

- A force or override path for scenarios or tools.
- Claims on anything but a read-write tag in the claimant's `Writes`.
- Any change to unclaimed tags, block outputs or block commands.
