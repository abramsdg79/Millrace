# Controllers in the Plant File — Design (plan 5d)

Date: 2026-09-25. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
building on `2026-09-20-catalogue-configuration-cli-design.md` (5a, "the
catalogue spec"), `2026-09-22-scenarios-and-run-design.md` (5b) and
`2026-09-22-control-blocks-design.md` (5c, "the control-blocks spec"), all
merged.

**Amended 2026-09-25 by the plan**
(`docs/superpowers/plans/2026-09-25-controllers-in-plant-json.md`, rulings
R80–R102), where writing against the real code forced a choice: controllers are
resolved in the build stage after a first `Validate()` of the plant alone, not in
the instantiate stage (R80); the plant's tags come from a new
`SimulationBuilder.PlantTags()` (R81); a controller id follows the component id
rule (R87); a component/block type clash is rejected when it is added (R91);
group definitions are PascalCase (R88); a tag of the wrong kind is `DSE114`
(R83); durations are capped at a year and `scanPeriodMs` at a day (R86);
`DSE013` lands on `.scanPeriodMs` (R92); the diagnostics trailer keeps its
`DSE001–DSE015` heading (R96); conformance also compares an owned tag's unit and
description (R102); every plugin descriptor defect is `DSE111` (R100); §7 states
the measured golden change. The sections below read as amended.

## 1. Scope

**Plan 5d — this document.** Describing the five 5c control blocks — timer,
permissive, interlock, alarm, sequencer — in the plant JSON: a `controllers`
section, a block entry kind in the catalogue, a module that registers the five,
two new parameter kinds, loader support, schema, diagnostics and CLI output. A
plant with interlocks, alarms and a start-up sequence then validates, lists its
tags and runs under `dse run` with no C#. The plan also closes R77 (a recording
must not capture a block's own writes) and attributes block writes in the event
log.

Out of scope, named: new block types; PID; branching sequential function
charts; alarm priorities, shelving and a dedicated `LiveState.Alarms`; a JSON
Schema for scenario files; CSV telemetry; `--speed`; the realtime items parked
since plan 4; the reference samples (plan 6).

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| JsonSchema.Net 8.0.5 pin (open since 5a) | **Keep.** Test-only, MIT; the controller schema gets the same agreement tests as components. |
| `--out` on `validate` / `tags` (open since 5a) | **The code wins.** Both stay stdout-only; the catalogue spec §4.1 is amended to say so. No code change. |
| R77 — a recording of a run with blocks | **The recorder never sees a block's writes.** A recording is the external inputs only; replay re-derives block writes by re-running the blocks. |
| Event-log attribution | A block's write logs `Set to false by INT01.`; every other write keeps `Set to false.` unchanged. |
| Where blocks live in the file | An own `controllers` section, sibling of `components`, with the component envelope (`id`, `type`, `parameters`) plus `scanPeriodMs`. |
| Where block descriptors live | A new catalogue entry kind, `BlockDescriptor`, in `Dse.Core/Catalogue`; the five are registered by `ControlModule` in a **new project `Dse.Control.Catalogue`**, so `Dse.Control` keeps referencing only `Dse.Io.Abstractions`. Plugins may register block types through `--assembly`. |
| Scan period | `scanPeriodMs`, **required**, no default. |
| Durations | Seconds, suffixed `S` (`presetS`, `timeoutS`, `delayS`, `onDelayS`); only the scan period is in milliseconds, like `timeStepMs` and a PLC task. |
| Resolving a value's kind | Each block descriptor declares its owned tags as a function of id and parameters; the loader resolves every tag and converts every value before constructing any block. |
| `controllers` in `validate`'s summary | Always present, `0` for a plant without blocks. |

### Success criteria

1. Every 5c block is declarable in a plant file; `dse validate`, `dse tags` and
   `dse run` handle a plant with blocks, with no C#.
2. The 5c worked example built from JSON and built in code produce
   byte-identical event logs.
3. A recording of a run with blocks contains only the external actions, and
   replaying it reproduces the original event log byte for byte.
4. Every 5b golden and the behaviour of every existing plant are unchanged. The
   5c golden changes only by a ` by <id>` suffix on block-issued `WRITE` lines;
   `validate`'s outputs change only by the `controllers` line and field; the
   catalogue and schema export goldens change only by the additions of §3.5
   and §4.6 — plus, in the schema golden, the `description` line that lists
   the catalogue's modules.
5. `Dse.Control` references only `Dse.Io.Abstractions`; no package reference
   under `src/`.
6. Release build with zero warnings; every existing test passes (updated only
   where criterion 4 allows, or where a decision above inverts what it asserts —
   the attributed message and R77's closure; the plan's R98 lists every such
   test).

## 2. Layout

| project | change |
|---|---|
| `src/Dse.Io.Abstractions` | none |
| `src/Dse.Control` | none — no new reference, no change to a block's behaviour |
| `src/Dse.Core` | `Catalogue`: `BlockDescriptor`, `CatalogueBuilder.AddBlock`, `ComponentCatalogue.Blocks`/`TryGetBlock`/`ModuleOf`, parameter kinds `Tag` and `Value`, block export, conformance for blocks. `Io`/`Control`: the write origin (§5). |
| **`src/Dse.Control.Catalogue`** (new) | references `Dse.Core` and `Dse.Control`; `ControlModule : ICatalogueModule`, the five descriptors and factories, the `transition` object slot. No packages. |
| `src/Dse.Configuration` | references `Dse.Control.Catalogue`; `controllers` in every stage that needs it; schema; `DSE113`–`DSE115`. |
| `src/Dse.Cli` | the default catalogue becomes `ComponentsModule` + `ControlModule`; `validate`'s `controllers` count. |
| `src/Dse.Scenarios` | none in code; the recorder's contract narrows through `IActionRecorder` (§5). |
| `src/Dse.Realtime` | none. |
| `tests/Dse.Control.Catalogue.Tests` (new) | conformance and reflection sweep for `ControlModule`. |

## 3. The catalogue

### 3.1 `BlockDescriptor`

A third entry kind beside `ComponentDescriptor` and `ObjectDescriptor`:

- `Type` (kebab-case), `Description`.
- `Parameters` — the existing `ParameterDescriptor` tree (groups, lists, enums,
  object slots, ranges, defaults, descriptions).
- `OwnedTags` — `(string id, ParameterValues p) → IReadOnlyList<(TagSpec Spec, TagAccess Access)>`:
  the tags the block will own, full names, outputs read-only and commands
  read-write. It must be a function of the parameters alone — never of the
  kinds of the tags the block reads or writes — which is what lets the loader
  know every block's tags before constructing any block (§4.4). The loader calls
  it with the parameters bound for checking, so it must read neither a `Value`
  nor an object parameter. For four of the five blocks the list is fixed; for
  the alarm it follows the configured limits.
- `Factory` — `(string id, TimeSpan scanPeriod, ParameterValues p) → IScanBlock`.
  Not exported.

`CatalogueBuilder.AddBlock(BlockDescriptor)` registers one; a module calls it
from `Register` as it calls `Add` for components. **Component and block type
names share one namespace**: `AddBlock` and `Add` reject a block type that
equals a component type (or a second block of the same type) when it is added,
naming both modules, as the builder does today for two components.

### 3.2 Two new parameter kinds

Modelled on the `Material` / `MaterialState` sibling pair:

| kind | JSON | meaning |
|---|---|---|
| `Tag` | string | The full name of a tag: a plant tag (component tag, composite exposure or `tags` bind) or a block-owned tag. Resolved by the loader (§4.4). A descriptor may require a kind (`Param.Tag(…, TagKind.Bool)`) and mark a tag the block commands (`writes: true`). `p.Tag("input")` returns the resolved name. Only a block or an object may declare one. |
| `Value` | boolean or number | A value for the tag named by a sibling `Tag` parameter (the descriptor says which). Converted to that tag's kind by the loader. `p.Value("value")` returns a `TagValue` of the right kind. |

Conversion, the 5b scenario rule applied to a known target:

| JSON | target Bool | target Int64 | target Double |
|---|---|---|---|
| `true` / `false` | Bool | DSE114 | DSE114 |
| integer-valued number (`3`, `3.0`, `3e0`) | DSE114 | Int64 | Double |
| other number (`1.5`) | DSE114 | DSE114 | Double |

### 3.3 Shared definitions

- Group `Condition`: `tag` (Tag, Bool), `normal` (Bool, required).
- Group `BlockWrite`: `tag` (Tag, commanded), `value` (Value of `tag`).
- Object slot `transition`, two types:
  - `when`: `tag` (Tag), `op` (Enum `==`, `!=`, `<`, `<=`, `>`, `>=`), `value` (Value of `tag`);
  - `after`: `delayS` (Double, 0 to one year).

Each appears once under `$defs` in the schema (`group.Condition`,
`group.BlockWrite`, `object.transition`); group names are PascalCase like every
other group definition, and the alarm limit and sequencer step groups are
`AlarmLimit` and `SequenceStep`.

### 3.4 The five blocks

`ControlModule` registers, with the constructor of 5c behind each factory:

| type | parameters | owned tags |
|---|---|---|
| `timer` | `mode` Enum `on-delay` / `off-delay` / `pulse`, required; `input` Tag (Bool); `presetS` Double ≥ 0 | `Q`, `ET` |
| `permissive` | `conditions` [Condition], min 1 | `Ok`, `FirstOut` |
| `interlock` | `conditions` [Condition], min 1; `trip` [BlockWrite], default empty | `Ok`, `Tripped`, `FirstOut`; command `Reset` |
| `alarm` | `input` Tag (Double); `limits` [ `kind` Enum `lo-lo` / `lo` / `hi` / `hi-hi`, `value` Double, `deadband` Double ≥ 0 default 0, `onDelayS` Double ≥ 0 default 0 ], min 1 (at most 4, by the constructor) | `<Kind>.Active`, `<Kind>.Acked` per limit, in limit order; command `Ack` |
| `sequencer` | `steps` [ `name` String, `writes` [BlockWrite] default empty, `transition` Object(`transition`), `timeoutS` Double > 0 optional ], min 1; `abort` [BlockWrite], default empty | `Step`, `Running`, `Held`, `Complete`, `Faulted`, `StepTime`; commands `Start`, `Hold`, `Resume`, `Abort`, `Reset` |

Every duration (`presetS`, `onDelayS`, `timeoutS`, `delayS`) is at most one year
(31 536 000 s): `TimeSpan.FromSeconds` overflows near 9.2e11 s, and without a
bound an absurd value would surface as an overflow rather than a `DSE103`.

Owned-tag names, kinds, units and descriptions are the 5c blocks' own; the
descriptor declares them, the conformance check (§3.5) proves the declaration.
What only the constructor can see — alarm limit values that do not ascend
with their kinds (the constructor sorts limits by kind, so the order they are
listed in never matters) or a kind given twice,
a Bool `when` with an ordering operator, an empty step name — stays the
constructor's check and surfaces as `DSE111`, as a component's does.

The worked example's interlock and part of its sequencer:

```json
"controllers": [
  { "id": "INT01", "type": "interlock", "scanPeriodMs": 100,
    "parameters": {
      "conditions": [
        { "tag": "CV001.Tripped", "normal": false },
        { "tag": "PERM01.Ok",     "normal": true  } ],
      "trip": [ { "tag": "CV001.Start", "value": false } ] } },
  { "id": "SEQ01", "type": "sequencer", "scanPeriodMs": 200,
    "parameters": {
      "steps": [
        { "name": "Start the belt",
          "writes": [ { "tag": "CV001.Start", "value": true } ],
          "transition": { "type": "when", "tag": "CV001.Speed", "op": ">=", "value": 1.0 },
          "timeoutS": 15 },
        { "name": "Run the feed",
          "writes": [ { "tag": "Feed.Enabled", "value": true } ],
          "transition": { "type": "after", "delayS": 60 } } ],
      "abort": [ { "tag": "CV001.Start", "value": false } ] } }
]
```

### 3.5 Conformance and export

`CatalogueConformance.Check` gains blocks: for every block descriptor it builds
an instance from defaults plus that type's entry in `ConformanceFixtures`
(which gains block fixtures), and reports every mismatch between `OwnedTags`
and the instance's `Outputs` (read-only) and `Commands` (read-write) by full
name, kind and access, and — for a tag matching on those — by unit and
description; and every parameter the factory read that the
descriptor does not declare (as for components). `ConformanceFixtures` may
carry several fixtures for one block type, so the alarm is checked with one
limit and with four.

`CatalogueJson.Export` gains a `blocks` array between `components` and
`objects`, sorted by type, each entry `type`, `module`, `description`,
`parameters` (no owned-tag list: owned tags are a function of the parameters),
with the same fixed property order and formatting rules; a parameter entry
gains `tagKind`, `writes` and `tagParameter` when set; `formatVersion` stays 1 (the
addition is backward compatible for a reader that ignores unknown members).

## 4. Configuration

### 4.1 The `controllers` section

Optional. An array of:

```
{ "id": <string>, "type": <block type>, "scanPeriodMs": <number > 0>, "parameters": { … } }
```

`id` follows the component id rule — non-empty, no dot, no whitespace — because
it prefixes every tag the block owns (`INT01.Ok`) and a dotted id would be
indistinguishable from a leaf's tag; it is unique across components and
controllers. `scanPeriodMs` is required, greater than zero, at least one tick
(0.0001 ms) and at most a day (86 400 000 ms, checked before conversion, as
`timeStepMs` is). `parameters` is optional when the type has no required
parameter. An empty array is valid. Unknown keys are errors, as everywhere.

### 4.2 Loader stages

No new stage; the existing ones gain a controllers pass.

1. **Parse** — unchanged.
2. **Structure** — each entry is walked against its block descriptor exactly as
   a component is: unknown type (`DSE102`), parameter missing / unknown /
   wrong JSON type / out of range and `scanPeriodMs` missing or out of range
   (`DSE103`), unknown key (`DSE101`), and an id equal to another controller's
   or to a component's (`DSE107`).
3. **References** — nothing: blocks name tags, not components.
4. **Instantiate** — components only, unchanged.
5. **Wire** — unchanged.
6. **Build** — three steps. (a) Components and binds are added and
   `Validate()` runs on the plant alone; any error stops the stage there, so a
   plant's own tag conflict (`DSE009`, `DSE010`) is reported as itself, never
   as a controller's `DSE113`/`DSE115`. (b) The tag resolution of §4.4, then
   each block's factory in file order. An `ArgumentException` from a block
   constructor is `DSE111` at the entry's path; any other factory or `OwnedTags`
   failure — an exception, a null result, a block with an id or scan period it
   was not given — is `DSE111` naming the module. (c) `AddScanBlock` for each
   block in file order and `Validate()` again: Core's `DSE013`–`DSE015` pass
   through, and `PathOf` learns controller ids — `DSE013` lands on
   `$.controllers[i].scanPeriodMs`, `DSE014` and `DSE015` on
   `$.controllers[i]`.

Tag resolution cannot sit in Instantiate: the builder is created in Build,
`tags` binds become bindings in Wire, and a tag's access depends on wiring (a
writable tag on an input a signal link drives is published read-only).

