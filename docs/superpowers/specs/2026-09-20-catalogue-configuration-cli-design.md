# Catalogue, Configuration and CLI — Design (plan 5a)

Date: 2026-09-20. Addendum to
`2026-09-02-industrial-process-simulation-engine-design.md` (the "main spec"),
refining its sections 4, 14, 16 and 17 for the catalogue, `Dse.Configuration`
and `Dse.Cli`.

## 1. Scope

The work originally parked as "plan 5" is split in two.

**Plan 5a — this document.** The component catalogue, declarative JSON plants
with a loader and a generated JSON Schema, and the CLI commands that need no
scenario: `catalog export`, `schema export`, `validate`, `tags`.

**Plan 5b — a later document.** `Dse.Control` (interlock, permissive, sequencer,
timer, alarm; scan scheduling), `Dse.Scenarios` (definition, recording, replay,
golden logs) and `dse run`. These open questions belong to 5b and are *not*
decided here: the `EventLog.ToText()` format, alarm state in `LiveState`,
scenario recording through the `WRITE` records and `ICommandRecorder`,
dispatcher failure observability.

Still parked with no consumer: frame pooling and the small real-time fixes
listed in plan 4's final review.

### Decisions taken in brainstorming

| Question | Decision |
|---|---|
| Where descriptors come from | Hand-written descriptor plus factory per type. No reflection, no attributes. A conformance test keeps them honest. |
| What a plant file can instantiate | Every catalogue type, leaf or shipped composite, in one flat list. JSON cannot define a composite. |
| How outside types reach the catalogue | `ICatalogueModule`, composed explicitly in code; the CLI loads more with `--assembly`. |
| How the generated schema is verified | A test-only reference to JsonSchema.Net (MIT). Nothing shipped depends on it. |
| Simulation options in the plant file | An optional `defaults` block; a 5b scenario overrides it. |
| Materials | Catalogue materials plus plant-local `materials`, one namespace. |
| What the loader returns | A `SimulationBuilder`, not a `Simulation`. |
| Transforms and hold conditions | One generalised `ObjectDescriptor` with a slot, not a catalogue kind each. |
| Referencing a composite | By its id: `"belt": "CV001"` resolves to the conveyor's belt. |
| Extra CLI commands | `schema export` and `tags`, beyond the main spec's three. |

## 2. The catalogue — `Dse.Core/Catalogue`

### 2.1 Shape

A `Catalogue` is an immutable value built once:

```csharp
Catalogue catalogue = new CatalogueBuilder()
    .Add<ComponentsModule>()
    .Add<WheelLineModule>()
    .Build();
```

There is no static registry and no assembly scanning. A module is:

```csharp
public interface ICatalogueModule
{
    string Name { get; }
    void Register(CatalogueBuilder builder);
}
```

`Dse.Components` ships `ComponentsModule`. `Build()` fails if two entries of the
same kind (or two objects in the same slot) share a type name, and the message
names both modules.

The catalogue holds three kinds of entry.

| Entry | Examples | Why |
|---|---|---|
| `ComponentDescriptor` | `motor`, `conveyor`, `belt-scale` | What a plant instantiates. |
| `ObjectDescriptor` | slot `transform`: `thermal-transfer`, `moisture-loss`, `residence-accumulator`; slot `hold`: `for-seconds`, `temperature-at-least`, `temperature-at-most`, `state-at-least`, `all` | Nested, typed, parameterised values a component takes. No id, ports or faults. |
| `MaterialDescriptor` | the shipped materials | Sources, formers and process units take a `MaterialType`; a plant names it. |

Type names are kebab-case and compared ordinally.

### 2.2 `ComponentDescriptor`

- `Type`, `Category` (`Mechanical`, `Instrumentation`, `Safety`, `Flow`,
  `Conveyor`, `Signal`), `Description`.
- `Parameters` — a tree of `ParameterDescriptor` (2.3).
- `Ports` — signal ports: name, direction, value type (`bool`, `double`, `int`,
  `long`), unit, optional range, `required`.
- `FlowPorts` — inlets and outlets with their `PayloadKind`. This is the main
  spec's "flow node kinds".
