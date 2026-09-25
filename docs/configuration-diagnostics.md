# Configuration diagnostics

<!-- Generated from ConfigDiagnostics.All by DiagnosticsReference.Render(). Do not edit by hand:
     run the Dse.Configuration tests with DSE_UPDATE_GOLDEN=1, read the result, commit it. -->

`dse validate` and `PlantLoader.Load` report every problem in a plant file as a diagnostic with four
parts: a **code**, a **JSON path** into the file (`$.components[3].parameters.motor.ratedPowerW`), a
**message** saying what is wrong, and a **fix** saying what to do. A diagnostic without a fix cannot be
constructed.

The loader works in stages — parse, structure, references, instantiate, wire, build — and stops at the
end of the first stage that found an error, having reported *every* error that stage could find. Fixing
what is reported may therefore reveal errors from a later stage.

A plant's `controllers` are resolved in the build stage, once the plant itself has validated: every tag
they name and every value they give is checked against the plant's tags and the blocks' own
(DSE113–DSE115) before any block is built.

| Code | Meaning |
|---|---|
| DSE100 | The file is not valid JSON |
| DSE101 | Unknown key |
| DSE102 | Unknown component, block, object or material type |
| DSE103 | Parameter missing, of the wrong type, or out of range |
| DSE104 | Reference to a component that does not exist |
| DSE105 | Referenced component lacks the required capability |
| DSE106 | Reference cycle |
| DSE107 | Duplicate id or material name |
| DSE108 | Unknown component or port in an address |
| DSE109 | The two ports cannot be connected |
| DSE110 | Unknown material state |
| DSE111 | A constructor rejected its parameters |
| DSE112 | A tag cannot bind that port |
| DSE113 | A controller names a tag the plant does not have |
| DSE114 | A controller's tag or value is of the wrong kind |
| DSE115 | A controller commands a read-only tag |

## DSE100 — The file is not valid JSON

The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column.

## DSE101 — Unknown key

An object has a key the loader does not know. Keys match exactly, including case. Nothing is ignored silently, so a misspelt optional parameter cannot quietly fall back to its default.

## DSE102 — Unknown component, block, object or material type

A "type" names something that is not in the catalogue — a component, a controller's block, an object in a slot — or a material name is not defined in the plant or the catalogue. Custom types need their assembly loaded with --assembly.

## DSE103 — Parameter missing, of the wrong type, or out of range

A required parameter is absent, a value has the wrong JSON type, a number is outside its declared range, an enum value is not allowed, or a list is shorter than its minimum. A controller's scanPeriodMs is required: a number above zero, at least one tick and at most a day.

## DSE104 — Reference to a component that does not exist

A reference parameter holds an id that no component in the plant has. Ids match exactly.

## DSE105 — Referenced component lacks the required capability

The referenced component exists but cannot supply what the parameter needs — a belt scale pointed at a motor. The fix lists the components that can.

## DSE106 — Reference cycle

Components reference each other in a loop, so none of them can be built first.

## DSE107 — Duplicate id or material name

Two components, two controllers, or a component and a controller share an id — they share one set of ids, because a block's id prefixes its tags — or a material is defined twice (the catalogue's materials and the plant's share one namespace).

## DSE108 — Unknown component or port in an address

A signal, flow or tag address is not of the form <component>.<port>, or names a component or port that does not exist. Port names match ignoring case.

## DSE109 — The two ports cannot be connected

A link joins ports that cannot be joined: two outputs, different value types, a signal port under "flows", bulk into discrete, an input that is already driven.

## DSE110 — Unknown material state

A state name is not one of the states the named material declares.

## DSE111 — A constructor rejected its parameters

Every parameter was individually valid but the component, block or object refused the combination — a reset level above the trip level, a belt length that is not a whole number of cells, alarm limits that do not ascend. The message is the constructor's own. A factory that fails any other way is a defect in its module, and the fix names the module.

## DSE112 — A tag cannot bind that port

A tag names a port that has no tag kind (a flow port, an enum output), or asks to write an output.

## DSE113 — A controller names a tag the plant does not have

A controller's tag parameter names no component tag, composite exposure or "tags" bind of the plant, and no tag a controller owns. Names match exactly, including case; the fix suggests the nearest, and `dse tags` lists them all.

## DSE114 — A controller's tag or value is of the wrong kind

A controller's value does not fit the kind of the tag it is for — 1.5 for an Int64 tag, true for a Double one — or a controller names a tag of a kind the block cannot use, such as a Double tag as an interlock condition. Integer-valued numbers fit Int64 and Double tags; only true and false fit a Bool tag.

## DSE115 — A controller commands a read-only tag

A controller writes a tag that does not accept writes: a measured value, another block's output, or a command input a signal link already drives, whose tag the plant publishes read-only.

## DSE001–DSE015 — plant validation

Codes below DSE100 come from `SimulationBuilder.Validate()` and mean the same for a plant built in code:
duplicate ids, unconnected required inputs, algebraic loops, belts too fast for their cells, incompatible
flow links, tag conflicts — and, for a control block declared under `controllers` or attached with
`AddScanBlock`, a scan period that is not a positive whole number of time steps (DSE013), a pin naming a
tag the plant does not have, publishes with another kind or will not accept a command (DSE014), and a
block id or owned tag name that collides with something the plant already has (DSE015). The numbering
skips 012. The loader passes them through with the path of the first component involved, or of the
controller (for DSE013, its `scanPeriodMs`); their message is split at its first sentence into message
and fix. A plant file's controllers are resolved before the blocks are validated, so DSE113–DSE115
report what DSE014 would. See `docs/architecture.md` and `docs/control-blocks.md`.
