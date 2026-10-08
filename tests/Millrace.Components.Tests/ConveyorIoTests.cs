using Millrace.Components.Conveyors;
using Millrace.Components.Flow;
using Millrace.Components.Instruments;
using Millrace.Components.Mechanical;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Components.Tests;

/// <summary>The conveyor operated the way a SCADA would: through tags only.</summary>
public class ConveyorIoTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static readonly ConveyorOptions Cv001 = new(
        LengthM: 10.0,
        CellSizeM: 0.5,
        BeltWidthM: 0.8,
        AngleOfReposeDeg: 20.0,
        MaterialDensityKgM3: 2000.0,
        EmptyBeltMassKg: 250.0,
        FrictionCoefficient: 0.04,
        PulleyDiameterM: 0.5,
        GearRatio: 20.0,
        Motor: new MotorRating(750.0, 150.0, 2.0),
        TailDragN: 80.0);

    private static SimulationOptions Options => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private static Simulation Build()
    {
        var feed = new BulkSource("Feed", Ore, 20.0, new MaterialProperties(2000.0, 0.03, 15.0));
        var conveyor = new Conveyor("CV001", Cv001);
        var chute = new TransferChute("Chute", capacityKg: 200.0);
        var pile = new BulkSink("Pile");

        feed.Out.ConnectTo(conveyor.Inlet("In"));
        conveyor.Outlet("Out").ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);

        return new SimulationBuilder(Options).Add(pile).Add(chute).Add(conveyor).Add(feed).Build();
    }

    private static void StartUp(Simulation sim)
    {
        sim.IO.WriteBool("CV001.SafetyReset", true);
        sim.RunFor(TimeSpan.FromMilliseconds(50));
        sim.IO.WriteBool("CV001.SafetyReset", false);
        sim.IO.WriteBool("CV001.Start", true);
    }

    [Fact]
    public void TheConveyorPublishesExactlyItsFace()
    {
        Simulation sim = Build();

        string[] names = sim.IO.Directory.Tags.Select(t => t.Name).Where(n => n.StartsWith("CV001.", StringComparison.Ordinal)).ToArray();

        Assert.Equal(
            new[]
            {
                "CV001.Contactor", "CV001.Current", "CV001.EStop", "CV001.EStop.Ok", "CV001.Permit",
                "CV001.PullKey1", "CV001.PullKey1.Ok", "CV001.PullKey2", "CV001.PullKey2.Ok",
                "CV001.Reset", "CV001.SafetyOk", "CV001.SafetyReset", "CV001.Speed",
                "CV001.Start", "CV001.Stopped", "CV001.TonnesPerHour", "CV001.Tripped",
                "CV001.ZeroSpeed.Value",
            },
            names);
        Assert.Equal(TagAccess.ReadWrite, sim.IO.Directory.Find("CV001.Start").Access);
        Assert.Equal(("t/h", TagAccess.ReadOnly), (sim.IO.Directory.Find("CV001.TonnesPerHour").Unit, sim.IO.Directory.Find("CV001.TonnesPerHour").Access));
        Assert.DoesNotContain(sim.IO.Directory.Tags, t => t.Name.Contains(".Motor.", StringComparison.Ordinal));
    }

    [Fact]
    public void StartsThroughTagsAndReportsThroughTags()
    {
        Simulation sim = Build();
        StartUp(sim);

        sim.RunFor(TimeSpan.FromSeconds(40));

        Assert.True(sim.IO.ReadBool("CV001.Contactor"));
        Assert.False(sim.IO.ReadBool("CV001.Tripped"));
        Assert.True(sim.IO.ReadBool("CV001.SafetyOk"));
        Assert.InRange(sim.IO.ReadDouble("CV001.Speed"), 1.75, 1.95);
        Assert.InRange(sim.IO.ReadDouble("CV001.TonnesPerHour"), 68.0, 76.0);
        Assert.InRange(sim.IO.ReadDouble("CV001.Current"), 1.2, 1.6);
        Assert.True(sim.IO.Read("CV001.Speed").Quality.IsGood);
        Assert.Contains(sim.Events.Records, r => r.Source == "CV001.Start" && r.Code == "WRITE");
    }

    [Fact]
    public void APullKeyWrittenAsATagStopsTheBelt()
    {
        Simulation sim = Build();
        StartUp(sim);
        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.IO.WriteBool("CV001.PullKey1", true);
        sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.False(sim.IO.ReadBool("CV001.PullKey1.Ok"));
        Assert.False(sim.IO.ReadBool("CV001.SafetyOk"));
        Assert.False(sim.IO.ReadBool("CV001.Contactor"));
        Assert.True(sim.IO.ReadBool("CV001.Stopped"));
        Assert.True(sim.IO.ReadDouble("CV001.Speed") < 0.05);
    }

    [Fact]
    public void APermitWrittenFalseHoldsTheBeltStoppedUntilItIsWrittenTrue()
    {
        Simulation sim = Build();
        sim.IO.WriteBool("CV001.Permit", false);
        StartUp(sim);
        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.True(sim.IO.ReadBool("CV001.Start"));
        Assert.False(sim.IO.ReadBool("CV001.Contactor"));
        Assert.True(sim.IO.ReadBool("CV001.Stopped"));
        Assert.DoesNotContain(sim.Events.Records, r => r.Source == "CV001.Starter" && r.Code == "CONTACTOR_CLOSED");

        sim.IO.WriteBool("CV001.Permit", true);
        sim.RunFor(TimeSpan.FromSeconds(10));

        Assert.True(sim.IO.ReadBool("CV001.Contactor"));
        Assert.InRange(sim.IO.ReadDouble("CV001.Speed"), 1.75, 1.95);
    }

    [Fact]
    public void AFailedSpeedSensorReadsBadOnTheWire()
    {
        Simulation sim = Build();
        StartUp(sim);
        sim.RunFor(TimeSpan.FromSeconds(30));

        sim.InjectFaultIn(TimeSpan.Zero, "CV001.SpeedSensor", InstrumentFaults.FailHigh);
        sim.Tick();

        TagValue speed = sim.IO.Read("CV001.Speed");
        Assert.Equal(TagQuality.Bad(QualityDetail.SensorFailure), speed.Quality);
        Assert.Equal(sim.IO.Directory.Find("CV001.Speed").RangeHigh, speed.AsDouble);
        Assert.True(sim.IO.Read("CV001.Current").Quality.IsGood);
    }

    [Fact]
    public void WritingAReadOnlyTagIsRejectedAtTheCallSite()
    {
        Simulation sim = Build();

        Assert.Throws<InvalidOperationException>(() => sim.IO.WriteBool("CV001.Contactor", true));
        Assert.Throws<InvalidOperationException>(() => sim.IO.WriteDouble("CV001.Start", 1.0));
    }
}