- `Faults` — the existing `FaultDescriptor` list, reused unchanged.
- `TelemetryKeys` — name and unit.
- `Tags` — the tag bindings the type declares: name suffix, kind, access, unit.
- `Factory` — `(string id, ParameterValues p, IReferenceResolver refs) → ISimNode`.
  Not exported.

Port names within one descriptor must be unique ignoring case; the descriptor's
constructor rejects a violation.

Each descriptor lives in its component's own file as
`public static ComponentDescriptor Descriptor { get; }`, beside the constructor
it must match.

### 2.3 Parameters

| Kind | Carries | JSON |
|---|---|---|
| `Double`, `Int` | unit, default, inclusive/exclusive range, description | number |
| `Bool` | default, description | boolean |
| `String` | default, description | string |
| `Enum` | allowed values, default | string |
| `Group` | child parameters — a nested record such as `MotorRating` or `InstrumentSpec` | object |
| `List(Group)` | child parameters, min count — `RecipeLine` | array of objects |
| `Reference` | required capability (an interface, e.g. `IMaterialObservable`) | string: a component id |
| `Material` | optional required `PayloadKind` | string: a material name |
| `MaterialState` | which sibling `Material` parameter it indexes | string: a state name |
| `Object(slot)` | the slot | `{ "type": …, …parameters }` |
| `ObjectList(slot)` | the slot, min count | array of the above |

A parameter with no default is required. Group definitions are shared values:
`InstrumentSpec` is declared once and reused by every instrument, and appears
once under `$defs` in the schema.

**Material state is referenced by name**, never by index. A factory resolves
the name against the material's state schema; an unknown name is a diagnostic
listing the states the material has. A transform or hold that needs a state but
has no sibling `Material` parameter (it sits inside a process unit) resolves
against the enclosing component's output material, which the loader passes down
through `ParameterValues`.

`ParameterValues` is the parsed, defaulted, range-checked tree the factory
reads: `p.Double("ratedPowerW")`, `p.Group("motor")`, `p.Material("output")`,
`p.Objects<IMaterialTransform>("transforms")`. Asking for a name the descriptor
does not declare throws — that is a bug in the factory, and the conformance test
finds it.

### 2.4 References to composites

`BeltScale`, `Pyrometer` and `PartCounter` take an `IMaterialObservable`. When a
`Reference` targets a node that does not itself implement the capability, the
resolver asks it for one:

```csharp
public interface ICapabilityProvider
{
    bool TryGetCapability<T>(out T capability) where T : class;
}
```

`Conveyor` implements it and hands out its belt, so `"belt": "CV001"` works and
nobody names an inner leaf. If neither route yields the capability, the
diagnostic lists the components in the plant that would satisfy it.

### 2.5 Conformance

Hand-written descriptors can drift from code. `Dse.Core.Testing` (a namespace in
`Dse.Core`, no test-framework dependency) provides:

```csharp
IReadOnlyList<string> CatalogueConformance.Check(Catalogue catalogue, ConformanceFixtures fixtures);
```

For every component entry it builds an instance from the descriptor's defaults
plus that type's entry in `fixtures` (minimal values for required parameters and
stand-ins for references), then reports every mismatch between the descriptor
and the instance: signal ports by name, direction, type and `required`; flow
ports by name, direction and payload kind; faults by id and parameter names;
declared tags by suffix, kind and access. It also builds every `ObjectDescriptor`
and `MaterialDescriptor` once.

Two tests in `Dse.Components.Tests` use it: the check returns no mismatches,
and a reflection sweep finds no concrete public `ISimNode`,
`IMaterialTransform` or hold factory in `Dse.Components` without a descriptor.
**Every concrete `ISimNode` gets a descriptor**, including `UnitDelay<T>`
(registered per closed type, e.g. `unit-delay-bool`) and the leaves `Conveyor`
flattens to. The helper is public so that sample and third-party modules run the
same check.

### 2.6 Export

`CatalogueJson.Export(catalogue)` writes deterministic JSON — entries sorted by
type name, fixed property order, invariant culture, `\n` line endings — with a
top-level `formatVersion: 1`. A golden file pins the export of
`ComponentsModule`.

