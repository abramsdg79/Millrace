# Millrace Rename Implementation Plan (plan 9)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the product from DSE to Millrace everywhere in the tree —
solution, projects, folders, namespaces, assemblies, the `millrace` command,
`MRnnn` diagnostic codes, `MILLRACE_UPDATE_GOLDEN`, Docker and FUXA names,
user docs and the history documents — and ship it as version 1.1.0, with no
tick, event, value or register address changing.

**Architecture:** One Python script, `.superpowers/sdd/9/rename.py`
(git-ignored, written in Task 1 and reused by every later task), holds the
whole case-sensitive mapping and the checks: it moves paths with `git mv`,
rewrites tracked text files, verifies every regenerated golden against the
mapped old golden, reports how git pairs the renames, counts the
false-positive identifiers and lists every line that still names the old
product. Task 2 is one atomic commit (code, tests, samples, user docs and the
HMI together), because a half-renamed solution does not build and the tests
read the READMEs, the docs pages and the FUXA project. Task 3 makes the
1.1.0 release, Task 4 rewrites the history documents, Task 5 is the
acceptance sweep.

**Tech Stack:** .NET 10 (`net10.0`, SDK 10.0.401), C#, xUnit 2.9.3; Python
3 (stdlib only, run as `python3 -I`); Docker 29.7 with Compose for the HMI
smoke check (available on this machine — measured with `docker info`). No
package added or changed.

**Spec:** `docs/superpowers/specs/2026-10-08-millrace-rename-design.md`
(all of it; every decision in it is the owner's). Where this plan departs
from it, the section below says so and why.

**Plan sequence:** This is plan 9. Plans 1–8 are merged on `master`; this
plan starts from the commit that adds the rename spec and this plan (the
"plan commit"), on top of `28291b2`. Measured on `28291b2` with
`dotnet test Dse.sln`: **1718 tests**, all passing — 37
`Dse.Io.Abstractions` / 498 `Dse.Core` / 189 `Dse.Components` / 57
`Dse.Realtime` / 239 `Dse.Configuration` / 170 `Dse.Scenarios` / 109
`Dse.Cli` / 153 `Dse.Control` / 27 `Dse.Control.Catalogue` / 95 `Dse.Modbus`
/ 144 `Dse.Samples`. Every number in this plan was measured by running the
script below, the builds, the tests and the Docker smoke check on a scratch
worktree of `28291b2` plus a simulated plan commit.

## Spec deviations and findings

Each was measured; the plan is built around the measurement.

1. **Fixture file names carry diagnostic codes.** 36 tracked files are named
   after a code — `tests/Dse.Configuration.Tests/Plants/invalid/DSE013-scan-period-off-the-step.json`
   (29 of them) and `tests/Dse.Scenarios.Tests/Scenarios/{invalid,unrunnable}/DSE2nn-*.json`
   (7). The spec's mapping names only `Dse.` paths. The tests take each
   fixture's code from its file name, so these must move too
   (`MR013-scan-period-off-the-step.json`). The script's `move` renames every
   path component the mapping changes: 23 folders, then 60 files (23
   `.csproj`, the `.sln`, the 36 fixtures).
2. **Three tests slice a code by its old width.** `((string)row[0])[..6]` in
   `tests/…Configuration.Tests/CorpusTests.cs:51` and
   `tests/…Scenarios.Tests/CorpusTests.cs:124`, and `name[..6]` in
   `tests/…Configuration.Tests/SchemaAgreementTests.cs:47`, read `DSE013`
   from `DSE013-….json`; on `MR013-….json` they read `MR013-` and **11 tests
   fail** (`EveryConfigurationCodeHasAnInvalidPlant`,
   `EveryScenarioCodeHasAFixture` and 9 cases of
   `TheSchemaRejectsStructuralErrorsAndOnlyThose`). Task 2 changes the three
   `6`s to `5` by hand — the only edits in Task 2 that are not the mapping.
   No other code in `src/` or `tests/` assumes a code's width (searched:
   `PadRight`/`PadLeft`, format widths, regexes, `Substring`, slices).
3. **No padded column shrinks.** The spec expects output columns padded to a
   code's width to shrink by one character. None does: every one of the 26
   goldens regenerates to exactly the mapped old golden. 6 change (the two
   generated diagnostics pages, the components and control catalogues, the
   plant schema, the FUXA project); the 20 event-log goldens do not name the
   product and do not change at all.
4. **No `#dse013`-style anchors exist.** Nothing in the tree links a
   diagnostic anchor. The generated pages' headings (`## DSE013 — …`) become
   `## MR013 — …` through the mapping, so their implicit GitHub anchors follow.
   The script keeps a `#dse013 → #mr013` rule anyway; it matches nothing.
5. **`ReleaseTests` pins 1.0.0.** `tests/Dse.Samples.Tests/ReleaseTests.cs`
   asserts `<Version>1.0.0</Version>`, `**Version 1.0.0.**` in the README and
   a changelog whose headings are `## Unreleased` and `## 1.0.0 — …`. The
   spec is silent; Task 3 changes the test (same two facts, so the count stays
   **1718**) to require `1.1.0`, `<Product>Millrace</Product>`, and the
   headings `## 1.1.0 — …`, `## 1.0.0 — 2026-10-07`.
6. **Git's rename detection cannot hold for every file while every commit
   builds.** The spec asks for both. Measured on the Task 2 commit (`rename.py
   renames`): of 560 moved paths, **551** pair old → new correctly and **9**
   do not. Five small files that are mostly the old name show as a delete
   plus a create: `Millrace.Cli.csproj`, `Millrace.Cli/Program.cs`,
   `Millrace.Configuration.csproj`, `Millrace.Modbus.csproj` (created),
   `Dse.Scenarios.csproj` (deleted) and
   `tests/Millrace.Core.Tests/ScaffoldingTests.cs`. Four are paired with the
   wrong file because they resemble each other:
   `Dse.Modbus.csproj → Millrace.Control.Catalogue.csproj`,
   `Dse.Control.Catalogue.csproj → Millrace.Scenarios.csproj`, and the two
   near-identical `Fakes/TestContexts.cs` of `Core.Tests` and
   `Components.Tests`, swapped. `git log --follow` on those nine shows no
   history or another file's. A commit that only moves files would pair all
   560 but would not build. This plan keeps **every commit builds** and
   records the nine; the controller should tell the owner before Task 2 (the
   alternative is one non-building, move-only commit first).
7. **The env var lives in three places, not in `GoldenLog.cs`.**
   `DSE_UPDATE_GOLDEN` is read by `tests/Shared/Golden.cs` and
   `tests/Dse.Samples.Tests/Sample.cs`, and named in the generated pages'
   header comments (`DiagnosticsReference.cs`,
   `ScenarioDiagnosticsReference.cs`) and docs. `src/Dse.Scenarios/GoldenLog.cs`
   holds no name. All are covered by the mapping.
8. **The false positives are wider than the spec's list, and all safe.** Every
   lowercase `dse` in the tree is a whole word (none sits inside an
   identifier). Case-insensitive `dse` across a word joint occurs **205**
   times in 26 identifiers (the spec's eight plus `TryReadSeconds`,
   `ReadSeed`, `AnOutletFeedsExactlyOneInlet`, …); the case-sensitive rules
   touch none of them, and `counts` proves it after each task.
9. **Absolute paths:** exactly three, all in plans:
   `cd /home/keeper/…/POCs/DSE` twice (plans 1 and 3) becomes
   `# from the repository root`, and `mv /home/keeper/…/POCs/DSE/.playwright-mcp …`
   (plan 8) becomes `mv .playwright-mcp …`.
10. **A catalogue module built against 1.0.0 stops loading.** Renaming
    assemblies and namespaces breaks `--assembly` modules compiled against
    `Dse.Core`. Measured: `millrace catalog export --assembly <1.0.0-built
    Dse.Cli.Tests.SampleModule.dll>` exits 3 with "… contains no catalogue
    module …" — a clean refusal, not a crash. Under Semantic Versioning that
    is a breaking change (2.0.0); the spec chose 1.1.0 and this plan keeps it,
    and the changelog says such a module must be rebuilt. The owner may want
    to reconsider the number.
