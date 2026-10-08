using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Millrace.Configuration;
using Millrace.Core;
using Millrace.Modbus;
using Millrace.Realtime;
using Millrace.Scenarios;

namespace Millrace.Cli.Commands;

/// <summary>
/// <c>millrace serve</c> (plan 8): runs a plant — and a scenario's timeline, if one
/// is given — paced to the wall clock, and serves its tags over Modbus TCP
/// until Ctrl+C, SIGTERM or the caller's cancellation. The simulation ticks on
/// this thread; the server answers on the thread pool, reading the image the
/// last tick published and queueing writes for the next tick's phase 1.
/// </summary>
internal static class Serve
{
    /// <summary>The port when <c>--port</c> is not given: 502 needs privileges, so the conventional unprivileged alternative.</summary>
    public const int DefaultPort = 5020;

    public static int Run(CliContext context)
    {
        int exit = Load(context, out Simulation? simulation);
        if (exit != ExitCodes.Ok)
        {
            return exit;
        }

        int port = context.CommandLine.Single(CommandTable.Port) is { } text
            ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
            : DefaultPort;
        IPAddress bind = context.CommandLine.Single(CommandTable.Bind) is { } address ? IPAddress.Parse(address) : IPAddress.Loopback;
        double speed = context.CommandLine.Single(CommandTable.Speed) is { } factor
            ? double.Parse(factor, NumberStyles.Float, CultureInfo.InvariantCulture)
            : 1.0;

        simulation!.Initialize();
        RegisterMap map = RegisterMap.Build(simulation.IO.Directory);

        // Signals are taken before the port opens, so a Ctrl+C or SIGTERM that
        // arrives as soon as "Listening on" is printed stops the run cleanly.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.Cancellation);
        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop(stop));
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop(stop));

        var server = new ModbusServer(map, simulation.IO.Snapshot, new CommandBus(simulation.IO));
        try
        {
            IPEndPoint endpoint;
            try
            {
                endpoint = server.Start(new IPEndPoint(bind, port));
            }
            catch (SocketException ex)
            {
                context.Err.Write(string.Create(CultureInfo.InvariantCulture, $"Cannot listen on port {port}: {ex.Message}\n"));
                return ExitCodes.Unreadable;
            }

            context.Out.Write(string.Create(CultureInfo.InvariantCulture, $"""
                Serving {context.CommandLine.Argument}: {Counts(map)}.
                Listening on {endpoint} (Modbus TCP, any unit id) at {speed:R}x real time. Press Ctrl+C to stop.

                """).ReplaceLineEndings("\n"));
            context.Out.Flush();

            ExecutionMode mode = speed == 1.0 ? ExecutionMode.RealTime : ExecutionMode.Scaled;
            new SimulationRunner(simulation, mode, speed).RunFor(TimeSpan.MaxValue, stop.Token);

            context.Out.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"Stopped at {simulation.Clock.Now:yyyy-MM-dd HH:mm:ss.fff} after {simulation.Clock.TickCount} ticks.\n"));
            return ExitCodes.Ok;
        }
        finally
        {
            server.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>One line, such as "124 tags as 36 coils, 59 discrete inputs, 56 input registers and 2 holding registers".</summary>
    internal static string Counts(RegisterMap map)
    {
        int Tags(ModbusArea area) => map.Entries.Count(e => e.Area == area);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{map.Entries.Count} tags as {Tags(ModbusArea.Coils)} coils, {Tags(ModbusArea.DiscreteInputs)} discrete inputs, " +
            $"{map.Size(ModbusArea.InputRegisters)} input registers and {map.Size(ModbusArea.HoldingRegisters)} holding registers");
    }

    private static Action<PosixSignalContext> Stop(CancellationTokenSource stop) => signal =>
    {
        signal.Cancel = true;
        try
        {
            stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A signal during teardown: the run is already over.
        }
    };

    /// <summary>The plant alone, or the plant bound to <c>--scenario</c>'s timeline. Reports failure as <c>validate</c> and <c>run</c> do.</summary>
    private static int Load(CliContext context, out Simulation? simulation)
    {
        simulation = null;
        string? scenarioPath = context.CommandLine.Single(CommandTable.Scenario);
        if (scenarioPath is null)
        {
            return PlantFile.TryBuild(context, out LoadResult? _, out simulation);
        }

        string plantPath = context.CommandLine.Argument!;
        if (!PlantFile.TryRead(context, scenarioPath, out string scenarioJson))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioParseResult parsed = ScenarioLoader.Parse(scenarioJson);
        if (parsed.Scenario is null)
        {
            PlantFile.ReportDiagnostics(context, scenarioPath, parsed.ToText(), parsed.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
            return ExitCodes.PlantInvalid;
        }

        string named = parsed.Scenario.ResolvePlantPath(scenarioPath);
        if (!string.Equals(named, Path.GetFullPath(plantPath), StringComparison.Ordinal))
        {
            context.Err.Write(
                $"'--scenario {scenarioPath}' runs the plant '{named}', not '{Path.GetFullPath(plantPath)}'.\n" +
                "Give the plant the scenario names, or a scenario written for this plant.\n");
            return ExitCodes.Usage;
        }

        if (!PlantFile.TryRead(context, plantPath, out string plantJson))
        {
            return ExitCodes.Unreadable;
        }

        ScenarioBinding binding = ScenarioRunner.Bind(parsed.Scenario, plantJson, context.Catalogue);
        if (binding.Simulation is null)
        {
            PlantFile.ReportDiagnostics(context, scenarioPath, binding.ToText(), binding.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error));
            return ExitCodes.PlantInvalid;
        }

        simulation = binding.Simulation;
        return ExitCodes.Ok;
    }
}
