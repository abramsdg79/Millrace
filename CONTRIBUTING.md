# Contributing

Thanks for your interest in Millrace. Bug reports, new components, samples
and documentation fixes are all welcome.

## Before you start

For anything larger than a small fix, open an issue first and describe what
you want to change. A new component type, control block or diagnostic touches
the catalogue, the schema and the generated docs, so it helps to agree on the
shape before you write code.

## Build and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
From the repository root:

```bash
dotnet build Millrace.sln
dotnet test Millrace.sln
```

Warnings are errors (`Directory.Build.props`), so a build with a warning
fails. There is no hosted CI, so run both commands before you open a pull
request and say in it that they pass.

## The rules that keep Millrace deterministic

Read the determinism rules in [architecture](docs/architecture.md) before you
write a component or change the core. In short: never use `System.Random`,
`string.GetHashCode()` or wall-clock time; never iterate a `Dictionary` or
`HashSet` during a tick; keep all state in the component. The same plant,
scenario and seed must always produce the same event log.

## Golden files

Event logs, catalogues, the plant schema and the two diagnostics pages
(`docs/*-diagnostics.md`) are golden files that tests compare byte for byte.
Never edit one by hand. When a change is meant to alter one, regenerate it:

```bash
MILLRACE_UPDATE_GOLDEN=1 dotnet test Millrace.sln
```

Then read the diff of every golden that changed, and explain in the pull
request why each change is right. An unexplained change to an event log is
the most common reason a pull request is sent back.

## Adding to the library

- **A component:** follow [authoring a component](docs/authoring-a-component.md).
- **A control block:** see [control blocks](docs/control-blocks.md).
- **A scenario or sample:** see [scenarios](docs/scenarios.md) and the two
  samples under `samples/`.

New behaviour comes with tests. A new component or block also needs an entry
in the catalogue golden and, if it can be misconfigured, a diagnostic with an
invalid-plant fixture under `tests/Millrace.Configuration.Tests/Plants/invalid/`.

## Commits and pull requests

- Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/):
  `feat(components): …`, `fix(cli): …`, `docs: …`, with a body that says why.
- Keep a pull request to one change, and describe it in a way that can go
  straight into [CHANGELOG.md](CHANGELOG.md). The changelog itself is updated
  when a release is cut.
- Report security problems privately, as [SECURITY.md](SECURITY.md) describes,
  not in an issue.

## Licence

Millrace is MIT-licensed (see [LICENSE](LICENSE)). By contributing, you agree
that your contribution is licensed under the same terms.