11. **Docker is available here, and the old stack's data exists.** `docker
    info` succeeds; images `dse-hmi/dse:local` and `dse-hmi/fuxa:1.3.4-modbus`
    and volumes `dse-hmi_fuxa-{appdata,db,logs}` are present. The renamed
    compose project `millrace-hmi` starts with fresh volumes and passed
    `smoke-check.sh` in the scratch run (CV001.Speed 1.83 m/s after the start
    sequence). The plan never touches the `dse-hmi` images or volumes; they
    are the owner's to remove.
12. **Prose is not reflowed.** `Dse` → `Millrace` lengthens lines by five
    characters, so some Markdown lines pass 78 columns. Reflowing would make
    the diff more than the mapping; the plan leaves them.

## Global Constraints

- **Every rule is case-sensitive** and lives in the script, nowhere else:
  `DSE_UPDATE_GOLDEN` → `MILLRACE_UPDATE_GOLDEN`; `DSE` + a digit + two
  digits-or-`x` → `MR` + the same (`DSE016` → `MR016`, `DSE1xx` → `MR1xx`);
  the word `DSE` → `Millrace`; `Dse` + three digits → `Mr` + the digits
  (`IsDse015` → `IsMr015`); any other `Dse` → `Millrace`; the word `dse` →
  `millrace`. No case-insensitive or hand-typed replacement, ever.
- **Only these lines may still name the old product** after Task 4 (the spec's
  list): the rename spec and this plan (the script's `SKIP`), `CHANGELOG.md`'s
  1.1.0 entry, and the one README line `Millrace was called DSE up to 1.0.0.`
  `python3 -I .superpowers/sdd/9/rename.py residue` enforces exactly this.
- **The false-positive counts never change** (baseline below, from
  `counts`): SpeedSensor 41, ItemIdSequence 46, elapsedSeconds 17,
  ElapsedSeconds 13, holdSeconds 12, AddSeconds 10, TimedSeconds 8, SortedSet
  8, mixed-case dse 205.
- **Goldens are regenerated, never hand-edited, and equal the mapped old
  golden byte for byte.** The script's `apply` skips every golden (the 15
  `samples/*/expected/*.log`, every file under a `Golden/` folder, the two
  generated `docs/*-diagnostics.md` pages and `hmi/fuxa/mine-conveyors.fuxap.json`
  — 26 files). They are regenerated with `MILLRACE_UPDATE_GOLDEN=1` (and the
  FUXA project with `generate-project.py`), **read**, and checked with
  `rename.py goldens <BASE>`, which must print `0 differ`. If a golden
  differs, report the diff the script prints; never edit a golden, a test or
  the mapping to make it pass without saying so.
- **Report every measurement.** Where a number in this plan (a file count, a
  test count, a failing-test list, a script line) disagrees with what you
  measure, report the measured value; never widen an expectation to fit.
- **Warnings are errors** (`Directory.Build.props`). After every task:
  `dotnet build Millrace.sln -c Release --nologo` prints `0 Warning(s)` and
  `0 Error(s)`, and `dotnet test Millrace.sln --nologo` passes **1718** tests
  (before Task 2 the solution is `Dse.sln`).
- **Moves use `git mv`** (the script's `move` runs it); a moved file's content
  changes only by the mapping, except the three code-width edits of Task 2.
- **Markdown and C# files** keep LF line endings and a final newline; the
  script preserves bytes it does not map (it reads and writes UTF-8 with
  `newline=""`).
- **Git, for every task.** One git command per `Bash` call. Stage with the
  exact command the task shows (`git add -u` in Tasks 2–4: no task creates a
  tracked file); never `git add -A`, never `git add .`, **never `git
  stash`**. Commit messages are conventional (`refactor: …`), a subject line,
  a blank line, a body wrapped at about 78 columns, a blank line and the
  trailer as the last line, copied verbatim:

  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  ```

  The trailer identifies the **session** that planned and drives this work,
  not the model that implements a given task: it is the same on every commit.
  Never put it on the subject line. Write each message with the Write tool to
  `.superpowers/sdd/9/msg-taskN.txt` and commit with
  `git commit -F .superpowers/sdd/9/msg-taskN.txt`; then run
  `git log -1 --format='%h%n%s%n--%n%b'` and check the subject, the blank
  line and the trailer.
- **Harness quirks inside a worktree.** The Bash tool refuses a command whose
  text contains "Github" (the repository's absolute path does), or git text
  inside a heredoc, `$(…)`, a variable or a loop. Use repo-relative paths
  only, run each command exactly as the plan shows it from the repository
  root, one git command per call, and write files with the Write tool. The
  script runs git itself (`subprocess`), so `python3 -I
  .superpowers/sdd/9/rename.py move` is allowed. `.superpowers/` is
  git-ignored: the script, messages and scratch files live in
  `.superpowers/sdd/9/` and are never added. A command that greps for
  nothing exits 1; read the output, do not chain it with `&&`.

## Review Focus

The five failure modes the spec implies but no existing test exercises, most
likely first. Each has its check in the owning task.

1. **An old name surviving where no test looks** — a comment, a Dockerfile
   line, a compose key, a doc sentence, a history plan. A reader expects no
   `DSE`, `Dse` or word `dse` outside the allowed lines. Check:
   `rename.py residue . ':!docs/superpowers'` prints `0 lines not allowed`
   (Task 2, Task 3); `rename.py residue` over the whole tree prints
   `11 allowed lines, 0 lines not allowed` (Task 4, Task 5).
2. **A real identifier corrupted by the rename** (`SpeedSensor`,
   `ItemIdSequence`, `ReadSeed`, …). A reader expects them untouched. Check:
   `rename.py selftest` (its `unchanged` cases, Task 1) and `rename.py counts`
   equal to the baseline after Tasks 2, 3, 4 and 5.
3. **Code that assumes a code is six characters wide.** A test or tool that
   slices, pads or matches `DSEnnn` by width silently mis-reads `MRnnn`.
   Check: Task 2's three edits, its red run (exactly the 9 golden-related
   failures and no corpus failure), and `git grep -n -E '\[\.\.6\]' -- src tests`
   printing nothing.
4. **A plugin module built against 1.0.0 given to `millrace --assembly`.** A
   plugin author expects a clear refusal and exit code 3, not a crash or a
   stack trace. Check: Task 5 step 8 builds the 1.0.0 sample module from the
   plan commit and loads it (measured: exit 3, "… contains no catalogue
   module …").
5. **A machine that ran the 1.0.0 HMI.** Its `dse-hmi` volumes hold a FUXA
   project that polls `dse:5020`; a user expects `docker compose up --build`
   of the renamed stack to load the renamed project and run. Check: Task 5
   step 9 (when Docker is available): the `millrace-hmi` project starts with
   its own volumes, `fuxa-init` logs "Loaded the mine-conveyors project into
   FUXA.", and `smoke-check.sh` prints `PASS`.

## Measurements

Measured on a scratch worktree of `28291b2` with the rename spec and a plan
file committed on top (the plan commit), running exactly the commands in
Tasks 1–4.

| check | before Task 2 | after Task 2 | after Task 3 | after Task 4 |
|---|---|---|---|---|
| `counts`: SpeedSensor / ItemIdSequence / elapsedSeconds / ElapsedSeconds | 41 / 46 / 17 / 13 | same | same | same |
| `counts`: holdSeconds / AddSeconds / TimedSeconds / SortedSet | 12 / 10 / 8 / 8 | same | same | same |
| `counts`: mixed-case dse | 205 | 205 | 205 | 205 |
| `counts`: Dse / DSE / word dse | 8009 / 1640 / 656 | 6113 / 1160 / 454 | 6116 / 1170 / 460 | 3 / 10 / 6 |
| `counts`: paths to rename | 560 | 0 | 0 | 0 |
| `residue` (whole tree) | 8661 not allowed | (history only) | (history only) | 11 allowed, 0 not allowed |
| tests | 1718 | 1718 | 1718 | 1718 |

(`counts` and `residue` skip the rename spec and this plan.)

- **Task 2:** `move` — 23 folders and 60 files; `apply . ':!docs/superpowers'`
  — 506 files rewritten, 26 goldens skipped; 3 hand edits; the commit is 582
  files, 2454 insertions and 2454 deletions: 555 R (65 of them unchanged
  content), 17 M, 5 A, 5 D. Before regeneration 9 tests fail (5 golden-file
  tests, 4 FUXA-project tests); after it all 1718 pass. `goldens` — 26
  checked, 0 differ, 6 changed by the mapping only.
- **Task 3:** 4 files, 50 insertions, 24 deletions; red run 1 failed of 2.
- **Task 4:** `apply docs/superpowers` — 29 files, 6260 lines changed in each
  direction.
- **Task 5:** README's `--expect` runs: `pull-key.log (99 events)`,
  `slow-press.log (267 events)`; `git log --follow --oneline --
  src/Millrace.Core/SimulationBuilder.cs` lists 13 commits, the same 13 as
  `git log --oneline -- src/Dse.Core/SimulationBuilder.cs` at the Task 2
  commit; HMI smoke `PASS: CV001.Speed = 1.83… m/s`.

## File structure

- `.superpowers/sdd/9/rename.py` — created in Task 1, git-ignored, never
  committed. Every later task runs it.
- Task 2 touches every tracked path outside `docs/superpowers/` that names the
  product: `Dse.sln` → `Millrace.sln`; `src/Dse.*/` → `src/Millrace.*/` (10
  projects) and `tests/Dse.*/` → `tests/Millrace.*/` (13 projects) with their
  `.csproj`; the 36 code-named fixtures; and in place `CHANGELOG.md`,
  `README.md`, `docs/{architecture,authoring-a-component,configuration-diagnostics,control-blocks,scenario-diagnostics,scenarios}.md`,
  `hmi/fuxa/{Dockerfile,README.md,docker-compose.yml,generate-project.py,mine-conveyors.fuxap.json}`,
  `samples/{mine-conveyors,wheel-line}/README.md`,
  `tests/Shared/{Golden.cs,ModbusClient.cs}`. Untouched: `Directory.Build.props`,
  `LICENSE`, `.gitignore`, `hmi/fuxa/{fuxa.Dockerfile,Dockerfile.dockerignore,smoke-check.sh}`,
  every plant and scenario JSON under `samples/`, every event-log golden.
- Task 3: `Directory.Build.props`, `CHANGELOG.md`, `README.md`,
  `tests/Millrace.Samples.Tests/ReleaseTests.cs`.
- Task 4: the 29 files under `docs/superpowers/` other than the rename spec
  and this plan.

## Task map

- **Task 1 — the mapping script.** Write `rename.py`, run its self-test and
  take the baseline counts. No commit (the file is git-ignored).
- **Task 2 — the code rename.** Move, map, three width edits, regenerate every
  golden and the FUXA project, verify, commit. One commit, because nothing
  in between builds.
- **Task 3 — the 1.1.0 release.** `ReleaseTests` first (red), then the
  version, the product name, the changelog's 1.1.0 entry and the README's
  status and its one allowed sentence.
- **Task 4 — the history documents.** Map `docs/superpowers/` (except the
  rename spec and plan), absolute paths included.
- **Task 5 — acceptance.** Every spec acceptance item, the README's commands,
  the 1.0.0-module check and the Docker smoke check. No commit unless a check
  fails and the controller rules on a fix.

---

### Task 1: The mapping script

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Create: `.superpowers/sdd/9/rename.py` (git-ignored; not committed)

**Interfaces:**
- Consumes: nothing.
- Produces: the commands every later task runs, from the repository root:
  `python3 -I .superpowers/sdd/9/rename.py selftest | counts | move |
  apply <pathspec>... | goldens <BASE> | renames [<commit>] | residue [<pathspec>...]`.
  `apply` and `residue` take git pathspecs (`.`, `docs/superpowers`,
  `':!docs/superpowers'`). `goldens` exits 1 when any golden differs;
  `residue` exits 1 when any line is not allowed; `move` exits 1 when a
  tracked path would still change under the mapping.

- [ ] **Step 1: Write the script**

Create `.superpowers/sdd/9/rename.py` with the Write tool, exactly:

```python
"""The DSE -> Millrace mapping (plan 9), one deterministic source for every task.

Run from the repository root, always as `python3 -I .superpowers/sdd/9/rename.py <command>`:

  selftest            check the mapping against known cases; exit 1 on a failure
  counts              print the false-positive and residue counts for the tracked tree
  move                git mv every tracked path the mapping renames (folders first)
  apply <path>...     rewrite the tracked text files under each path (a file or a folder)
  goldens <base>      check each regenerated golden against the mapped golden at <base>
  renames [<commit>]  count git's view of the staged change (or of <commit>) per status
                      letter and list each path it does not pair old -> mapped(old)
  residue [<path>...] list every tracked line still naming DSE, Dse or the word dse
                      (or holding a /home/ path); exit 1 unless each is one the spec allows

Every rule is case-sensitive. The order matters: the specific rules run first.
"""
import difflib
import os
import re
import subprocess
import sys