### 2.7 Not included

Unit algebra or conversion (units are strings); units on runtime ports
(`AddInput`/`AddOutput` are unchanged); per-entry versioning.

## 3. Configuration — `Dse.Configuration`

### 3.1 The plant file

```json
{
  "$schema": "./dse-plant.schema.json",
  "defaults": { "seed": 1, "timeStepMs": 10, "startTime": "2026-01-01T06:00:00Z" },
  "materials": [
    { "name": "ore-wet", "kind": "bulk", "states": [],
      "properties": { "density": 1900, "moisture": 0.12, "temperature": 15 } }
  ],
  "components": [
    { "id": "FEED",  "type": "bulk-source",
      "parameters": { "material": "ore-wet", "rateKgPerS": 120 } },
    { "id": "CV001", "type": "conveyor",
      "parameters": { "lengthM": 80, "motor": { "ratedPowerW": 55000 } } },
    { "id": "WT001", "type": "belt-scale",
      "parameters": { "belt": "CV001", "positionM": 40,
                      "spec": { "unit": "t/h", "rangeLow": 0, "rangeHigh": 800 } } }
  ],
  "signals": [ { "from": "SR001.healthy", "to": "CV001.safetyOk" } ],
  "flows":   [ { "from": "FEED.out", "to": "CV001.in" } ],
  "tags":    [ { "name": "CV001.RUN_CMD", "port": "CV001.run", "access": "write" } ]
}
```

(Parameter names above are illustrative; the descriptors are authoritative.)

- `defaults` is optional, as is each of its members. A scenario (5b) or
  `--time-step` overrides it. With neither, `timeStepMs` falls back to
  `SimulationOptions`' own default.
- `materials` is optional. Plant-local and catalogue materials share one
  namespace; a collision is an error.
- Comments and trailing commas are accepted.
- Unknown keys are errors at every level.

**Addressing.** A signal or flow port is `"<componentId>.<port>"`, split on the
*last* dot because flattened leaf ids contain dots. Component ids match
ordinally; port names match ignoring case.

### 3.2 Loader

```csharp
LoadResult result = PlantLoader.Load(json, catalogue, new LoadOptions { TimeStep = … });
```

`LoadResult` carries `Diagnostics` and, when none is an error, a
`SimulationBuilder` ready for `Build()`. Returning the builder lets a scenario
set options first.

Stages, each collecting every error it can find before the pipeline stops at the
end of the first stage that produced any:

1. **Parse** — `JsonDocument`. A syntax error reports line and column.
2. **Structure** — walk the document against the descriptors: unknown type,
   unknown / missing / wrong-typed parameter, out-of-range value, duplicate id,
   unknown key, unknown object type in a slot, unknown material or state name.
3. **References** — resolve `Reference` parameters and check capability; order
   instantiation topologically; a reference cycle is an error listing the cycle.
4. **Instantiate** — call factories in that order. An `ArgumentException` from a
   constructor is caught and reported against the component's JSON path.
5. **Wire** — signals, flows, tag binds. Unknown component or port (with a
   nearest-name suggestion), wrong direction, payload-kind mismatch.
6. **Build** — add everything to a `SimulationBuilder` and run `Validate()`. The
   existing `DSE001`–`DSE011` errors pass through unchanged.

### 3.3 Diagnostics

A new `DSE1xx` range. A `ConfigDiagnostic` has a code, a severity, a JSON path
(`$.components[3].parameters.motor.ratedPowerW`), a message, and a **fix** — a
required constructor argument, so "errors that name the fix" is enforced by the
type, not by convention.

```
DSE104 $.components[2].parameters.belt
  'CV01' is not a component in this plant.
  Fix: use one of CV001, CV002, CV003 — 'CV001' is closest.
```

All codes live in one table (`ConfigDiagnostics`), from which the reference page
in `docs/` is generated; a test asserts the committed page is current. Initial
allocation, to be finalised in the plan:

