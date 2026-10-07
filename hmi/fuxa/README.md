# The mine conveyors in FUXA

The [mine-conveyor sample](../../samples/mine-conveyors/README.md) as an
operator sees it: DSE runs the plant in real time and serves its tags over
Modbus TCP, and [FUXA](https://github.com/frangoteam/FUXA), an open-source web
SCADA, polls them and shows an overview mimic, the alarms and the trends. You
start the line, stop it, pull a pull-key or press an e-stop from the browser,
and watch the plant's PLC logic answer.

```
dse  ──Modbus TCP :5020──▶  FUXA  ──HTTP :1881──▶  your browser
```

## Run it

You need [Docker](https://docs.docker.com/get-docker/) with Compose v2;
nothing else (the images build DSE from this repository). From this folder:

```bash
docker compose up --build
```

then open <http://localhost:1881>. The first start builds two images and takes
a few minutes; later starts take seconds. Three services start:

| service | what it does |
|---|---|
| `dse` | `dse serve samples/mine-conveyors/plant.json --port 5020`: the plant, in real time, on port 5020 (localhost only) |
| `fuxa` | FUXA 1.3.4 with its Modbus driver, on port 1881 (localhost only) |
| `fuxa-init` | waits for FUXA, loads `mine-conveyors.fuxap.json` into it on the first start, and exits |

Stop with Ctrl+C, or `docker compose down` from another terminal. The plant
starts cold every time `dse` starts: every belt stopped and every interlock
tripped, as at power-up.

To check the stack from a terminal instead of a browser, with it running:

```bash
./smoke-check.sh
```

It waits for FUXA to read CV001's speed, presses **Start line** through
FUXA's API, and prints `PASS: CV001.Speed = … m/s after the start sequence.`
once the belt is up to speed (exit 0), or what failed (exit 1).

## The screens

**Overview** is the line from the ore feed to the stockpile. Each belt is
green while its contactor is closed, grey while it is open, and red while its
interlock is tripped; under it are its speed (m/s), load (t/h) and motor
current (A), and lamps for its contactor, its safety relay, its zero-speed
switch and its interlock's `Ok`. At power-up all three belts are red: every
interlock trips on its permissive until the start sequence resets the safety
relays (see "Power-up" in the sample's README).

**Alarms** lists FUXA's alarms: each belt's motor current `Hi` and `HiHi` (the
sample's `ALM_CVn` blocks, with their deadband and 3 s on-delay; FUXA raises
on the blocks' `Active` tags) and each interlock's trip. The bell in the
header counts them; acknowledge a row with its tick.

**Trends** draws the three belts' speeds and motor currents, live since the
view opened, ten minutes at most. The charts are drawn in the browser from the
values FUXA reads, with no stored history: they start empty each time you open
the view, and become a rolling ten-minute window once it has been open that
long. Until you hover a chart, its legend shows `--` for each line and a time
of 1970 (FUXA's legend shows the values under the pointer).

## The buttons

| button | writes | what it mirrors |
|---|---|---|
| Start line | `SEQ_START.Reset`, then `SEQ_START.Start` | every scenario's `SEQ_START.Start` at 1 s; [normal start and stop](../../samples/mine-conveyors/README.md#1-normal-start-and-stop) |
| Stop line | `SEQ_STOP.Reset`, then `SEQ_STOP.Start` | the normal stop at 90 s |
| Reset safety relays | `CV001.SafetyReset`, `CV002.SafetyReset`, `CV003.SafetyReset`, in turn | the sequence's first step, by hand |
| Reset interlocks | `INT_CV003.Reset`, `INT_CV002.Reset`, `INT_CV001.Reset`, `INT_FEED.Reset`, in turn | the manual recovery of [a start while the line is tripped](../../samples/mine-conveyors/README.md#9-a-start-while-the-line-is-tripped) |
| CVn pull-key | `CVn.PullKey1`, toggled | [pull-key](../../samples/mine-conveyors/README.md#2-pull-key) |
| CVn e-stop | `CVn.EStop`, toggled | [emergency stop](../../samples/mine-conveyors/README.md#3-emergency-stop) |

The first four are push-buttons. Every command they write acts on a rising
edge and the blocks scan every 100 or 200 ms, so a button does not write the
tag itself: it runs a FUXA server script, `pulse`, that writes each tag true,
holds it for 0.5 s, and writes it false — long enough for any scan to see the
edge, however briefly the button was pressed. **Start line** resets the start
sequence first, because a sequence that has completed (or faulted) accepts a
new start only after a reset.

The pull-keys and e-stops latch, as the real devices do: a red button is
actuated, and pressing it again restores it. Restoring a pull-key does not
restart anything: reset the safety relays and the interlocks, then start the
line — or just press **Start line**, which resets both on its way.

Faults — an overload, a blocked chute, a welded contactor — are not tags, so
the HMI cannot inject them. To watch one, replay a scenario's timeline: in
`docker-compose.yml`, add `"--scenario",
"samples/mine-conveyors/scenarios/chute-blockage.json"` to the `dse` service's
`command`, then `docker compose up`. The scenario starts the line at 1 s and
blocks CH1 at 80 s; CV001's `Hi` alarm raises at about 1 min 53 s, `HiHi`
about 16 s later, and the overload trips at about 3 min 10 s.

## Register map

`dse serve` maps the plant's tags in their directory order: a read-write Bool
is a coil, a read-only one a discrete input; a Double is a big-endian Float32
in two registers and an Int64 a big-endian Int32 in two registers (clamped to
the Int32 range), holding when read-write, input when read-only. A tag a
block claims, such as `CV001.Permit`, is read-only. Any unit id is answered.
`address` is 1-based, as FUXA and most SCADAs show it; `offset` is what goes
on the wire. This is the output of
`dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json`
(`--format csv` for a spreadsheet, `--format fuxa` for the FUXA device's
tags):

```text
area               address  offset  type     tag                    access     unit      description
coils                    1       0  Bool     ALM_CV001.Ack          ReadWrite            Acknowledges every outstanding limit on a rising edge
coils                    2       1  Bool     ALM_CV002.Ack          ReadWrite            Acknowledges every outstanding limit on a rising edge
coils                    3       2  Bool     ALM_CV003.Ack          ReadWrite            Acknowledges every outstanding limit on a rising edge
coils                    4       3  Bool     CV001.EStop            ReadWrite            Actuated by the operator
coils                    5       4  Bool     CV001.PullKey1         ReadWrite            Actuated by the operator
coils                    6       5  Bool     CV001.PullKey2         ReadWrite            Actuated by the operator
coils                    7       6  Bool     CV001.Reset            ReadWrite            Overload reset, rising edge
coils                    8       7  Bool     CV001.SafetyReset      ReadWrite            Safety reset, rising edge
coils                    9       8  Bool     CV001.Start            ReadWrite            Run command
coils                   10       9  Bool     CV002.EStop            ReadWrite            Actuated by the operator
coils                   11      10  Bool     CV002.PullKey1         ReadWrite            Actuated by the operator
coils                   12      11  Bool     CV002.PullKey2         ReadWrite            Actuated by the operator
coils                   13      12  Bool     CV002.Reset            ReadWrite            Overload reset, rising edge
coils                   14      13  Bool     CV002.SafetyReset      ReadWrite            Safety reset, rising edge
coils                   15      14  Bool     CV002.Start            ReadWrite            Run command
coils                   16      15  Bool     CV003.EStop            ReadWrite            Actuated by the operator
coils                   17      16  Bool     CV003.PullKey1         ReadWrite            Actuated by the operator
coils                   18      17  Bool     CV003.PullKey2         ReadWrite            Actuated by the operator
coils                   19      18  Bool     CV003.Reset            ReadWrite            Overload reset, rising edge
coils                   20      19  Bool     CV003.SafetyReset      ReadWrite            Safety reset, rising edge
coils                   21      20  Bool     CV003.Start            ReadWrite            Run command
coils                   22      21  Bool     Feed.Enabled           ReadWrite            Feeder enabled
coils                   23      22  Bool     INT_CV001.Reset        ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   24      23  Bool     INT_CV002.Reset        ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   25      24  Bool     INT_CV003.Reset        ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   26      25  Bool     INT_FEED.Reset         ReadWrite            Clears the latch on a rising edge when every condition is normal
coils                   27      26  Bool     SEQ_START.Abort        ReadWrite            Returns to idle on a rising edge
coils                   28      27  Bool     SEQ_START.Hold         ReadWrite            Freezes the step clock on a rising edge
coils                   29      28  Bool     SEQ_START.Reset        ReadWrite            Returns to idle from faulted or complete on a rising edge
coils                   30      29  Bool     SEQ_START.Resume       ReadWrite            Continues a held step on a rising edge
coils                   31      30  Bool     SEQ_START.Start        ReadWrite            Enters step 1 from idle on a rising edge
coils                   32      31  Bool     SEQ_STOP.Abort         ReadWrite            Returns to idle on a rising edge
coils                   33      32  Bool     SEQ_STOP.Hold          ReadWrite            Freezes the step clock on a rising edge
coils                   34      33  Bool     SEQ_STOP.Reset         ReadWrite            Returns to idle from faulted or complete on a rising edge
coils                   35      34  Bool     SEQ_STOP.Resume        ReadWrite            Continues a held step on a rising edge
coils                   36      35  Bool     SEQ_STOP.Start         ReadWrite            Enters step 1 from idle on a rising edge
discrete inputs          1       0  Bool     ALM_CV001.Hi.Acked     ReadOnly             Nothing is outstanding on the Hi limit
discrete inputs          2       1  Bool     ALM_CV001.Hi.Active    ReadOnly             The Hi limit is in alarm
discrete inputs          3       2  Bool     ALM_CV001.HiHi.Acked   ReadOnly             Nothing is outstanding on the HiHi limit
discrete inputs          4       3  Bool     ALM_CV001.HiHi.Active  ReadOnly             The HiHi limit is in alarm
discrete inputs          5       4  Bool     ALM_CV002.Hi.Acked     ReadOnly             Nothing is outstanding on the Hi limit
discrete inputs          6       5  Bool     ALM_CV002.Hi.Active    ReadOnly             The Hi limit is in alarm
discrete inputs          7       6  Bool     ALM_CV002.HiHi.Acked   ReadOnly             Nothing is outstanding on the HiHi limit
discrete inputs          8       7  Bool     ALM_CV002.HiHi.Active  ReadOnly             The HiHi limit is in alarm
discrete inputs          9       8  Bool     ALM_CV003.Hi.Acked     ReadOnly             Nothing is outstanding on the Hi limit
discrete inputs         10       9  Bool     ALM_CV003.Hi.Active    ReadOnly             The Hi limit is in alarm
discrete inputs         11      10  Bool     ALM_CV003.HiHi.Acked   ReadOnly             Nothing is outstanding on the HiHi limit
discrete inputs         12      11  Bool     ALM_CV003.HiHi.Active  ReadOnly             The HiHi limit is in alarm
discrete inputs         13      12  Bool     CH1.Full               ReadOnly             At capacity
discrete inputs         14      13  Bool     CH2.Full               ReadOnly             At capacity
discrete inputs         15      14  Bool     CV001.Contactor        ReadOnly             Contactor closed
discrete inputs         16      15  Bool     CV001.EStop.Ok         ReadOnly             Safety loop healthy
discrete inputs         17      16  Bool     CV001.Permit           ReadOnly             Run permit; false holds the contactor open (claimed by INT_CV001)
discrete inputs         18      17  Bool     CV001.PullKey1.Ok      ReadOnly             Safety loop healthy
discrete inputs         19      18  Bool     CV001.PullKey2.Ok      ReadOnly             Safety loop healthy
discrete inputs         20      19  Bool     CV001.SafetyOk         ReadOnly             Relay energised
discrete inputs         21      20  Bool     CV001.Stopped          ReadOnly             Below the threshold for the delay
discrete inputs         22      21  Bool     CV001.Tripped          ReadOnly             Overload relay tripped
discrete inputs         23      22  Bool     CV002.Contactor        ReadOnly             Contactor closed
discrete inputs         24      23  Bool     CV002.EStop.Ok         ReadOnly             Safety loop healthy
discrete inputs         25      24  Bool     CV002.Permit           ReadOnly             Run permit; false holds the contactor open (claimed by INT_CV002)
discrete inputs         26      25  Bool     CV002.PullKey1.Ok      ReadOnly             Safety loop healthy
discrete inputs         27      26  Bool     CV002.PullKey2.Ok      ReadOnly             Safety loop healthy
discrete inputs         28      27  Bool     CV002.SafetyOk         ReadOnly             Relay energised
discrete inputs         29      28  Bool     CV002.Stopped          ReadOnly             Below the threshold for the delay
discrete inputs         30      29  Bool     CV002.Tripped          ReadOnly             Overload relay tripped
discrete inputs         31      30  Bool     CV003.Contactor        ReadOnly             Contactor closed
discrete inputs         32      31  Bool     CV003.EStop.Ok         ReadOnly             Safety loop healthy
discrete inputs         33      32  Bool     CV003.Permit           ReadOnly             Run permit; false holds the contactor open (claimed by INT_CV003)
discrete inputs         34      33  Bool     CV003.PullKey1.Ok      ReadOnly             Safety loop healthy
discrete inputs         35      34  Bool     CV003.PullKey2.Ok      ReadOnly             Safety loop healthy
discrete inputs         36      35  Bool     CV003.SafetyOk         ReadOnly             Relay energised
discrete inputs         37      36  Bool     CV003.Stopped          ReadOnly             Below the threshold for the delay
discrete inputs         38      37  Bool     CV003.Tripped          ReadOnly             Overload relay tripped
discrete inputs         39      38  Bool     Feed.Permit            ReadOnly             Run permit; false stops the feeder (claimed by INT_FEED)
discrete inputs         40      39  Bool     INT_CV001.Ok           ReadOnly             Not tripped
discrete inputs         41      40  Bool     INT_CV001.Tripped      ReadOnly             Latched by an abnormal condition
discrete inputs         42      41  Bool     INT_CV002.Ok           ReadOnly             Not tripped
discrete inputs         43      42  Bool     INT_CV002.Tripped      ReadOnly             Latched by an abnormal condition
discrete inputs         44      43  Bool     INT_CV003.Ok           ReadOnly             Not tripped
discrete inputs         45      44  Bool     INT_CV003.Tripped      ReadOnly             Latched by an abnormal condition
discrete inputs         46      45  Bool     INT_FEED.Ok            ReadOnly             Not tripped
discrete inputs         47      46  Bool     INT_FEED.Tripped       ReadOnly             Latched by an abnormal condition
discrete inputs         48      47  Bool     PERM_CV001.Ok          ReadOnly             Every condition is normal
discrete inputs         49      48  Bool     PERM_CV002.Ok          ReadOnly             Every condition is normal
discrete inputs         50      49  Bool     PERM_CV003.Ok          ReadOnly             Every condition is normal
discrete inputs         51      50  Bool     SEQ_START.Complete     ReadOnly             The last step finished
discrete inputs         52      51  Bool     SEQ_START.Faulted      ReadOnly             A step timed out
discrete inputs         53      52  Bool     SEQ_START.Held         ReadOnly             The step clock is frozen
discrete inputs         54      53  Bool     SEQ_START.Running      ReadOnly             A step is running
discrete inputs         55      54  Bool     SEQ_STOP.Complete      ReadOnly             The last step finished
discrete inputs         56      55  Bool     SEQ_STOP.Faulted       ReadOnly             A step timed out
discrete inputs         57      56  Bool     SEQ_STOP.Held          ReadOnly             The step clock is frozen
discrete inputs         58      57  Bool     SEQ_STOP.Running       ReadOnly             A step is running
discrete inputs         59      58  Bool     Stockpile.Full         ReadOnly             At capacity
input registers          1       0  Float32  CH1.Level              ReadOnly   fraction  Held mass over capacity
input registers          3       2  Float32  CH2.Level              ReadOnly   fraction  Held mass over capacity
input registers          5       4  Float32  CV001.Current          ReadOnly   A         Measured value
input registers          7       6  Float32  CV001.Speed            ReadOnly   m/s       Measured value
input registers          9       8  Float32  CV001.TonnesPerHour    ReadOnly   t/h       Measured value
input registers         11      10  Float32  CV001.ZeroSpeed.Value  ReadOnly   m/s       Monitored speed
input registers         13      12  Float32  CV002.Current          ReadOnly   A         Measured value
input registers         15      14  Float32  CV002.Speed            ReadOnly   m/s       Measured value
input registers         17      16  Float32  CV002.TonnesPerHour    ReadOnly   t/h       Measured value
input registers         19      18  Float32  CV002.ZeroSpeed.Value  ReadOnly   m/s       Monitored speed
input registers         21      20  Float32  CV003.Current          ReadOnly   A         Measured value
input registers         23      22  Float32  CV003.Speed            ReadOnly   m/s       Measured value
input registers         25      24  Float32  CV003.TonnesPerHour    ReadOnly   t/h       Measured value
input registers         27      26  Float32  CV003.ZeroSpeed.Value  ReadOnly   m/s       Monitored speed
input registers         29      28  Float32  Feed.HopperMass        ReadOnly   kg        Mass in the hopper
input registers         31      30  Int32    INT_CV001.FirstOut     ReadOnly   count     Index of the condition that tripped, or -1
input registers         33      32  Int32    INT_CV002.FirstOut     ReadOnly   count     Index of the condition that tripped, or -1
input registers         35      34  Int32    INT_CV003.FirstOut     ReadOnly   count     Index of the condition that tripped, or -1
input registers         37      36  Int32    INT_FEED.FirstOut      ReadOnly   count     Index of the condition that tripped, or -1
input registers         39      38  Int32    PERM_CV001.FirstOut    ReadOnly   count     Index of the first condition to leave normal, or -1
input registers         41      40  Int32    PERM_CV002.FirstOut    ReadOnly   count     Index of the first condition to leave normal, or -1
input registers         43      42  Int32    PERM_CV003.FirstOut    ReadOnly   count     Index of the first condition to leave normal, or -1
input registers         45      44  Int32    SEQ_START.Step         ReadOnly   count     The step running, or 0 when idle
input registers         47      46  Float32  SEQ_START.StepTime     ReadOnly   s         Time in the current step
input registers         49      48  Int32    SEQ_STOP.Step          ReadOnly   count     The step running, or 0 when idle
input registers         51      50  Float32  SEQ_STOP.StepTime      ReadOnly   s         Time in the current step
input registers         53      52  Float32  Stockpile.Rate         ReadOnly   kg/s      Receiving rate
input registers         55      54  Float32  Stockpile.Received     ReadOnly   kg        Cumulative mass received
holding registers        1       0  Float32  Feed.Rate              ReadWrite  kg/s      Feed rate
```

A write to an address no read-write tag occupies, or to one register of a
two-register value (FC6 always is one), answers exception 02; a non-finite
value, or one outside the tag's range, answers 03; any other function, 01.

## Limits

- **A local demo.** FUXA runs with authentication off and DSE's Modbus server
  has none either: anyone who can reach ports 1881 and 5020 can operate the
  plant. So `docker-compose.yml` publishes both on 127.0.0.1 only: the
  browser and any Modbus tool on this machine reach them, and FUXA reaches
  `dse:5020` over the compose network. Docker's port rules bypass the host's
  firewall, so to open either port to your network you must do it
  deliberately: change its bind address in `ports` (`"127.0.0.1:1881:1881"`
  to `"1881:1881"`, or to one interface's address), and only on a network
  where everyone who can reach it may start, stop and trip the line and
  rewrite the HMI.
- **Edits stay in FUXA's volume.** Whatever you change in FUXA's editor
  (<http://localhost:1881/editor>) is kept in the `fuxa-appdata` volume, and
  `fuxa-init` does not overwrite a project FUXA already has. The committed
  project is generated by `generate-project.py` (see "Changing the
  project"), so an edit you want to keep must be folded back into the
  generator; a project exported from the editor and committed as is would be
  lost at the next regeneration. To go back to the committed project,
  `docker compose down -v` and start again.
- **Real time, not replay.** Under `dse serve` the plant runs on the wall
  clock and the operator's writes land whenever they arrive, so a session is
  not reproducible tick for tick the way `dse run` is.
- **One plant.** The wheel-line HMI is a follow-up.

## Changing the project

`mine-conveyors.fuxap.json` is generated; do not edit it by hand. Change
`generate-project.py`, then, from the repository root:

```bash
dotnet run --project src/Dse.Cli -- modbus-map samples/mine-conveyors/plant.json --format fuxa --out fuxa-tags.json
python3 -I hmi/fuxa/generate-project.py fuxa-tags.json hmi/fuxa/mine-conveyors.fuxap.json
rm fuxa-tags.json
```

Regenerate the same way when the plant's tags change: the tests fail until
the project's device tags equal `dse modbus-map … --format fuxa` again.

## Cleaning up

```bash
docker compose down -v --rmi all
```

removes the containers, the network, FUXA's volumes, and the images the stack
built or pulled. Docker keeps the build cache of the two image builds; `docker
builder prune` removes it.

## Files

| file | what it is |
|---|---|
| `docker-compose.yml` | the three services |
| `Dockerfile` | the `dse` image: the .NET SDK builds `src/Dse.Cli`, the .NET runtime runs it, with `samples/` |
| `Dockerfile.dockerignore` | sends only `Directory.Build.props`, `src/` and `samples/` to that build |
| `fuxa.Dockerfile` | `frangoteam/fuxa:1.3.4` with the `modbus-serial` 8.0.19 driver installed |
| `generate-project.py` | writes `mine-conveyors.fuxap.json` from the register map: its source |
| `mine-conveyors.fuxap.json` | the FUXA project: the `DSE` Modbus device, the three views, the alarms, the trend charts and the `pulse` script |
| `smoke-check.sh` | the scripted check above |