ROOT_PATH = "/home/keeper/Work/" + "Github" + "/POCs/DSE"

RULES = [
    # Absolute paths to the owner's checkout (three, all in docs/superpowers/plans).
    (re.compile(r"^cd " + re.escape(ROOT_PATH) + r"$", re.M), "# from the repository root"),
    (re.compile(re.escape(ROOT_PATH) + r"/"), ""),
    (re.compile(re.escape(ROOT_PATH)), "."),
    # The golden update switch.
    (re.compile(r"DSE_UPDATE_GOLDEN"), "MILLRACE_UPDATE_GOLDEN"),
    # A diagnostic code or code family: DSE016, DSE1xx, DSE20x, DSE999.
    (re.compile(r"DSE(?=[0-9][0-9x]{2})"), "MR"),
    # The product, in prose and comments.
    (re.compile(r"\bDSE\b"), "Millrace"),
    # A diagnostic number inside an identifier: IsDse015 -> IsMr015.
    (re.compile(r"Dse(?=[0-9]{3})"), "Mr"),
    # Namespaces, projects, assemblies, folders, and the product word in identifiers.
    (re.compile(r"Dse"), "Millrace"),
    # A documentation anchor: #dse013 -> #mr013.
    (re.compile(r"\bdse(?=[0-9]{3}\b)"), "mr"),
    # The command, the Docker names, temp-file prefixes, the dispatcher thread.
    (re.compile(r"\bdse\b"), "millrace"),
]

# Files the mapping never touches: they describe the rename itself.
SKIP = {
    "docs/superpowers/specs/2026-10-08-millrace-rename-design.md",
    "docs/superpowers/plans/2026-10-08-millrace-rename.md",
}

# Generated files: regenerated by the code or by generate-project.py, never
# rewritten by this script. Also every tracked file under a Golden/ folder and
# under each GOLDEN_DIRS folder.
GOLDENS = [
    "docs/configuration-diagnostics.md",
    "docs/scenario-diagnostics.md",
    "hmi/fuxa/mine-conveyors.fuxap.json",
]
GOLDEN_DIRS = ["samples/mine-conveyors/expected/", "samples/wheel-line/expected/"]

FALSE_POSITIVES = ["SpeedSensor", "ItemIdSequence", "elapsedSeconds", "ElapsedSeconds",
                   "holdSeconds", "AddSeconds", "TimedSeconds", "SortedSet"]


WORD_DSE = re.compile(r"\bdse\b")


def map_text(text):
    for pattern, replacement in RULES:
        text = pattern.sub(replacement, text)
    return text


def tracked(*paths):
    out = subprocess.run(["git", "ls-files", "-z", "--", *paths], check=True, capture_output=True).stdout
    return [p for p in out.decode().split("\0") if p]


def is_golden(path):
    return path in GOLDENS or "/Golden/" in path or any(path.startswith(d) for d in GOLDEN_DIRS)


def read(path):
    with open(path, "rb") as f:
        data = f.read()
    if b"\0" in data:
        return None
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return None


def selftest():
    cases = [
        ("namespace Dse.Core.Flow;", "namespace Millrace.Core.Flow;"),
        ('<ProjectReference Include="..\\Dse.Core\\Dse.Core.csproj" />', '<ProjectReference Include="..\\Millrace.Core\\Millrace.Core.csproj" />'),
        ("<AssemblyName>dse</AssemblyName>", "<AssemblyName>millrace</AssemblyName>"),
        ("public void AClaimNamingNoTagIsDse016()", "public void AClaimNamingNoTagIsMr016()"),
        ("ASubTickStepFromThePlantsDefaultsIsDse205ThenDse103BeforeBuilding", "ASubTickStepFromThePlantsDefaultsIsMr205ThenMr103BeforeBuilding"),
        ("OnlyDse016(diagnostics)", "OnlyMr016(diagnostics)"),
        ("DseValidateNamesTheNearestTag", "MillraceValidateNamesTheNearestTag"),
        ("TheDeviceIsTheDseServerPolledEvery200Milliseconds", "TheDeviceIsTheMillraceServerPolledEvery200Milliseconds"),
        ("EveryPublicScanBlockInDseControlHasADescriptor", "EveryPublicScanBlockInMillraceControlHasADescriptor"),
        ("error DSE016 at controllers[0]", "error MR016 at controllers[0]"),
        ("DSE1xx and DSE0xx and DSE20x, DSE999", "MR1xx and MR0xx and MR20x, MR999"),
        ("## DSE100 — The file is not valid JSON", "## MR100 — The file is not valid JSON"),
        ("DSE's first external consumer", "Millrace's first external consumer"),
        ('"title": "DSE plant"', '"title": "Millrace plant"'),
        ("DSE_UPDATE_GOLDEN=1 dotnet test", "MILLRACE_UPDATE_GOLDEN=1 dotnet test"),
        ("Run `dse help` to list the commands.", "Run `millrace help` to list the commands."),
        ("--out dse-plant.schema.json", "--out millrace-plant.schema.json"),
        ('$"dse-run-{Guid.NewGuid():N}"', '$"millrace-run-{Guid.NewGuid():N}"'),
        ('string name = "dse-dispatcher"', 'string name = "millrace-dispatcher"'),
        ("name: dse-hmi", "name: millrace-hmi"),
        ("image: dse-hmi/dse:local", "image: millrace-hmi/millrace:local"),
        ('ENTRYPOINT ["/app/dse"]', 'ENTRYPOINT ["/app/millrace"]'),
        ("WORKDIR /dse", "WORKDIR /millrace"),
        ('"address": "dse:5020"', '"address": "millrace:5020"'),
        ("dotnet build Dse.sln", "dotnet build Millrace.sln"),
        ("see [DSE013](configuration-diagnostics.md#dse013)", "see [MR013](configuration-diagnostics.md#mr013)"),
        ("cd " + ROOT_PATH, "# from the repository root"),
        ("mv " + ROOT_PATH + "/.playwright-mcp x", "mv .playwright-mcp x"),
    ]
    unchanged = [
        "SpeedSensor ItemIdSequence elapsedSeconds ElapsedSeconds _holdSeconds",
        "AddSeconds TimedSeconds SortedSet TryReadSeconds ReadSeed",
        "EveryInvalidPlantYieldsExactlyTheCodeInItsName AnOutletFeedsExactlyOneInlet",
        "MalformedServeInvocationsAreUsageErrors BuildsEveryObjectDescriptor",
    ]
    failures = 0
    for old, new in cases + [(u, u) for u in unchanged]:
        got = map_text(old)
        if got != new:
            failures += 1
            print(f"FAIL: {old!r}\n  expected {new!r}\n  got      {got!r}")
    print(f"selftest: {len(cases) + len(unchanged)} cases, {failures} failed")
    return 1 if failures else 0