| Code | Meaning |
|---|---|
| DSE100 | JSON syntax error |
| DSE101 | Unknown key |
| DSE102 | Unknown component / object / material type |
| DSE103 | Parameter missing, unknown, wrong JSON type, or out of range |
| DSE104 | Reference to a component that does not exist |
| DSE105 | Referenced component lacks the required capability |
| DSE106 | Reference cycle |
| DSE107 | Duplicate component id or material name |
| DSE108 | Unknown port in a signal, flow or tag address |
| DSE109 | Port direction or payload kind mismatch |
| DSE110 | Unknown material state name |
| DSE111 | Component constructor rejected its parameters |

### 3.4 Recipe and material mismatch — not in 5a

Plan 3's review parked "validate `BulkProcessUnit` recipe/material mismatch at
build time" for this plan. It stays parked, with reasoning. The constructor
already rejects everything it can see: a non-bulk material, a duplicate or empty
line, a non-positive mass; and the unit's inlets are created *from* the recipe,
so a line cannot name a missing inlet. What remains is an upstream source
delivering a material other than the one a line expects — and that cannot be
checked at build time, because a flow port declares a `PayloadKind` and nothing
more. Checking it needs outlets that declare the `MaterialType` they produce and
a rule for propagating that through belts and chutes, which is a change to the
Core flow model with its own design questions (what does a chute fed by two
materials declare?). Neither reference sample has a `BulkProcessUnit`, so
nothing in v1 is blocked. The loader does catch the configuration-level half: a
recipe line naming an unknown material is `DSE102`, and a non-bulk one surfaces
the constructor's message as `DSE111`.

### 3.5 Schema

`PlantSchema.Generate(catalogue)` emits JSON Schema draft 2020-12:

- `components.items` is a `oneOf`, one branch per component type, discriminated
  by `"type": { "const": … }`;
- `required` lists parameters without defaults; `minimum` / `maximum` /
  `exclusiveMinimum` / `exclusiveMaximum` come from ranges; `default` and
  `description` are carried over;
- `additionalProperties: false` throughout;
- groups, object slots (each a `oneOf` of its registered types) and the material
  shape live under `$defs`;
- references, material names and state names are plain strings — a schema cannot
  check they resolve, and each such property's description says the loader does.

Output is deterministic and pinned by a golden file for `ComponentsModule`.

### 3.6 Tests

- **Fixture corpus** under `tests/Dse.Configuration.Tests/Plants/`:
  `valid/*.json` load clean; each `invalid/<code>-<case>.json` yields exactly
  that code. One theory per directory.
- **Schema agreement** (JsonSchema.Net, test-only): the schema is itself valid
  2020-12; it accepts every `valid/` fixture; it rejects every `invalid/` fixture
  whose error is structural (DSE101–103); fixtures whose error is semantic
  (DSE104+) are expected to *pass* the schema, and the test asserts that too, so
  the boundary between the two validators is documented by a test.
- **Round trip** — the conveyor plant of `ConveyorRealtimeTests`, expressed as
  JSON, produces a byte-identical `EventLog.ToText()` to the code-built plant
  over the same run. This proves the factories build what the constructors
  build.
- **Multiple errors** — one fixture with three independent structural errors
  reports all three.

### 3.7 Not included

A plant writer (object → JSON); includes or multi-file plants; variable or
environment substitution; YAML.

## 4. CLI — `Dse.Cli`

### 4.1 Commands

| Command | Does | stdout |
|---|---|---|
| `dse catalog export` | The catalogue as JSON (2.6) | the JSON |
| `dse schema export` | The generated plant schema (3.5) | the JSON |
| `dse validate <plant.json>` | The whole loader pipeline through `Validate()` | summary |
| `dse tags <plant.json>` | Loads and builds the plant, prints its tag directory: name, kind, access, unit, range | the table |

`dse run <scenario>` arrives with plan 5b.

Options: `--assembly <path>` (repeatable, all commands); `--out <file>`
(payload to a file — accepted by `catalog export`, `schema export` and `run`
only; `validate` and `tags` write to standard output, amended by plan 5d);
`--format text|json` (`validate`, `tags`); `--time-step <ms>`
(`validate`, `tags`). `dse`, `dse help` and `dse <command> --help` print help
generated from one command table.