### 4.3 Order

File order is add order: blocks scan in the order the `controllers` array
lists them, and the later block wins when two write one tag on one tick (5c).
The documentation says so.

### 4.4 Resolving tags and values

Before constructing any block, the loader builds a **name → (kind, access)**
table from:

- the plant's tags — every component tag, composite exposure and `tags` bind,
  as `Build()` would produce them, read-only downgrade included — from
  `SimulationBuilder.PlantTags()`, a new public Core query, so there is one
  definition of the plant's tags;
- every controller's `OwnedTags(id, parameters)`.

A name given twice keeps its first entry (plant tags come first); Core reports
the clash as `DSE015`.

Then, for every `Tag` parameter in every controller, with its JSON path:

| failure | code |
|---|---|
| the name is not in the table | `DSE113`, with the nearest name suggested (the catalogue's `Suggest`) |
| a `Value` does not convert to the tag's kind (§3.2), or the tag is not of the kind the parameter requires (a condition on a Double tag) | `DSE114` |
| the tag is a `write`'s target and is read-only | `DSE115` |

A `Value` whose tag did not resolve is not reported a second time. Every such
error is collected before the stage stops. Core's `DSE014` stays as
the backstop for code-built plants and is not expected to fire for a plant that
passed this stage.

### 4.5 Diagnostics

`DSE112` is already `TagCannotBind`; the new codes are:

| code | meaning |
|---|---|
| DSE113 | A controller names a tag the plant does not have. |
| DSE114 | A controller's tag or value is of the wrong kind. |
| DSE115 | A controller writes a read-only tag. |

Each with a message and a fix, e.g.

```
DSE113 $.controllers[3].parameters.steps[1].writes[0].tag
  'CV001.Strat' is not a tag in this plant.
  Fix: use a tag the plant has — 'CV001.Start' is closest.
```

`docs/configuration-diagnostics.md` is regenerated: `DSE113`–`DSE115` join its
table, and the titles of `DSE102` and `DSE107` widen to cover controllers. Its
trailer keeps the heading `DSE001–DSE015 — plant validation`, because it
describes Core's pass-through codes, which do not change; it gains a sentence on
controllers.

### 4.6 Schema

`PlantSchema.Generate` adds:

- `controllers`: an array whose `items` is a `oneOf`, one branch per block type,
  discriminated by `"type": { "const": … }` under `$defs` keys `block.<type>`,
  each requiring `id` (pattern `^[^.\s]+$`), `type` and `scanPeriodMs`
  (`exclusiveMinimum: 0`, `maximum: 86400000`), `additionalProperties: false`;
  `controllers` is the last root property, and a catalogue without block types
  gives it `maxItems: 0` (an empty `oneOf` is not valid JSON Schema);
- `$defs` for `group.Condition`, `group.BlockWrite`, `group.AlarmLimit`,
  `group.SequenceStep` and the `transition` slot (a `oneOf` of `when` and
  `after`);
- `Tag` as a plain string and `Value` as `{ "type": ["boolean", "number"] }`,
  each description saying the loader resolves it.

`DSE101`–`DSE103` controller fixtures are structural (the schema rejects them);
`DSE107`, `DSE111`, `DSE113`–`DSE115` and `DSE013` are semantic (the schema
accepts them). The agreement test asserts both, as for components. The schema
golden is regenerated and read; besides the additions, its `description` line,
which lists the catalogue's modules, changes.

## 5. The write path

**One queue, with an origin.** `PendingWrite` gains `string? Origin` — a block
id, or null. The scan host enqueues through an internal
`TagImage.Write(int index, TagValue value, string origin)`; every other path —
the `CommandBus`, `WriteAt`/`WriteIn`, scenario writes, bound ports — passes
null. One queue keeps today's order: writes land at phase 1 of the next tick
in enqueue order, so the last writer still wins.

`ApplyNow` stays the one landing site:

- logs `WRITE` with message `Set to {value} by {origin}.` when there is an
  origin, and `Set to {value}.` — unchanged — when there is not;
- calls `IActionRecorder.Wrote` **only when there is no origin**.

`IActionRecorder`'s documentation changes from "every action that took effect"
to "every external action that took effect — a write a control block issued is
not one: replaying the external actions re-runs the block, which issues it
again". Faults and clears are unchanged; a block cannot inject either.

Consequences: `ScenarioRecorder` over a run with blocks records only the
scenario's (or bus's) writes, faults and clears (R77 closed); replay re-derives
every block write. `LiveState`, tick frames and `Dse.Realtime.ICommandRecorder`
are unchanged — a block write never passes through the bus, and its effect
reaches frames like any other tag's.