def counts():
    texts = [t for t in (read(p) for p in tracked() if p not in SKIP and os.path.isfile(p)) if t is not None]
    for word in FALSE_POSITIVES:
        print(f"{word:16} {sum(t.count(word) for t in texts)}")
    mixed = sum(len(re.findall(r"(?i)dse", t)) - t.count("dse") - t.count("Dse") - t.count("DSE") for t in texts)
    print(f"{'mixed-case dse':16} {mixed}")
    print(f"{'Dse':16} {sum(t.count('Dse') for t in texts)}")
    print(f"{'DSE':16} {sum(t.count('DSE') for t in texts)}")
    print(f"{'word dse':16} {sum(len(WORD_DSE.findall(t)) for t in texts)}")
    print(f"{'paths to rename':16} {sum(map_text(p) != p for p in tracked())}")
    return 0


def move():
    """Folders first (the shallowest component the mapping changes), then files."""
    moves = []
    for path in tracked():
        parts = path.split("/")
        for i, part in enumerate(parts[:-1]):
            if map_text(part) != part:
                pair = ("/".join(parts[: i + 1]), "/".join(parts[:i] + [map_text(part)]))
                if pair not in moves:
                    moves.append(pair)
                break
    for old, new in moves:
        subprocess.run(["git", "mv", old, new], check=True)
    files = [p for p in tracked() if map_text(p.split("/")[-1]) != p.split("/")[-1]]
    for path in files:
        head, name = os.path.split(path)
        subprocess.run(["git", "mv", path, os.path.join(head, map_text(name))], check=True)
    left = [p for p in tracked() if map_text(p) != p]
    print(f"move: {len(moves)} folders and {len(files)} files moved; {len(left)} tracked paths still change under the mapping")
    return 1 if left else 0


def apply(paths):
    changed = 0
    skipped = []
    for path in tracked(*paths):
        if path in SKIP or is_golden(path):
            skipped.append(path)
            continue
        text = read(path)
        if text is None:
            continue
        new = map_text(text)
        if new != text:
            with open(path, "w", encoding="utf-8", newline="") as f:
                f.write(new)
            changed += 1
    print(f"apply: {changed} files rewritten; skipped {len(skipped)} (rename spec/plan and goldens)")
    return 0


def golden_files():
    return [p for p in tracked() if is_golden(p)]


def old_path(path):
    return path.replace("Millrace.", "Dse.")  # no golden's file name holds a code


def goldens(base):
    bad = 0
    files = golden_files()
    for path in files:
        old = subprocess.run(["git", "show", f"{base}:{old_path(path)}"], check=True, capture_output=True).stdout.decode()
        with open(path, encoding="utf-8", newline="") as f:
            now = f.read()
        expected = map_text(old)
        if expected == now:
            print(f"OK    {path}" + ("" if old == now else "  (changed by the mapping only)"))
        else:
            bad += 1
            print(f"DIFF  {path}")
            sys.stdout.writelines(difflib.unified_diff(expected.splitlines(True), now.splitlines(True), "mapped-old", "regenerated", n=1))
    print(f"goldens: {len(files)} checked, {bad} differ from the mapped old golden")
    return 1 if bad else 0


def renames(commit):
    """How git's rename detection sees the staged change (no argument) or a commit:
    a count per status letter, then every path git does not pair old -> mapped(old)."""
    span = ["--cached"] if commit is None else [f"{commit}~1", commit]
    out = subprocess.run(["git", "diff", "--name-status", "-M", *span], check=True, capture_output=True).stdout.decode()
    letters = {}
    odd = []
    for line in out.splitlines():
        fields = line.split("\t")
        letter = fields[0][0]
        letters[letter] = letters.get(letter, 0) + 1
        if letter in "AD" or (letter == "R" and map_text(fields[1]) != fields[2]):
            odd.append(line)
    print("renames: " + ", ".join(f"{k} {v}" for k, v in sorted(letters.items())))
    for line in odd:
        print("  not paired by the mapping: " + line.replace("\t", "  "))
    return 0


ALLOWED_README_LINE = "Millrace was called DSE up to 1.0.0."


def residue(paths):
    """Every tracked line still naming DSE, Dse or the word dse, or holding an
    absolute /home/ path, split into the spec's allowed lines and the rest.
    Exit 1 when any line is not allowed."""
    allowed = bad = 0
    for path in tracked(*paths):
        if path in SKIP:
            continue
        text = read(path)
        if text is None:
            continue
        in_110 = False
        for n, line in enumerate(text.split("\n"), 1):
            if path == "CHANGELOG.md" and line.startswith("## "):
                in_110 = line.startswith("## 1.1.0 ")
            if "DSE" in line or "Dse" in line or WORD_DSE.search(line) or "/home/" in line:
                ok = (path == "README.md" and line == ALLOWED_README_LINE) or (path == "CHANGELOG.md" and in_110)
                allowed += ok
                bad += not ok
                print(f"{'allowed' if ok else 'NOT ALLOWED'}  {path}:{n}: {line[:150]}")
    print(f"residue: {allowed} allowed lines, {bad} lines not allowed")
    return 1 if bad else 0


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "selftest":
        sys.exit(selftest())
    if command == "counts":
        sys.exit(counts())
    if command == "move":
        sys.exit(move())
    if command == "apply" and len(sys.argv) > 2:
        sys.exit(apply(sys.argv[2:]))
    if command == "goldens" and len(sys.argv) == 3:
        sys.exit(goldens(sys.argv[2]))
    if command == "renames" and len(sys.argv) <= 3:
        sys.exit(renames(sys.argv[2] if len(sys.argv) == 3 else None))
    if command == "residue":
        sys.exit(residue(sys.argv[2:]))
    print(__doc__)
    sys.exit(2)
```

- [ ] **Step 2: Run the self-test**

Run: `python3 -I .superpowers/sdd/9/rename.py selftest`
Expected: `selftest: 32 cases, 0 failed` and exit 0.

- [ ] **Step 3: Show the self-test catches a wrong rule**

Copy the script to `.superpowers/sdd/9/rename-mutant.py` with the Write tool,
changing only the line `    (re.compile(r"Dse"), "Millrace"),` to
`    (re.compile(r"(?i)dse"), "Millrace"),` — the case-insensitive replace the
spec forbids — then run
`python3 -I .superpowers/sdd/9/rename-mutant.py selftest`.
Expected (measured): exit 1, last line `selftest: 32 cases, 15 failed`, and
among the `FAIL:` lines every `unchanged` case (for example `SpeedSensor` →
`SpeMillracensor`). Delete the mutant:
`rm .superpowers/sdd/9/rename-mutant.py`.

- [ ] **Step 4: Take the baseline counts**

Run: `python3 -I .superpowers/sdd/9/rename.py counts`
Expected, exactly:

```
SpeedSensor      41
ItemIdSequence   46
elapsedSeconds   17
ElapsedSeconds   13
holdSeconds      12
AddSeconds       10
TimedSeconds     8
SortedSet        8
mixed-case dse   205
Dse              8009
DSE              1640
word dse         656
paths to rename  560
```

Run: `python3 -I .superpowers/sdd/9/rename.py residue . ':!docs/superpowers'`
Expected: exit 1, last line `residue: 0 allowed lines, 2401 lines not allowed`
(every product name outside the history documents; Task 2 removes them).

- [ ] **Step 5: Report**

No commit: `git status --short` prints nothing (`.superpowers/` is ignored).
Report the self-test line, the mutant's exit code and the two outputs of
Step 4.

---

### Task 2: The code rename

**Model:** implementer sonnet; reviewer opus.

**Files:** (measured; 582 paths in the commit)
- Move (`git mv`, by the script): `Dse.sln`; the 23 folders `src/Dse.*`,
  `tests/Dse.*` with their `.csproj`; the 36 fixtures
  `tests/Dse.Configuration.Tests/Plants/invalid/DSEnnn-*.json`,
  `tests/Dse.Scenarios.Tests/Scenarios/invalid/DSE20n-*.json`,
  `tests/Dse.Scenarios.Tests/Scenarios/unrunnable/DSE20n-*.json`.
- Modify by the mapping: 506 files (moved and in place; see File structure).
- Modify by hand: `tests/Millrace.Configuration.Tests/CorpusTests.cs:51`,
  `tests/Millrace.Scenarios.Tests/CorpusTests.cs:124`,
  `tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs:47`.
- Regenerate: the 26 goldens (see Global Constraints).

**Interfaces:**
- Consumes: `rename.py` (Task 1).
- Produces: `Millrace.sln`; projects and assemblies `Millrace.<Area>`; the
  executable `src/Millrace.Cli/bin/<Configuration>/net10.0/millrace`; the
  env var `MILLRACE_UPDATE_GOLDEN`; `BASE`, the plan commit's short hash,
  which Task 5 reuses.

- [ ] **Step 1: Record BASE and check the starting point**

Run: `git rev-parse --short HEAD`
Write the hash it prints in your report as **BASE**; every later command in
this task and in Task 5 that says `BASE` takes this literal hash. It is the
plan commit (`git log -1 --format=%s` names the plan).

Run: `dotnet build Dse.sln -c Release --nologo` — expect `0 Warning(s)`,
`0 Error(s)`.
Run: `dotnet test Dse.sln --nologo` — expect 1718 passed, 0 failed.

- [ ] **Step 2: Remove build output**

`git mv` of a folder carries its ignored `bin/` and `obj/` along, with stale
project paths in them. Run:
`find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +`
Then
`find src tests -type d -name obj` prints nothing.

- [ ] **Step 3: Move the paths**

Run: `python3 -I .superpowers/sdd/9/rename.py move`
Expected: `move: 23 folders and 60 files moved; 0 tracked paths still change under the mapping`, exit 0.

- [ ] **Step 4: Apply the mapping outside the history documents**

Run: `python3 -I .superpowers/sdd/9/rename.py apply . ':!docs/superpowers'`
Expected: `apply: 506 files rewritten; skipped 26 (rename spec/plan and goldens)`.

- [ ] **Step 5: Fix the three code-width slices**

With the Edit tool:

In `tests/Millrace.Configuration.Tests/CorpusTests.cs` replace
```csharp
        var covered = Corpus.Invalid().Select(row => ((string)row[0])[..6]).ToHashSet(StringComparer.Ordinal);