On success `validate` prints components, flattened leaves, signal links, flow
links, tags, controllers (plan 5d), and the time step used. On failure it
prints every diagnostic in the 3.3 format. With `--format json` the output is
`{ "ok": bool, "summary": {…} | null, "diagnostics": [ { code, severity, path, message, fix } ] }`.

`tags` reuses `TagDirectory.ToText()` for text and emits an array of tag
descriptors for JSON. If the plant has errors it behaves as `validate` does.

### 4.2 Exit codes and streams

`0` success · `1` the plant has errors · `2` usage error · `3` a file or
assembly could not be read or loaded. Payload to stdout; diagnostics and usage
to stderr — except under `--format json`, where the diagnostics are the payload
and go to stdout.

### 4.3 Plugin loading

Each `--assembly` is loaded into one shared, non-collectible
`AssemblyLoadContext` whose resolver returns the CLI's own copies of every
`Dse.*` assembly, so `ICatalogueModule` has one type identity. Every public
`ICatalogueModule` with a parameterless constructor is registered. An assembly
with none is an exit-3 error saying so; a duplicate type name surfaces the
catalogue's own error naming both modules.

### 4.4 Structure

No argument-parsing package: a small hand-rolled parser keeps the zero-dependency
rule. `Program.Main` calls `CliApp.Run(string[] args, TextWriter stdout, TextWriter stderr) → int`,
so tests run in-process. The project sets `PackAsTool` with
`ToolCommandName=dse`; nothing is published.

### 4.5 Tests

In-process tests for every command, option, exit code and both formats. A
fixture project, `tests/Dse.Cli.Tests.SampleModule`, holds one trivial component
written by following the authoring recipe verbatim; its built DLL drives the
`--assembly` tests: valid plugin, assembly with no module, duplicate type name,
missing file.

### 4.6 Not included

`dse run`; `dse init` or scaffolding; watch mode; coloured output.

## 5. Rollout, recipe and documentation

**Descriptor rollout.** `ComponentsModule` registers every concrete node in
`Dse.Components` and the Core nodes a plant can use (`UnitDelay<T>`, the belts),
the three transforms, the five holds and the shipped materials. The conformance
tests are written first, so each rollout task — mechanical; instruments; safety;
flow; transforms, holds and materials; conveyor — starts red and ends green.

**Authoring recipe.** `docs/authoring-a-component.md`: which interfaces to
implement; where state lives; declaring ports, faults and tags; writing the
descriptor and factory; registering a module; the determinism rules (randomness
only from `InitContext`, no wall clock, no statics, no iteration over unordered
collections); and running `CatalogueConformance.Check` as the last step. The
sample-module component is built from it and linked as the worked example.

**Other documentation.** The architecture document gains a
catalogue → schema → loader section; the README gains a CLI quick start; the
generated `DSE1xx` reference page.

## 6. Projects

| New | Depends on |
|---|---|
| `src/Dse.Core/Catalogue/`, `src/Dse.Core/Testing/` (folders, not projects) | — |
| `src/Dse.Configuration` | `Core`, `Components` |
| `src/Dse.Cli` | `Core`, `Components`, `Configuration` |
| `tests/Dse.Configuration.Tests` | + JsonSchema.Net |
| `tests/Dse.Cli.Tests`, `tests/Dse.Cli.Tests.SampleModule` | |

`Dse.Cli` gains references to `Dse.Scenarios`, `Dse.Control` and `Dse.Realtime`
in plan 5b, when `run` needs them.

## 7. Success criteria

- `dse catalog export` and `dse schema export` reproduce their golden files.
- Every concrete node, transform and hold in `Dse.Components` has a descriptor,
  and conformance reports no mismatch.
- The JSON conveyor plant and the code-built one produce byte-identical event
  logs.
- Every `DSE1xx` code has an invalid fixture that produces exactly it, and every
  diagnostic carries a fix.
- The schema and the loader agree on the fixture corpus, with the structural /
  semantic boundary asserted.
- A plugin assembly's types appear in `catalog export`, `schema export` and load
  through `validate`.
- Release build with zero warnings; the existing 471 tests still pass.