## 6. CLI

- The default catalogue is `ComponentsModule` + `ControlModule`; `--assembly`
  modules may register block types.
- `dse validate`: the text summary gains a `controllers   N` line after the
  `tags` line and the JSON summary a `"controllers": N` member after
  `"explicitTags"`, both always present (`PlantSummary.Controllers`, defaulted to
  0 so the 5a constructor still compiles). Existing
  expectations are updated for exactly that.
- `dse tags`: unchanged in code; block-owned tags appear because they are
  directory entries.
- `dse catalog export` / `dse schema export`: the additions of §3.5 and §4.6;
  `catalog export`'s help line ("every component, transform, hold and material
  type") names block types too; the `--assembly` help says the shipped catalogue
  includes the control blocks.
- `dse run`: unchanged in code; a plant with `controllers` runs, and a scenario
  may `write` a block command (`SEQ01.Start`, `INT01.Reset`, `CUR01.Ack`) —
  `DSE206` already resolves block tags against the built plant.
- `validate` and `tags` stay stdout-only (ruling below).

## 7. The worked example

The 5c worked example — the `conveyor-line` plant with `PERM01`, `INT01`,
`CUR01` and `SEQ01` — becomes a plant file,
`tests/Dse.Configuration.Tests/Plants/valid/conveyor-control.json`, and its
timeline a scenario file beside the CLI tests.

