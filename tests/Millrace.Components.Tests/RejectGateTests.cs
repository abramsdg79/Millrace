using Millrace.Components.Flow;
using Millrace.Components.Instruments;
using Millrace.Components.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Logging;
using Millrace.Core.Time;
using Millrace.Io;
using Xunit;

namespace Millrace.Components.Tests;

public class RejectGateTests
{
    private static readonly MaterialType Billet = new("Billet", PayloadKind.Discrete);

    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(0.5),
    };

    private sealed record Rig(Simulation Sim, ItemSource Source, RejectGate Gate, ItemSink Good, ItemSink Scrap);

    /// <summary>
    /// Billets every <paramref name="interval"/> s at 900 °C into a gate with a 2 s dwell;
    /// Out feeds Good unless <paramref name="connectOut"/> is false, RejectOut feeds Scrap
    /// unless <paramref name="connectReject"/> is false.
    /// </summary>
    private static Rig Build(
        double interval = 1.0,
        int goodCapacity = int.MaxValue,
        int scrapCapacity = int.MaxValue,
        bool connectReject = true,
        bool connectOut = true,
        Switch? reject = null,
        Pyrometer? pyrometer = null,
        RejectGate? gate = null)
    {
        var source = new ItemSource("Billets", Billet, 20.0, interval, new MaterialProperties(7800.0, 0.0, 900.0));
        gate ??= new RejectGate("Gate", 2.0);
        var good = new ItemSink("Good", goodCapacity);
        var scrap = new ItemSink("Scrap", scrapCapacity);
        source.Out.ConnectTo(gate.In);
        if (connectOut)
        {
            gate.Out.ConnectTo(good.In);
        }

        if (connectReject)
        {
            gate.RejectOut.ConnectTo(scrap.In);
        }

        var builder = new SimulationBuilder(Options()).Add(good).Add(scrap).Add(gate).Add(source);
        if (reject is not null)
        {
            reject.Out.ConnectTo(gate.Reject);
            builder.Add(reject);
        }

        if (pyrometer is not null)
        {
            builder.Add(pyrometer);
        }

        return new Rig(builder.Build(), source, gate, good, scrap);
    }

    private static IEnumerable<SimEventRecord> Rejections(Simulation sim) =>
        sim.Events.Records.Where(r => r.Source == "Gate" && r.Code == "REJECTED");

    [Fact]
    public void AnItemDwellsThenLeavesByOutAndTheNextEntersOnTheSameTick()
    {
        Rig rig = Build();
        var onStation = new List<long>();

        for (int tick = 0; tick < 13; tick++)
        {
            rig.Sim.Tick();
            onStation.Add(rig.Gate.Item?.Id ?? 0L);
        }

        // Item 1 is deposited on tick 2, dwells through ticks 3-6 (4 × 0.5 s = 2 s)
        // and leaves on tick 7, when item 2 takes its place.
        Assert.Equal([0L, 0L, 1L, 1L, 1L, 1L, 1L, 2L, 2L, 2L, 2L, 2L, 3L], onStation);
        Assert.Equal(2L, rig.Good.LastItem!.Id);
        Assert.Equal(40.0, rig.Good.MassReceived);
        Assert.Equal(0.0, rig.Scrap.MassReceived);
        Assert.Empty(Rejections(rig.Sim));
    }

    [Fact]
    public void AWriteThatLandsOnTheReleaseTickDivertsThatItem()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.FromSeconds(3.5), "Gate.Reject", TagValue.Bool(true));   // tick 7

        rig.Sim.RunFor(TimeSpan.FromSeconds(4));   // ticks 0-7

        Assert.Equal(1L, rig.Scrap.LastItem!.Id);
        Assert.Equal(0.0, rig.Good.MassReceived);
        SimEventRecord rejected = Assert.Single(Rejections(rig.Sim));
        Assert.Equal((7L, "Item 1 rejected."), (rejected.Tick, rejected.Message));
    }

    [Fact]
    public void AWriteOneTickLateDivertsTheNextItem()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.FromSeconds(4), "Gate.Reject", TagValue.Bool(true));   // tick 8

        rig.Sim.RunFor(TimeSpan.FromSeconds(6.5));   // ticks 0-12

        Assert.Equal(1L, rig.Good.LastItem!.Id);
        Assert.Equal(2L, rig.Scrap.LastItem!.Id);
        Assert.Equal(12L, Assert.Single(Rejections(rig.Sim)).Tick);
    }

    [Fact]
    public void OnlyTheValueOnTheReleaseTickCounts()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.FromSeconds(1), "Gate.Reject", TagValue.Bool(true));      // before item 1 arrives
        rig.Sim.WriteAt(TimeSpan.FromSeconds(3.5), "Gate.Reject", TagValue.Bool(false));   // its release tick

        rig.Sim.RunFor(TimeSpan.FromSeconds(4));

        Assert.Equal(1L, rig.Good.LastItem!.Id);
        Assert.Null(rig.Scrap.LastItem);
    }

    [Fact]
    public void AnItemThatOutRefusesWaitsAndTheChoiceIsTakenAgainOnEveryTick()
    {
        Rig rig = Build(goodCapacity: 0);

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);
        Assert.True(rig.Gate.Occupied.Value);
        Assert.Equal(0L, rig.Gate.Passed.Value);

        rig.Sim.WriteIn(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));   // tick 20
        rig.Sim.Tick();

        Assert.Equal(1L, rig.Scrap.LastItem!.Id);
        Assert.Equal(20L, Assert.Single(Rejections(rig.Sim)).Tick);
        Assert.Equal(2L, rig.Gate.Item!.Id);
    }

    [Fact]
    public void AnItemTheRejectOutletRefusesWaitsUntilRejectFalls()
    {
        Rig rig = Build(scrapCapacity: 0);
        rig.Sim.WriteAt(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);
        Assert.Equal(0.0, rig.Good.MassReceived);

        rig.Sim.WriteIn(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(false));
        rig.Sim.Tick();

        Assert.Equal(1L, rig.Good.LastItem!.Id);
        Assert.Empty(Rejections(rig.Sim));
    }

    [Fact]
    public void AnUnconnectedRejectOutletHoldsARejectedItemOnTheStation()
    {
        var reject = new Switch("Kick", value: true);
        Rig rig = Build(connectReject: false, reject: reject);

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);

        reject.Value = false;
        rig.Sim.Tick();
        Assert.Equal(1L, rig.Good.LastItem!.Id);
    }

    [Fact]
    public void AnUnconnectedOutHoldsAPassedItemOnTheStation()
    {
        Rig rig = Build(connectOut: false);

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, rig.Gate.Item!.Id);

        rig.Sim.WriteIn(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));   // tick 20
        rig.Sim.Tick();
        Assert.Equal(1L, rig.Scrap.LastItem!.Id);
        Assert.Equal(20L, Assert.Single(Rejections(rig.Sim)).Tick);
    }

    [Fact]
    public void ADepositOnAnOccupiedStationIsRefused()
    {
        var gate = new RejectGate("G", 1.0);
        gate.DepositItem(gate.In, new ItemInstance(1L, Billet, 20.0, default));

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => gate.DepositItem(gate.In, new ItemInstance(2L, Billet, 20.0, default)));
        Assert.Equal("Reject gate 'G' already holds Billet#1; it takes one item at a time.", refused.Message);
    }

    [Fact]
    public void TheStationHoldsOneItemAtATime()
    {
        Rig rig = Build(interval: 0.5, goodCapacity: 0);

        rig.Sim.RunFor(TimeSpan.FromSeconds(20));

        Assert.Equal(1L, rig.Gate.Item!.Id);
        Assert.Equal(20.0, rig.Gate.MassHeld);
        Assert.False(rig.Gate.CanAcceptItem(rig.Gate.In, new ItemInstance(99L, Billet, 20.0, default)));
        Assert.True(rig.Source.Queued.Value > 30);
        Assert.Equal(0.0, rig.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void AStuckKickerPassesEveryItemUntilCleared()
    {
        Rig rig = Build();
        rig.Sim.WriteAt(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));
        rig.Sim.InjectFaultAt(TimeSpan.Zero, "Gate", RejectGate.Stuck);
        rig.Sim.ClearFaultAt(TimeSpan.FromSeconds(7), "Gate", RejectGate.Stuck);   // tick 14

        rig.Sim.RunFor(TimeSpan.FromSeconds(9));   // items leave on ticks 7, 12 and 17

        Assert.Equal(40.0, rig.Good.MassReceived);
        Assert.Equal(2L, rig.Good.LastItem!.Id);
        Assert.Equal(3L, rig.Scrap.LastItem!.Id);
    }

    [Fact]
    public void AKickerThatSticksWhileAnItemWaitsOnARefusedRejectSendsItOut()
    {
        Rig rig = Build(scrapCapacity: 0);
        rig.Sim.WriteAt(TimeSpan.Zero, "Gate.Reject", TagValue.Bool(true));

        rig.Sim.RunFor(TimeSpan.FromSeconds(10));
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "Gate", RejectGate.Stuck);
        rig.Sim.Tick();

        Assert.Equal(1L, rig.Good.LastItem!.Id);
    }

    [Fact]
    public void APyrometerOnTheStationSeesTheItemOrItsBackground()
    {
        var gate = new RejectGate("Gate", 2.0);
        var pyrometer = new Pyrometer("TT", gate, positionM: 0.0, windowM: 0.0, new InstrumentSpec("degC", 0.0, 1500.0));
        Rig rig = Build(interval: 5.0, pyrometer: pyrometer, gate: gate);
        var readings = new List<(bool Occupied, double Reading)>();

        for (int tick = 0; tick < 40; tick++)
        {
            bool occupied = gate.Item is not null;   // what phase 2 of the next tick sees
            rig.Sim.Tick();
            readings.Add((occupied, pyrometer.Value.Value));
        }

        Assert.Contains((true, 900.0), readings);
        Assert.Contains((false, 20.0), readings);
        Assert.All(readings, r => Assert.Equal(r.Occupied ? 900.0 : 20.0, r.Reading));
    }

    [Fact]
    public void EveryItemThatEntersLeavesByExactlyOneOutlet()
    {
        var reject = new Switch("Kick");
        Rig rig = Build(interval: 0.5, reject: reject);

        for (int tick = 0; tick < 240; tick++)
        {
            reject.Value = tick % 7 < 3;
            rig.Sim.Tick();
        }

        rig.Sim.Tick();   // publish the last hand-off
        Assert.True(rig.Good.Count.Value > 5);
        Assert.True(rig.Scrap.Count.Value > 5);
        Assert.Equal(rig.Good.Count.Value, rig.Gate.Passed.Value);
        Assert.Equal(rig.Scrap.Count.Value, rig.Gate.Rejected.Value);
        Assert.Equal((double)rig.Gate.Passed.Value, rig.Sim.Telemetry.Read("Gate.Passed"));
        Assert.Equal((double)rig.Gate.Rejected.Value, rig.Sim.Telemetry.Read("Gate.Rejected"));
        Assert.Equal(rig.Scrap.Count.Value, Rejections(rig.Sim).LongCount());
        Assert.Equal(
            rig.Sim.MassBalance.Created,
            rig.Good.MassReceived + rig.Scrap.MassReceived + rig.Gate.MassHeld + rig.Source.MassHeld,
            9);
        Assert.Equal(0.0, rig.Sim.MassBalance.Drift, 9);
    }

    [Fact]
    public void TheGatePublishesAWritableRejectAndReadOnlyState()
    {
        Assert.Equal(
            new[]
            {
                ("Reject", TagKind.Bool, TagAccess.ReadWrite),
                ("Occupied", TagKind.Bool, TagAccess.ReadOnly),
                ("Passed", TagKind.Int64, TagAccess.ReadOnly),
                ("Rejected", TagKind.Int64, TagAccess.ReadOnly),
            },
            new RejectGate("G", 1.0).DescribeTags().Select(t => (t.Name, t.Kind, t.Access)));
    }

    [Fact]
    public void ARejectInputASignalDrivesIsPublishedReadOnly()
    {
        Rig free = Build();
        Rig wired = Build(reject: new Switch("Kick"));

        Assert.Equal(TagAccess.ReadWrite, free.Sim.IO.Directory.Find("Gate.Reject").Access);
        Assert.Equal(TagAccess.ReadOnly, wired.Sim.IO.Directory.Find("Gate.Reject").Access);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsADwellThatIsNotAPositiveFiniteTime(double dwell)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RejectGate("G", dwell));
    }
}