```
with
```csharp
        var covered = Corpus.Invalid().Select(row => ((string)row[0])[..5]).ToHashSet(StringComparer.Ordinal);
```

In `tests/Millrace.Scenarios.Tests/CorpusTests.cs` replace
```csharp
            .Select(row => ((string)row[0])[..6])
```
with
```csharp
            .Select(row => ((string)row[0])[..5])
```

In `tests/Millrace.Configuration.Tests/SchemaAgreementTests.cs` replace
```csharp
        string code = name[..6];
```
with
```csharp
        string code = name[..5];
```

Run: `git grep -n -E '\[\.\.6\]' -- src tests`
Expected: no output (exit 1).

- [ ] **Step 6: Build**

Run: `dotnet build Millrace.sln -c Release --nologo`
Expected: `0 Warning(s)`, `0 Error(s)`. Then `ls src/Millrace.Cli/bin/Release/net10.0/millrace`
lists the file (the executable is `millrace`, from `<AssemblyName>millrace</AssemblyName>`).

- [ ] **Step 7: Run the tests before regenerating (red)**

Run: `dotnet test Millrace.sln --nologo`
Expected: 1718 total, **exactly 9 failed**, all because a golden still holds
the old names:

- `Millrace.Control.Catalogue.Tests.ControlCatalogueTests.TheControlCatalogueExportsExactlyTheGoldenFile`
- `Millrace.Components.Tests.Catalogue.ComponentsExportTests.TheShippedCatalogueExportsExactlyTheGoldenFile`
- `Millrace.Configuration.Tests.DiagnosticsReferenceTests.TheCommittedReferencePageIsCurrent`
- `Millrace.Configuration.Tests.PlantSchemaTests.MatchesTheGoldenFile`
- `Millrace.Scenarios.Tests.ScenarioDiagnosticsReferenceTests.TheCommittedReferencePageIsCurrent`
- `Millrace.Samples.Tests.FuxaProjectTests.EveryTagTheHmiWritesIsACoil`
- `Millrace.Samples.Tests.FuxaProjectTests.EveryTagTheHmiReferencesIsADeviceTag`
- `Millrace.Samples.Tests.FuxaProjectTests.TheDeviceTagsAreExactlyTheModbusMapOfTheMinePlant`
- `Millrace.Samples.Tests.FuxaProjectTests.TheDeviceIsTheMillraceServerPolledEvery200Milliseconds`

Any other failure (in particular a `CorpusTests` or `SchemaAgreementTests`
case) means Step 5 is incomplete: stop and report it.

- [ ] **Step 8: Regenerate the FUXA project**

Run: `dotnet run --project src/Millrace.Cli -- modbus-map samples/mine-conveyors/plant.json --format fuxa --out .superpowers/sdd/9/fuxa-tags.json`
Run: `python3 -I hmi/fuxa/generate-project.py .superpowers/sdd/9/fuxa-tags.json hmi/fuxa/mine-conveyors.fuxap.json`
Expected: the second prints `views [45, 1, 2]`.

- [ ] **Step 9: Regenerate every golden**

Run: `MILLRACE_UPDATE_GOLDEN=1 dotnet test Millrace.sln --nologo`
Expected: 1718 passed (the update run writes the goldens at their source).
Run again without the variable: `dotnet test Millrace.sln --nologo`
Expected: 1718 passed, 0 failed (the rebuild copies the new goldens to the
test output).

Remove the `.actual` files the red run left:
`find docs tests samples hmi -name '*.actual' -print -delete`
Expected: it prints these five, then `find docs tests samples hmi -name '*.actual'` prints nothing:

```
docs/scenario-diagnostics.md.actual
docs/configuration-diagnostics.md.actual
tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json.actual
tests/Millrace.Configuration.Tests/Golden/plant.schema.json.actual
tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json.actual
```

(the order may differ).

- [ ] **Step 10: Prove every golden is the mapped old golden**

Run: `python3 -I .superpowers/sdd/9/rename.py goldens BASE` (BASE from Step 1)
Expected: 26 lines starting `OK`, exactly these six marked
`(changed by the mapping only)`, and the last line
`goldens: 26 checked, 0 differ from the mapped old golden`, exit 0:

```
OK    docs/configuration-diagnostics.md  (changed by the mapping only)
OK    docs/scenario-diagnostics.md  (changed by the mapping only)
OK    hmi/fuxa/mine-conveyors.fuxap.json  (changed by the mapping only)
OK    tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json  (changed by the mapping only)
OK    tests/Millrace.Configuration.Tests/Golden/plant.schema.json  (changed by the mapping only)
OK    tests/Millrace.Control.Catalogue.Tests/Golden/control-catalogue.json  (changed by the mapping only)
```

Then **read** the regenerated goldens' changes: run
`git diff -M BASE -- docs/configuration-diagnostics.md docs/scenario-diagnostics.md tests/Dse.Configuration.Tests/Golden/plant.schema.json tests/Millrace.Configuration.Tests/Golden/plant.schema.json`
(both schema paths, so git pairs the move and shows a 4-line change)
and read every hunk; read the first 40 lines of
`tests/Millrace.Components.Tests/Catalogue/Golden/components-catalogue.json`
with the Read tool (the `modules` list and the first `"module"` read
`Millrace.Components`); and run
`grep -n -E '"(name|id|address|title)": "(Millrace|millrace)' hmi/fuxa/mine-conveyors.fuxap.json`,
which prints exactly these five lines (measured):

```
3:  "name": "Millrace mine conveyors",
14:      "id": "millrace",
15:      "name": "Millrace",
20:        "address": "millrace:5020",
2723:        "title": "Millrace mine conveyors",
```

`goldens` already proves no register address moved (the mapping touches no
digit run that is not after `DSE`/`Dse`). Quote what you checked.

- [ ] **Step 11: Counts and residue**

Run: `python3 -I .superpowers/sdd/9/rename.py counts`
Expected: the nine false-positive lines exactly as in Task 1, then
`Dse              6113`, `DSE              1160`, `word dse         454`,
`paths to rename  0`.

Run: `python3 -I .superpowers/sdd/9/rename.py residue . ':!docs/superpowers'`
Expected: `residue: 0 allowed lines, 0 lines not allowed`, exit 0.

- [ ] **Step 12: The command line works under its new name**

Run: `src/Millrace.Cli/bin/Release/net10.0/millrace --help`
Expected first line: `millrace — deterministic industrial process simulation engine`.

Run: `src/Millrace.Cli/bin/Release/net10.0/millrace validate samples/mine-conveyors/plant.json`
Expected: `OK  samples/mine-conveyors/plant.json`, then the summary (`tags          124 (0 explicit)`, `controllers   12`), exit 0.

Run: `timeout -s INT 6 src/Millrace.Cli/bin/Release/net10.0/millrace serve samples/mine-conveyors/plant.json --port 5520`
Expected: `Serving samples/mine-conveyors/plant.json: 124 tags as 36 coils, 59 discrete inputs, 56 input registers and 2 holding registers.`,
`Listening on 127.0.0.1:5520 (Modbus TCP, any unit id) at 1x real time. Press Ctrl+C to stop.`,
then `Stopped at … after … ticks.` (exit 124 is `timeout`'s own code).

- [ ] **Step 13: Stage and check git's view**

Run: `git add -u`
Run: `git ls-files --others --exclude-standard` — expect no output.
Run: `git diff --stat` — expect no output (nothing unstaged).
Run: `python3 -I .superpowers/sdd/9/rename.py renames`
Expected, exactly (Spec deviation 6; report any difference):

```
renames: A 5, D 5, M 17, R 555
  not paired by the mapping: D  src/Dse.Cli/Dse.Cli.csproj
  not paired by the mapping: D  src/Dse.Cli/Program.cs
  not paired by the mapping: D  src/Dse.Configuration/Dse.Configuration.csproj
  not paired by the mapping: D  src/Dse.Scenarios/Dse.Scenarios.csproj
  not paired by the mapping: A  src/Millrace.Cli/Millrace.Cli.csproj
  not paired by the mapping: A  src/Millrace.Cli/Program.cs
  not paired by the mapping: A  src/Millrace.Configuration/Millrace.Configuration.csproj
  not paired by the mapping: R054  src/Dse.Modbus/Dse.Modbus.csproj  src/Millrace.Control.Catalogue/Millrace.Control.Catalogue.csproj
  not paired by the mapping: A  src/Millrace.Modbus/Millrace.Modbus.csproj
  not paired by the mapping: R053  src/Dse.Control.Catalogue/Dse.Control.Catalogue.csproj  src/Millrace.Scenarios/Millrace.Scenarios.csproj
  not paired by the mapping: D  tests/Dse.Core.Tests/ScaffoldingTests.cs
  not paired by the mapping: R080  tests/Dse.Core.Tests/Fakes/TestContexts.cs  tests/Millrace.Components.Tests/Fakes/TestContexts.cs
  not paired by the mapping: R081  tests/Dse.Components.Tests/Fakes/TestContexts.cs  tests/Millrace.Core.Tests/Fakes/TestContexts.cs
  not paired by the mapping: A  tests/Millrace.Core.Tests/ScaffoldingTests.cs