- **Round trip:** the JSON-built plant and the 5c code-built plant, run over the
  same timeline, produce byte-identical `EventLog.ToText()`. The test lives in
  `Dse.Control.Tests`, beside the code-built example it compares with.
- **CLI golden:** `dse run <scenario> --expect conveyor-control.log` exits 0
  in-process.
- **The 5c golden** `tests/Dse.Control.Tests/Golden/conveyor-control.log` is
  regenerated with the attribution, read, and checked: the only differences are
  a ` by <id>` suffix on the `WRITE` lines a block issued. Measured on the 5c
  golden, that is exactly ten of its 40 lines — `INT01`'s two trip writes (lines
  3 and 33) and `SEQ01`'s eight step-entry writes (lines 7, 12, 13, 16, 17, 25,
  37 and 39). No abort write occurs in this run. The test's two `SEQ01.Start`
  writes (lines 5 and 10) are unchanged.

## 8. Testing

`Dse.Core.Tests`
- A write with an origin logs `… by <id>.`, is not seen by the recorder, and
  keeps its enqueue order against an external write on the same tick (last
  wins); a write without one is byte-unchanged.
- `BlockDescriptor` and `CatalogueBuilder.AddBlock`: a duplicate block type and
  a block type equal to a component type are rejected naming both modules;
  `Tag` / `Value` parameter binding and conversion, every cell of §3.2's table.
