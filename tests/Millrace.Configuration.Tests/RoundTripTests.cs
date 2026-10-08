using Millrace.Components.Conveyors;
using Millrace.Components.Flow;
using Millrace.Components.Mechanical;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Time;
using Millrace.Io;

namespace Millrace.Configuration.Tests;

/// <summary>
/// The spec's proof that factories build what constructors build: the same
/// plant, once from JSON and once by hand, driven identically, must write the
/// same event log byte for byte.
/// </summary>
public class RoundTripTests
{
    private static Simulation ByHand()
    {
        var ore = new MaterialType("Ore", PayloadKind.Bulk);
        var feed = new BulkSource("Feed", ore, 20.0, new MaterialProperties(2000.0, 0.03, 15.0));
        var conveyor = new Conveyor("CV001", new ConveyorOptions(
            LengthM: 10.0, CellSizeM: 0.5, BeltWidthM: 0.8, AngleOfReposeDeg: 20.0, MaterialDensityKgM3: 2000.0,
            EmptyBeltMassKg: 250.0, FrictionCoefficient: 0.04, PulleyDiameterM: 0.5, GearRatio: 20.0,
            Motor: new MotorRating(750.0, 150.0, 2.0), TailDragN: 80.0));
        var chute = new TransferChute("Chute", capacityKg: 200.0);
        var pile = new BulkSink("Pile");
        feed.Out.ConnectTo(conveyor.Inlet("In"));
        conveyor.Outlet("Out").ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);

        return new SimulationBuilder(new SimulationOptions
        {
            Seed = 1UL,
            StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
            TimeStep = TimeSpan.FromMilliseconds(10),
        }).Add(pile).Add(chute).Add(conveyor).Add(feed).Build();
    }

    private static Simulation FromJson() => Plants.Load(Corpus.Read("valid", "conveyor-line.json")).Builder!.Build();

    /// <summary>Reset the safety relay, start, run loaded, pull a key, run on.</summary>
    private static void Operate(Simulation simulation)
    {
        simulation.IO.WriteBool("CV001.SafetyReset", true);
        simulation.RunFor(TimeSpan.FromMilliseconds(500));
        simulation.IO.WriteBool("CV001.SafetyReset", false);
        simulation.IO.WriteBool("CV001.Start", true);
        simulation.RunFor(TimeSpan.FromSeconds(30));
        simulation.IO.WriteBool("CV001.PullKey1", true);
        simulation.RunFor(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TheTagDirectoriesAreIdentical()
    {
        Assert.Equal(ByHand().IO.Directory.ToText(), FromJson().IO.Directory.ToText());
    }

    [Fact]
    public void TheEventLogsAreByteIdentical()
    {
        Simulation byHand = ByHand();
        Simulation fromJson = FromJson();

        Operate(byHand);
        Operate(fromJson);

        string expected = byHand.Events.ToText();
        Assert.Contains("WRITE", expected, StringComparison.Ordinal);
        Assert.True(expected.Split('\n').Length > 5, "The scenario should produce a real event log, not a near-empty one.");
        Assert.Equal(expected, fromJson.Events.ToText());
        Assert.Equal(byHand.IO.ReadDouble("Pile.Received"), fromJson.IO.ReadDouble("Pile.Received"));
        Assert.Equal(byHand.Telemetry.Read("CV001.Motor.ThermalState"), fromJson.Telemetry.Read("CV001.Motor.ThermalState"));
    }
}
