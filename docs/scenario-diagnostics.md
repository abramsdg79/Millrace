# Scenario diagnostics

<!-- Generated from ScenarioDiagnostics.All by ScenarioDiagnosticsReference.Render(). Do not edit by hand:
     run the Dse.Scenarios tests with DSE_UPDATE_GOLDEN=1, read the result, commit it. -->

`dse run`, `ScenarioLoader.Parse` and `ScenarioRunner.Run` report every problem in a scenario file as a
diagnostic with four parts: a **code**, a **JSON path** into the file (`$.timeline[1].args.amount`), a
**message** saying what is wrong, and a **fix** saying what to do. It is the same `ConfigDiagnostic` a
plant file's problems arrive as, and a diagnostic without a fix cannot be constructed.

Checking happens in two passes. `ScenarioLoader.Parse` is structural and needs no plant: DSE200 to
DSE204, and DSE203 for a time that is not on the step the scenario itself declared. `ScenarioRunner`
then loads the plant and binds every action against the built simulation: DSE205, DSE206, and DSE203
against the step the plant actually runs at. **Every check happens before tick 0**, so a scenario that
is wrong never produces a partial event log.

| Code | Meaning |
|---|---|
| DSE200 | Scenario is not valid JSON |
| DSE201 | Unknown key |
| DSE202 | Value missing, of the wrong type, or out of range |
| DSE203 | Time is not on a tick, or not before the end |
| DSE204 | Action is malformed |
| DSE205 | Plant file is invalid |
| DSE206 | Action does not bind to the plant |

## DSE200 — Scenario is not valid JSON

The text could not be parsed. Comments and trailing commas are allowed; everything else must be strict JSON. The message gives the line and column.

## DSE201 — Unknown key

An object has a key the format does not define. Keys match exactly, including case. Nothing is ignored silently, so a misspelt key cannot quietly do nothing.

## DSE202 — Value missing, of the wrong type, or out of range

A required value is absent, has the wrong JSON type, or is outside its range: no "plant", no "duration", a duration of zero or less, a negative seed, a start time without an offset, a fault argument that is not a number, or a write value that is neither a JSON boolean nor a number.

## DSE203 — Time is not on a tick, or not before the end

A time is not a whole number of time steps, or an action is scheduled at or after the end of the run. A run of 120 s at 10 ms runs ticks 0 to 11999, so an action at 120 s would never fire.

## DSE204 — Action is malformed

An action does not have exactly one of "write", "fault" and "clear", or lacks a companion that shape requires, or carries one that belongs to another shape.

## DSE205 — Plant file is invalid

The plant the scenario names has errors of its own. This diagnostic names the plant; the plant's own diagnostics follow it unchanged, with their codes, their paths and their fixes. A plant file that cannot be read at all is the command line's exit code 3, not a diagnostic.

## DSE206 — Action does not bind to the plant

An action names a tag, a component, a fault or a fault argument the plant does not have, or writes a tag that is read-only or of another kind. Every action is bound before tick 0, so a bad scenario never produces a partial log.

## DSE100–DSE112 — the plant's own diagnostics

A scenario names a plant, and that plant is loaded by the same loader `dse validate` uses. When the
plant has errors of its own they follow the DSE205 line unchanged, with their codes, their paths and
their fixes. See [configuration diagnostics](configuration-diagnostics.md).