- Conformance reports a declared owned tag the instance lacks, one it has but
  did not declare, a kind or access mismatch, a unit or description mismatch,
  and a parameter the factory reads but the descriptor does not declare.
- `SimulationBuilder.PlantTags()` equals the built directory (less block-owned
  tags) and publishes a writable tag on a driven input as read-only.

`Dse.Control.Catalogue.Tests` (new)
- `CatalogueConformance.Check` over `ControlModule` returns no mismatch, with
  alarm fixtures of one and four limits.
- A reflection sweep finds no public concrete `IScanBlock` in `Dse.Control`
  without a descriptor.

`Dse.Configuration.Tests`
- `valid/conveyor-control.json` loads clean (the round trip of §7 is in
  `Dse.Control.Tests`); an empty `controllers` array is valid.
- One invalid fixture per case: `DSE102` unknown block type; `DSE103` missing
  `scanPeriodMs`, an unknown `op`, a negative `presetS`; `DSE101` unknown key in
  a controller; `DSE107` a block id equal to a component id; `DSE111` alarm
  limit values that do not ascend with their kinds; `DSE113` unknown tag with a
  suggestion; `DSE114` `1.5` to an Int64 tag and `true` to a Double tag;
  `DSE115` a write to a read-only tag; `DSE013` a scan period that is not a
  whole number of steps, reported at the controller's `scanPeriodMs`.
