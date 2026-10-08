using Millrace.Components.Conveyors;
using Millrace.Components.Flow;
using Millrace.Components.Mechanical;
using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Time;
using Millrace.Io;

namespace Millrace.Realtime.Tests;

/// <summary>A conveyor operated and observed exclusively through the real-time layer.</summary>
public class ConveyorRealtimeTests
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

    private sealed class Line : IDisposable
    {
        public Line()
        {
            var feed = new BulkSource("Feed", Ore, 20.0, new MaterialProperties(2000.0, 0.03, 15.0));
            var conveyor = new Conveyor("CV001", Cv001);
            var chute = new TransferChute("Chute", capacityKg: 200.0);
            var pile = new BulkSink("Pile");
            feed.Out.ConnectTo(conveyor.Inlet("In"));
            conveyor.Outlet("Out").ConnectTo(chute.In);
            chute.Out.ConnectTo(pile.In);

            Sim = new SimulationBuilder(new SimulationOptions
            {
                Seed = 1UL,
                StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
                TimeStep = TimeSpan.FromMilliseconds(10),
            }).Add(pile).Add(chute).Add(conveyor).Add(feed).Build();

            Hub = new RealtimeHub(Sim.IO.Directory, ringCapacity: 1 << 14);
            Sim.AttachFrameSink(Hub);
            Bus = new CommandBus(Sim.IO);
            Hmi = Hub.Subscribe(new SubscriptionOptions
            {
                Policy = BackpressurePolicy.Conflate,
                Prefixes = ["CV001"],
                DeadbandPercentOfRange = 0.5,
                Decimation = TimeSpan.FromMilliseconds(100),
            });
            Historian = Hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 1 << 16 });
        }

        public Simulation Sim { get; }

        public RealtimeHub Hub { get; }

        public CommandBus Bus { get; }

        public Subscription Hmi { get; }

        public Subscription Historian { get; }

        /// <summary>Runs simulation time, pumping every 100 ms of it as a dispatcher would.</summary>
        public void Run(double seconds)
        {
            int slices = (int)Math.Round(seconds * 10.0);
            for (int i = 0; i < slices; i++)
            {
                Sim.RunFor(TimeSpan.FromMilliseconds(100));
                Hub.Pump();
            }
        }

        public void StartUp()
        {
            Assert.Equal(CommandOutcome.Accepted, Bus.WriteBool("CV001.SafetyReset", true));
            Sim.RunFor(TimeSpan.FromMilliseconds(50));
            Assert.Equal(CommandOutcome.Accepted, Bus.WriteBool("CV001.SafetyReset", false));
            Assert.Equal(CommandOutcome.Accepted, Bus.WriteBool("CV001.Start", true));
        }

        public static List<FrameDelta> Drain(Subscription sub)
        {
            var deltas = new List<FrameDelta>();
            while (sub.TryRead(out FrameDelta delta))
            {
                deltas.Add(delta);
            }

            return deltas;
        }

        public void Dispose()
        {
            Hmi.Dispose();
            Historian.Dispose();
            Hub.Dispose();
        }
    }

    [Fact]
    public void TheLiveStateReportsTheRunningConveyor()
    {
        using var line = new Line();
        line.StartUp();

        line.Run(40.0);

        LiveState state = line.Hub.State;
        Assert.Equal(line.Sim.Clock.TickCount - 1, state.Tick);
        Assert.True(state.Get("CV001.Contactor").Value.AsBool);
        Assert.False(state.Get("CV001.Tripped").Value.AsBool);
        Assert.InRange(state.Get("CV001.Speed").Value.AsDouble, 1.75, 1.95);
        Assert.InRange(state.Get("CV001.TonnesPerHour").Value.AsDouble, 68.0, 76.0);
        Assert.True(state.Get("CV001.Speed").Value.Quality.IsGood);
        Assert.True(state.Get("CV001.Contactor").LastChangeTick > 0);
        Assert.Equal(0L, line.Hub.DroppedFrames);
        Assert.Contains(state.Snapshot().RecentEvents, e => e.Code == "AT_SPEED");
    }

    [Fact]
    public void TheHistorianSeesEveryTickAndEveryEventInOrder()
    {
        using var line = new Line();
        line.StartUp();
        line.Run(40.0);

        List<FrameDelta> deltas = Line.Drain(line.Historian);

        Assert.False(line.Historian.IsFaulted);
        Assert.NotEmpty(deltas);
        for (int i = 1; i < deltas.Count; i++)
        {
            Assert.True(deltas[i].Tick > deltas[i - 1].Tick);
        }

        IEnumerable<DiscreteEvent> events = deltas.SelectMany(d => d.Events);
        Assert.Contains(events, e => e.Source == "CV001.Start" && e.Code == "WRITE");
        Assert.Contains(events, e => e.Source == "CV001.Safety" && e.Code == "SAFETY_RESET");
        Assert.Contains(events, e => e.Source == "CV001.Motor" && e.Code == "AT_SPEED");
        int speedIndex = line.Sim.IO.Directory.Find("CV001.Speed").Index;
        Assert.True(deltas.Count(d => d.Changes.Any(c => c.Index == speedIndex)) > 1000);
    }

    [Fact]
    public void TheHmiGetsADecimatedDeadbandedConveyorOnlyView()
    {
        using var line = new Line();
        line.StartUp();
        line.Run(40.0);

        List<FrameDelta> deltas = Line.Drain(line.Hmi);

        Assert.False(line.Hmi.IsFaulted);
        // Conflate: everything since the last read is one delta.
        FrameDelta delta = Assert.Single(deltas);
        Assert.All(delta.Changes, c => Assert.StartsWith("CV001.", line.Sim.IO.Directory[c.Index].Name, StringComparison.Ordinal));
        int speedIndex = line.Sim.IO.Directory.Find("CV001.Speed").Index;
        Assert.InRange(delta.Changes.Single(c => c.Index == speedIndex).Value.AsDouble, 1.75, 1.95);

        // Read continuously for two more seconds: at most one delta per 100 ms of sim time.
        int reads = 0;
        for (int i = 0; i < 20; i++)
        {
            line.Sim.RunFor(TimeSpan.FromMilliseconds(100));
            line.Hub.Pump();
            reads += Line.Drain(line.Hmi).Count;
        }

        Assert.InRange(reads, 0, 20);
    }

    [Fact]
    public void APullKeyThroughTheBusStopsTheBeltAndTheStateShowsIt()
    {
        using var line = new Line();
        line.StartUp();
        line.Run(30.0);

        Assert.Equal(CommandOutcome.Accepted, line.Bus.WriteBool("CV001.PullKey1", true));
        line.Run(20.0);

        LiveState state = line.Hub.State;
        Assert.False(state.Get("CV001.SafetyOk").Value.AsBool);
        Assert.False(state.Get("CV001.Contactor").Value.AsBool);
        Assert.True(state.Get("CV001.Stopped").Value.AsBool);
        Assert.Contains(Line.Drain(line.Historian).SelectMany(d => d.Events), e => e.Code == "SAFETY_TRIP");
    }

    [Fact]
    public void TheBusRefusesWhatTheDirectoryForbids()
    {
        using var line = new Line();

        Assert.Equal(CommandOutcome.ReadOnly, line.Bus.WriteBool("CV001.Contactor", true));
        Assert.Equal(CommandOutcome.KindMismatch, line.Bus.WriteDouble("CV001.Start", 1.0));
        Assert.Equal(CommandOutcome.UnknownTag, line.Bus.WriteBool("CV001.Nope", true));
        Assert.Equal(0, line.Sim.IO.PendingWrites);
    }

    [Fact]
    public void FiftyConsumersDoNotChangeTheRun()
    {
        static double[] Run(int subscribers)
        {
            using var line = new Line();
            var subs = new List<Subscription>();
            for (int i = 0; i < subscribers; i++)
            {
                subs.Add(line.Hub.Subscribe(new SubscriptionOptions { Policy = BackpressurePolicy.Lossless, Capacity = 1 << 16 }));
            }

            line.StartUp();
            line.Run(20.0);
            double[] result =
            [
                line.Sim.IO.ReadDouble("CV001.Speed"),
                line.Sim.IO.ReadDouble("CV001.TonnesPerHour"),
                line.Sim.IO.ReadDouble("CV001.Current"),
                line.Sim.Telemetry.Read("CV001.Motor.Current"),
            ];
            subs.ForEach(s => s.Dispose());
            return result;
        }

        Assert.Equal(Run(0), Run(50));
    }
}