```

Run: `git diff --cached --shortstat`
Expected: `582 files changed, 2454 insertions(+), 2454 deletions(-)`.

- [ ] **Step 14: Commit**

Write `.superpowers/sdd/9/msg-task2.txt` with the Write tool:

```
refactor: rename Dse.* projects and namespaces to Millrace.*

Moves Dse.sln, every Dse.* folder and project, and the code-named
fixtures DSEnnn-*.json to their Millrace names with git mv, then applies
the plan 9 mapping to every tracked file outside docs/superpowers:
namespaces and assemblies Millrace.*, the millrace command, diagnostic
codes MRnnn, MILLRACE_UPDATE_GOLDEN, the Docker names and the FUXA device.
Three tests that took a code from a fixture name by its old width read
five characters instead of six. Every golden is regenerated and equals
the mapped old golden byte for byte.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Run: `git commit -F .superpowers/sdd/9/msg-task2.txt`
Run: `git log -1 --format='%h%n%s%n--%n%b'` and check it.
Run: `python3 -I .superpowers/sdd/9/rename.py renames HEAD` — expect the
same output as Step 13. Report BASE and the commit hash.

---

### Task 3: The 1.1.0 release

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify: `tests/Millrace.Samples.Tests/ReleaseTests.cs` (5 edits)
- Modify: `Directory.Build.props` (one line changed, one added)
- Modify: `CHANGELOG.md` (the `## Unreleased` heading and its first paragraph)
- Modify: `README.md` (the Status section)

**Interfaces:**
- Consumes: Task 2's tree (`Millrace.Samples.Tests`, `millrace`).
- Produces: version 1.1.0, `<Product>Millrace</Product>`, the changelog's
  1.1.0 entry (the only changelog lines allowed to name DSE) and the README
  line `Millrace was called DSE up to 1.0.0.`

- [ ] **Step 1: Make the release test ask for 1.1.0**

With the Edit tool, in `tests/Millrace.Samples.Tests/ReleaseTests.cs`:

Replace
```csharp
/// The v1.0.0 release (plan 7): the version the build stamps, the changelog's
/// one entry and the root README's "Getting started" agree, every link the
/// changelog makes resolves, and every command the README tells a newcomer to
/// run does what it says.
```
with
```csharp
/// The 1.1.0 release (plans 7 and 9): the version and product name the build
/// stamps, the changelog's two entries and the root README's "Getting started"
/// agree, every link the changelog makes resolves, and every command the README
/// tells a newcomer to run does what it says.
```

Replace
```csharp
    [GeneratedRegex(@"^## 1\.0\.0 — \d{4}-\d{2}-\d{2}$", RegexOptions.Multiline)]
```
with
```csharp
    [GeneratedRegex(@"^## (\d+\.\d+\.\d+) — \d{4}-\d{2}-\d{2}$")]
```

Replace
```csharp
        Assert.Contains("    <Version>1.0.0</Version>\n", props, StringComparison.Ordinal);
```
with
```csharp
        Assert.Contains("    <Version>1.1.0</Version>\n", props, StringComparison.Ordinal);
        Assert.Contains("    <Product>Millrace</Product>\n", props, StringComparison.Ordinal);
```

Replace
```csharp
        Assert.Single(ReleaseHeading().Matches(changelog));
        string[] headings = changelog.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        string[] expected = headings.Contains("## Unreleased") ? ["## Unreleased", headings[^1]] : [headings[^1]];
        Assert.Equal(expected, headings);
        Assert.Matches(ReleaseHeading(), headings[^1]);
```
with
```csharp
        string[] headings = changelog.Split('\n').Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToArray();
        Assert.All(headings, heading => Assert.Matches(ReleaseHeading(), heading));
        Assert.Equal(["1.1.0", "1.0.0"], headings.Select(heading => ReleaseHeading().Match(heading).Groups[1].Value));
        Assert.Equal("## 1.0.0 — 2026-10-07", headings[^1]);
```

Replace
```csharp
        Assert.Contains("**Version 1.0.0.**", readme, StringComparison.Ordinal);
```
with
```csharp
        Assert.Contains("**Version 1.1.0.**", readme, StringComparison.Ordinal);
```

(The test does not assert the README's "called DSE" sentence: a test line
naming DSE would itself be a line the spec does not allow. `residue`
checks that sentence instead, Step 7.)

- [ ] **Step 2: Run it to see it fail**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~ReleaseTests"`
Expected: `Failed: 1, Passed: 1, Total: 2` —
`TheVersionTheChangelogAndTheReadmeAgree` with
`Assert.Contains() Failure: Sub-string not found` (the `<Version>1.1.0</Version>` line).

- [ ] **Step 3: Set the version and the product name**

In `Directory.Build.props` replace
```xml
    <Version>1.0.0</Version>
```
with
```xml
    <Version>1.1.0</Version>
    <Product>Millrace</Product>
```

- [ ] **Step 4: Write the changelog's 1.1.0 entry**

