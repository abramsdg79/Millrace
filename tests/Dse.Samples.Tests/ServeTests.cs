using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Dse.Cli;
using Dse.Tests.Shared;

namespace Dse.Samples.Tests;

/// <summary>
/// End to end (plan 8 criterion 5): <c>dse serve</c> runs the mine plant
/// in-process at 20 times real time on a port the system picks; a test-side
/// Modbus client writes the start sequence's coil and reads CV001's speed
/// rise past the 1.74 m/s the sequence proves it at. Every wait is bounded.
/// </summary>
public partial class ServeTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"Listening on 0\.0\.0\.0:(\d+) ")]
    private static partial Regex Listening();

    [Fact]
    public async Task AStartSequenceWrittenOverModbusRunsCv001UpToSpeed()
    {
        Dictionary<string, (string Area, int Offset)> map = Map();
        (string startArea, int startCoil) = map["SEQ_START.Start"];
        (string speedArea, int speedRegister) = map["CV001.Speed"];
        (string runningArea, int running) = map["SEQ_START.Running"];
        Assert.Equal(("coils", "input registers", "discrete inputs"), (startArea, speedArea, runningArea));

        var stdout = new LockedWriter();
        var stderr = new LockedWriter();
        using var stop = new CancellationTokenSource();

        // The run blocks its thread for as long as it serves, so it gets a thread of its own:
        // on a plain Task.Run it holds a thread-pool thread, and the server and this test's
        // client starve for the pool's slow growth on a one- or two-core machine.
        Task<int> serving = Task.Factory.StartNew(
            () => CliApp.Run(["serve", Sample.Plant, "--port", "0", "--speed", "20"], stdout, stderr, stop.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        try
        {
            int port = await PortOf(stdout, stderr, serving);

            // The client reads sockets synchronously, so it too gets a thread of its own.
            await Task.Factory.StartNew(
                () => Drive(port, startCoil, speedRegister, running),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).WaitAsync(Patience);
        }
        finally
        {
            await stop.CancelAsync();
        }

        Assert.Equal(ExitCodes.Ok, await serving.WaitAsync(Patience));
        Assert.Empty(stderr.Text);
        Assert.Contains("Stopped at 2026-03-02 06:", stdout.Text, StringComparison.Ordinal);
    }

    /// <summary>Starts the sequence over Modbus and reads CV001 up to speed. Blocks on sockets, so it runs on its own thread.</summary>
    private static void Drive(int port, int startCoil, int speedRegister, int running)
    {
        using var client = new ModbusClient(new IPEndPoint(IPAddress.Loopback, port));

        Assert.True(ModbusClient.Float(client.ReadInputRegisters(speedRegister, 2), 0) < 0.1f, "CV001 runs before the start.");
        client.WriteCoil(startCoil, true);

        float speed = 0f;
        var clock = Stopwatch.StartNew();
        bool sawRunning = false;
        while (speed < 1.74f)
        {
            Assert.True(clock.Elapsed < Patience, $"CV001 reached only {speed} m/s in {Patience.TotalSeconds} s.");
            Thread.Sleep(50);
            sawRunning |= client.ReadDiscreteInputs(running, 1)[0];
            speed = ModbusClient.Float(client.ReadInputRegisters(speedRegister, 2), 0);
        }

        Assert.True(sawRunning, "SEQ_START never read Running.");
        client.WriteCoil(startCoil, false);
    }

    /// <summary>Every tag's area and wire offset, from <c>dse modbus-map --format csv</c>, as an integrator would read them.</summary>
    private static Dictionary<string, (string Area, int Offset)> Map()
    {
        CliRun run = Cli.Run("modbus-map", Sample.Plant, "--format", "csv");
        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        return run.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(line => line.Split(','))
            .ToDictionary(cells => cells[4], cells => (cells[0], int.Parse(cells[2], CultureInfo.InvariantCulture)), StringComparer.Ordinal);
    }

    private static async Task<int> PortOf(LockedWriter stdout, LockedWriter stderr, Task<int> serving)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            Match match = Listening().Match(stdout.Text);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            Assert.False(serving.IsCompleted, $"dse serve exited before listening: {stderr.Text}");
            Assert.True(clock.Elapsed < Patience, "dse serve did not say where it listens.");
            await Task.Delay(20);
        }
    }

    /// <summary>A writer the serving thread writes and the test thread reads.</summary>
    private sealed class LockedWriter : TextWriter
    {
        private readonly Lock _sync = new();
        private readonly StringBuilder _text = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Text
        {
            get
            {
                lock (_sync)
                {
                    return _text.ToString();
                }
            }
        }

        public override void Write(char value)
        {
            lock (_sync)
            {
                _text.Append(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_sync)
            {
                _text.Append(value);
            }
        }
    }
}