- A plant's own `DSE010` tag conflict is reported as itself, not as the
  controller's `DSE113`; every plugin descriptor defect is `DSE111`; a
  `scanPeriodMs` or duration of `1e30` is `DSE103`.
- A block naming another block's owned tag resolves regardless of file order.
- Schema agreement over the new fixtures, the structural/semantic boundary
  asserted.

`Dse.Scenarios.Tests`
- Record the worked example's run: the recording holds exactly the scenario's
  actions; replaying it reproduces the original event log byte for byte.

`Dse.Cli.Tests`
- `validate` text and JSON with `controllers`; `dse run --expect` on the worked
  example; the catalogue and schema export goldens; a sample-module assembly
  that registers a block type, seen by `catalog export`, `schema export` and
  `validate`.

## 9. Documentation

- `docs/control-blocks.md` — "Attaching a block" gains *In the plant file*; the
  "attached in code, `dse run` cannot" limitation and the matching line in
  *What is not here* go; each block section gains its JSON form; the file-order
  rule; write attribution in the event log.
- `docs/authoring-a-component.md` — *Registering a block*: the descriptor,
  `OwnedTags`, the factory, the module, and running the conformance check.
- `docs/scenarios.md` — recordings contain external actions only.
- `docs/configuration-diagnostics.md` — regenerated for `DSE113`–`DSE115`.
- `docs/architecture.md`, `README.md` — `Dse.Control.Catalogue` in the module
  list; controllers in the configuration section and the status.
- `2026-09-20-catalogue-configuration-cli-design.md` §4.1 — amended: `--out` is
  accepted by `catalog export`, `schema export` and `run` only; `validate` and
  `tags` write to stdout.

## 10. Rulings carried into the plan

- **The JsonSchema.Net 8.0.5 pin stays** (test project only, only in
  `SchemaAgreementTests.cs`).
- **`--out` is not accepted by `validate` or `tags`**; the catalogue spec is
  amended rather than the code.
- **`DSE112` is taken** (`TagCannotBind`); controller tag diagnostics are
  `DSE113`–`DSE115`.
- The plan's rulings R80–R102 refine this document; the sections above have
  been amended to agree with them.

Global constraints from 5a–5c apply unchanged: determinism, zero packages under
`src/`, event messages as sentences ending in a full stop, goldens generated
and read, never invented.