In `CHANGELOG.md` replace (this is the text Task 2's mapping left)
```markdown
## Unreleased

Millrace's first external consumer: a SCADA watching and operating a simulated
plant over Modbus TCP
```
with
```markdown
## 1.1.0 — 2026-10-08

The first public release, under a new name: DSE is now **Millrace**, the
channel that drives a mill wheel. "DSE" is a known brand in the same
industrial space
([design](docs/superpowers/specs/2026-10-08-millrace-rename-design.md),
[plan 9](docs/superpowers/plans/2026-10-08-millrace-rename.md)). The rename
changes names only: no tick, event, value or register address changes, and a
golden line changes only where it spells a name.

### Rename

- The solution is `Millrace.sln` (was `Dse.sln`); every project, folder,
  assembly and namespace is `Millrace.*` (was `Dse.*`). A catalogue module
  built against `Dse.Core` 1.0.0 must be rebuilt against `Millrace.Core`.
- The command line is `millrace` (was `dse`).
- Diagnostic codes are `MR` and the same three digits, with the same meanings
  and order: `MR001`–`MR016`, `MR100`–`MR115` and `MR200`–`MR206` (were
  `DSE001`–`DSE016`, `DSE100`–`DSE115` and `DSE200`–`DSE206`).
- Goldens regenerate with `MILLRACE_UPDATE_GOLDEN=1` (was
  `DSE_UPDATE_GOLDEN=1`).
- The FUXA stack's compose project is `millrace-hmi`, its server service and
  FUXA device `millrace`, its image `millrace-hmi/millrace:local` (were
  `dse-hmi`, `dse` and `dse-hmi/dse:local`). The new project starts with
  fresh volumes; `docker compose -p dse-hmi down -v` removes the old ones.
- The assemblies carry the product name `Millrace` and version 1.1.0.

Also in 1.1.0, Millrace's first external consumer: a SCADA watching and
operating a simulated plant over Modbus TCP
```

The date is the release day; if the merge lands on a later day the
controller amends this heading then (the test checks only its shape).

- [ ] **Step 5: Rewrite the README's status**

In `README.md` replace
```markdown
**Version 1.0.0.** Everything in the v1 scope of the
```
with
```markdown
**Version 1.1.0.** Everything in the v1 scope of the
```

Replace
```markdown
`millrace` command line, and two reference samples. The [changelog](CHANGELOG.md)
lists what each project contains and links every spec and plan.
```
with
```markdown
`millrace` command line, and two reference samples. Since 1.0.0,
`millrace serve` runs a plant in real time and serves its tags over Modbus
TCP, and `hmi/fuxa/` puts the mine-conveyor sample in the FUXA web SCADA with
one `docker compose up` — see [A SCADA on the sample](#a-scada-on-the-sample).
The [changelog](CHANGELOG.md) lists what each release contains and links
every spec and plan.

Millrace was called DSE up to 1.0.0.
```

Delete this paragraph and the blank line before it (the old "Since 1.0.0"
note; its content moved into the paragraph above, and its
`CHANGELOG.md#unreleased` link no longer resolves):
```markdown

**Since 1.0.0 (unreleased):** Millrace's first external consumer. `millrace serve` runs a
plant in real time and serves its tags over Modbus TCP, and `hmi/fuxa/` puts
the mine-conveyor sample in the FUXA web SCADA with one `docker compose up` —
see [A SCADA on the sample](#a-scada-on-the-sample) and the
[changelog](CHANGELOG.md#unreleased).
```

- [ ] **Step 6: Run the release tests, then everything**

Run: `dotnet test tests/Millrace.Samples.Tests --nologo --filter "FullyQualifiedName~ReleaseTests"`
Expected: `Passed: 2, Total: 2`.
Run: `dotnet build Millrace.sln -c Release --nologo` — `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — 1718 passed.

- [ ] **Step 7: Residue and counts**

Run: `python3 -I .superpowers/sdd/9/rename.py residue . ':!docs/superpowers'`
Expected: exit 0, `residue: 11 allowed lines, 0 lines not allowed`; the
allowed lines are `CHANGELOG.md` lines 8, 9, 18, 19, 20, 21, 24, 26, 29, 30
and `README.md:26: Millrace was called DSE up to 1.0.0.`

Run: `python3 -I .superpowers/sdd/9/rename.py counts`
Expected: the nine false-positive lines as in Task 1, then
`Dse              6116`, `DSE              1170`, `word dse         460`,
`paths to rename  0`.

- [ ] **Step 8: Commit**

Run: `git status --short` — expect exactly
` M CHANGELOG.md`, ` M Directory.Build.props`, ` M README.md`,
` M tests/Millrace.Samples.Tests/ReleaseTests.cs`.
Run: `git add -u`

Write `.superpowers/sdd/9/msg-task3.txt`:

```
chore(release): version 1.1.0, the first release as Millrace

Sets Version 1.1.0 and Product Millrace in Directory.Build.props, turns
the changelog's Unreleased section into the 1.1.0 entry, which states the
rename and keeps the Modbus and FUXA notes, and folds the README's
"Since 1.0.0" note into its status with the one sentence that names the
old product. ReleaseTests now asks for 1.1.0, the product name and the
two release headings.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Run: `git commit -F .superpowers/sdd/9/msg-task3.txt`
Run: `git log -1 --format='%h%n%s%n--%n%b'` and check it.

---

### Task 4: The history documents

**Model:** implementer sonnet; reviewer sonnet.

**Files:**
- Modify by the mapping: the 29 files under `docs/superpowers/specs/` and
  `docs/superpowers/plans/` other than
  `docs/superpowers/specs/2026-10-08-millrace-rename-design.md` and
  `docs/superpowers/plans/2026-10-08-millrace-rename.md` (the script's `SKIP`).

**Interfaces:**
- Consumes: `rename.py`; the tree after Task 3.
- Produces: a tree whose only old-name lines are the allowed ones.

- [ ] **Step 1: Apply the mapping to the history documents**

Run: `python3 -I .superpowers/sdd/9/rename.py apply docs/superpowers`
Expected: `apply: 29 files rewritten; skipped 2 (rename spec/plan and goldens)`.
Run: `git diff --shortstat`
Expected: `29 files changed, 6260 insertions(+), 6260 deletions(-)`.

- [ ] **Step 2: Check the absolute paths**

Run: `git diff -U0 -- docs/superpowers/plans/2026-09-02-simulation-core.md docs/superpowers/plans/2026-09-12-component-library.md docs/superpowers/plans/2026-10-07-modbus-fuxa-hmi.md`
and find, among the hunks, the three path lines: two `+# from the repository root`
(each replacing a `cd` to the owner's checkout) and
``+(`mv .playwright-mcp .superpowers/sdd/8/screenshots`)``. Quote them.

Run: `python3 -I .superpowers/sdd/9/rename.py apply docs/superpowers`
again. Expected: `apply: 0 files rewritten; …` (the mapping is idempotent).

- [ ] **Step 3: Residue over the whole tree, and counts**

Run: `python3 -I .superpowers/sdd/9/rename.py residue`
Expected: exit 0, `residue: 11 allowed lines, 0 lines not allowed` — the
same 11 lines as Task 3 Step 7, nothing from `docs/superpowers/`.

Run: `python3 -I .superpowers/sdd/9/rename.py counts`
Expected: the nine false-positive lines as in Task 1, then
`Dse              3`, `DSE              10`, `word dse         6`,
`paths to rename  0` (every one of them on an allowed line).

- [ ] **Step 4: Tests still pass**

`WheelLineReadmeTests` reads the main spec. Run: `dotnet test Millrace.sln --nologo`
Expected: 1718 passed.

- [ ] **Step 5: Commit**

Run: `git add -u`
Run: `git diff --cached --shortstat` — expect
`29 files changed, 6260 insertions(+), 6260 deletions(-)`.

Write `.superpowers/sdd/9/msg-task4.txt`:

```
docs: rewrite the specs and plans with the Millrace names

Applies the plan 9 mapping to every spec and plan under docs/superpowers
except the rename's own spec and plan, which describe the rename. The
three absolute paths to the owner's checkout become "from the repository
root" and a relative path.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
```

Run: `git commit -F .superpowers/sdd/9/msg-task4.txt`
Run: `git log -1 --format='%h%n%s%n--%n%b'` and check it.

---

### Task 5: Acceptance

**Model:** implementer sonnet; reviewer sonnet.

**Files:** none change. Scratch only under `.superpowers/sdd/9/`.

**Interfaces:**
- Consumes: BASE and the Task 2 commit hash (both in Task 2's report).
- Produces: the acceptance report the controller gives the owner.

- [ ] **Step 1: Clean build and the full suite**

Run: `find src tests -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +`
Run: `dotnet build Millrace.sln -c Release --nologo` — `0 Warning(s)`, `0 Error(s)`.
Run: `dotnet test Millrace.sln --nologo` — 1718 passed, 0 failed (37 / 498 /
189 / 57 / 239 / 170 / 109 / 153 / 27 / 95 / 144 per project, as before the
rename).

- [ ] **Step 2: The spec's greps**

Run: `git grep -c -e Dse -e DSE`
Expected: exactly four files — `CHANGELOG.md:7`, `README.md:1`, the rename
plan and the rename spec (their own counts are whatever they are).
Run: `git grep -c -w dse`
Expected: exactly three files — `CHANGELOG.md:3`, the rename plan and the
rename spec.
Run: `python3 -I .superpowers/sdd/9/rename.py residue` — exit 0,
`11 allowed lines, 0 lines not allowed`.

- [ ] **Step 3: False positives**

Run: `python3 -I .superpowers/sdd/9/rename.py counts`
Expected: SpeedSensor 41, ItemIdSequence 46, elapsedSeconds 17,
ElapsedSeconds 13, holdSeconds 12, AddSeconds 10, TimedSeconds 8, SortedSet
8, mixed-case dse 205 — identical to Task 1.

- [ ] **Step 4: Goldens, once more**

Run: `python3 -I .superpowers/sdd/9/rename.py goldens BASE`
Expected: `goldens: 26 checked, 0 differ from the mapped old golden`.

- [ ] **Step 5: History tracing**

Run: `python3 -I .superpowers/sdd/9/rename.py renames <Task 2 commit hash>`
Expected: the 15 lines of Task 2 Step 13.
Run: `git log --follow --oneline -- src/Millrace.Core/SimulationBuilder.cs`
Expected: 13 commits, the oldest
`7658ce4 feat(core): add simulation lifecycle, tick phases and builder validation`. Run
`git log --oneline -- src/Dse.Core/SimulationBuilder.cs` and compare: the
same 13 hashes.

- [ ] **Step 6: The README's commands, as written**

Each from the repository root (`dotnet run` builds Debug):

- `dotnet run --project src/Millrace.Cli -- run samples/mine-conveyors/scenarios/pull-key.json --expect samples/mine-conveyors/expected/pull-key.log`
  → `Matched samples/mine-conveyors/expected/pull-key.log (99 events).`, exit 0.
- `dotnet run --project src/Millrace.Cli -- run samples/wheel-line/scenarios/slow-press.json --expect samples/wheel-line/expected/slow-press.log`
  → `Matched samples/wheel-line/expected/slow-press.log (267 events).`, exit 0.
- `src/Millrace.Cli/bin/Debug/net10.0/millrace --help` → first line
  `millrace — deterministic industrial process simulation engine`.
- `src/Millrace.Cli/bin/Debug/net10.0/millrace catalog export --out .superpowers/sdd/9/catalog.json` → exit 0.
- `src/Millrace.Cli/bin/Debug/net10.0/millrace schema export --out .superpowers/sdd/9/millrace-plant.schema.json`
  → exit 0; its line 3 is `  "title": "Millrace plant",`. (The README
  writes `millrace-plant.schema.json` in the current folder; write it under
  `.superpowers/` so the tree stays clean.)
- `src/Millrace.Cli/bin/Debug/net10.0/millrace validate samples/mine-conveyors/plant.json` → `OK  samples/mine-conveyors/plant.json`, exit 0.
- `src/Millrace.Cli/bin/Debug/net10.0/millrace validate tests/Millrace.Configuration.Tests/Plants/invalid/MR013-scan-period-off-the-step.json`
  → first line `MR013 $.controllers[0].scanPeriodMs`, last line
  `1 error in MR013-scan-period-off-the-step.json`, exit 1.
- `src/Millrace.Cli/bin/Debug/net10.0/millrace tags samples/mine-conveyors/plant.json` → first line starts `ALM_CV001.Ack  Bool  ReadWrite`.
- `src/Millrace.Cli/bin/Debug/net10.0/millrace modbus-map samples/mine-conveyors/plant.json` → header line starts `area               address  offset  type`.
- `timeout -s INT 6 src/Millrace.Cli/bin/Debug/net10.0/millrace serve samples/mine-conveyors/plant.json --port 5520`
  → `Listening on 127.0.0.1:5520 (Modbus TCP, any unit id) at 1x real time. Press Ctrl+C to stop.` then `Stopped at …` (exit 124 from `timeout`).

- [ ] **Step 7: The tree is clean**

Run: `git status --short` — no output.

- [ ] **Step 8: A 1.0.0-built module is refused cleanly (Review Focus 4)**

Run: `mkdir -p .superpowers/sdd/9/old`
Run: `git archive -o .superpowers/sdd/9/old.tar BASE Directory.Build.props src tests/Dse.Cli.Tests.SampleModule`
Run: `tar -xf .superpowers/sdd/9/old.tar -C .superpowers/sdd/9/old`
Run: `dotnet build .superpowers/sdd/9/old/tests/Dse.Cli.Tests.SampleModule --nologo -v q`
Run: `src/Millrace.Cli/bin/Debug/net10.0/millrace catalog export --assembly .superpowers/sdd/9/old/tests/Dse.Cli.Tests.SampleModule/bin/Debug/net10.0/Dse.Cli.Tests.SampleModule.dll`
Expected (measured): exit 3 and, on stderr,
`Assembly '.superpowers/sdd/9/old/tests/Dse.Cli.Tests.SampleModule/bin/Debug/net10.0/Dse.Cli.Tests.SampleModule.dll' contains no catalogue module. A module is a public, non-abstract class that implements ICatalogueModule and has a public parameterless constructor.`
No stack trace. Then `rm -rf .superpowers/sdd/9/old .superpowers/sdd/9/old.tar`.

- [ ] **Step 9: The HMI, when Docker is available (Review Focus 5)**

Run: `docker info --format '{{.ServerVersion}}'`
If it fails, write "Docker not available; the HMI smoke check was not run"
in the report and skip the rest of this step. (On the planning machine it
printed a version: Docker 29.7.)

Run: `docker compose -p dse-hmi ps` — expect no running container (the old
stack would hold ports 5020 and 1881). If one runs, stop and report; do not
stop the owner's stack yourself.
Run: `docker compose -f hmi/fuxa/docker-compose.yml up -d --build`
Expected: containers `millrace-hmi-millrace-1`, `millrace-hmi-fuxa-1`,
`millrace-hmi-fuxa-init-1` started.
Run: `sh hmi/fuxa/smoke-check.sh`
Expected: `FUXA reads CV001.Speed = <a value near 0> m/s.` (the raw float,
e.g. `0.000113…`), `Pressed Start line.`,
`PASS: CV001.Speed = 1.8… m/s after the start sequence.`, exit 0.
Run: `docker compose -f hmi/fuxa/docker-compose.yml logs fuxa-init`
Expected: a line `Loaded the mine-conveyors project into FUXA.`
Run: `docker compose -f hmi/fuxa/docker-compose.yml logs millrace`
Expected: `Listening on 0.0.0.0:5020 (Modbus TCP, any unit id) at 1x real time. …`
Run: `docker compose -f hmi/fuxa/docker-compose.yml down -v`
(removes only the `millrace-hmi` containers, network and volumes this step
created). Never remove the `dse-hmi` images or volumes.

- [ ] **Step 10: Report**

Quote every expected line above as measured, BASE, the three commit hashes
and the Docker outcome. No commit.

---

## After merge

Steps for the controller and the owner, not tasks:

- Tell the owner about Spec deviation 6 (nine files git does not pair) and
  10 (1.1.0 versus 2.0.0) **before** Task 2 runs, so either can be changed.
- Offer the annotated tag `v1.1.0` on the merge commit; tag `v1.0.0` stays
  where it is.
- The owner confirms renaming the GitHub repository `DSE` → `Millrace`
  (GitHub keeps a redirect); then `git remote set-url origin <new URL>`.
- The `dse-hmi` Docker images and volumes on this machine are the owner's to
  remove (`docker compose -p dse-hmi down -v`, `docker image rm
  dse-hmi/dse:local dse-hmi/fuxa:1.3.4-modbus`).
- The local checkout folder `…/POCs/DSE` and the memory notes that name it
  are outside the tree; renaming them is the owner's call.

## Spec coverage

| Spec item | Where |
|---|---|
| Full rename: solution, projects, folders, namespaces, assemblies | Task 2 (`move`, `apply`) |
| Product name `Millrace` in prose | Task 2 (rule `\bDSE\b`, `Dse`), Task 4 |
| CLI `millrace`; `AssemblyName` of `Millrace.Cli` | Task 2 (rule `\bdse\b`; Step 6, 12) |
| `MR` + three digits; anchors | Task 2 (rule `DSE(?=…)`; findings 3, 4) |
| History documents rewritten | Task 4 |
| Version 1.1.0, `<Product>Millrace</Product>` | Task 3 |
| Git history and tags untouched | no task rewrites history; After merge |
| `Dse`+number → `Mr`+number; product word → `Millrace` in identifiers | Task 1 selftest, Task 2 |
| `DSE_UPDATE_GOLDEN` → `MILLRACE_UPDATE_GOLDEN` | Task 2 |
| `dse-plant.schema.json`, temp prefixes, `dse-dispatcher` | Task 1 selftest, Task 2 |
| Docker names; `/app/millrace`; `millrace:5020` | Task 2 (Step 10), Task 5 Step 9 |
| Absolute paths in plans | Task 4 Step 2 |
| What may keep the old name | `residue`, Tasks 3–5 |
| Goldens regenerated with `MILLRACE_UPDATE_GOLDEN=1`, mapped-old == new | Task 2 Steps 9–10, Task 5 Step 4 |
| FUXA project regenerated by `generate-project.py`; addresses unchanged | Task 2 Steps 8, 10 |
| Build clean, 1718 tests | every task; Task 5 Step 1 |
| `git grep` finds only allowed lines | Task 5 Step 2 |
| False positives unchanged | `counts`, Tasks 1–5 |
| `millrace --help`, `validate`, `serve`, README commands | Task 2 Step 12, Task 5 Step 6 |
| `smoke-check.sh` with Docker | Task 5 Step 9 |
| `git log --follow`; every commit builds | Task 2 Step 13, Task 5 Step 5; finding 6 |

## Self-review

- Spec coverage: every requirement maps to a step above; the two the source
  makes impossible to meet together are finding 6, raised before execution.
- Placeholders: `BASE` and the Task 2 commit hash are runtime values the
  plan tells the implementer how to obtain and where to record; every file
  edit shows its exact old and new text; the script is complete.
- Names: the script's commands (`selftest`, `counts`, `move`, `apply`,
  `goldens`, `renames`, `residue`) are the same in every task; the env var is
  `MILLRACE_UPDATE_GOLDEN` everywhere after Task 2.
- Review Focus: each of the five has a check in its owning task (Tasks 2–5).
