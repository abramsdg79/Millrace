using Millrace.Components.Conveyors;
using Millrace.Components.Flow;
using Millrace.Components.Mechanical;
using Millrace.Components.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
using Millrace.Core.Logging;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Components.Tests;

public class ConveyorTests
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

    private static SimulationOptions Options(ulong seed = 1UL) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private sealed record Plant(
        Simulation Sim,
        BulkSource Feed,
        Conveyor Conveyor,
        TransferChute Chute,
        BulkSink Pile,
        Switch Start,
        Switch SafetyReset,
        Switch PullKey1)
    {
        public void Run(double seconds) => Sim.RunFor(TimeSpan.FromSeconds(seconds));

        public double Seconds => Sim.Clock.Elapsed.TotalSeconds;

        public IEnumerable<SimEventRecord> Events(string source) => Sim.Events.Records.Where(r => r.Source == source);

        /// <summary>Pulses the safety reset and holds the start command: the operator's start sequence.</summary>
        public void StartUp()
        {
            SafetyReset.Value = true;
            Run(0.05);
            SafetyReset.Value = false;
            Start.Value = true;
        }
    }

    private static Plant Build(ulong seed = 1UL, double feedRate = 20.0)
    {
        var feed = new BulkSource("Feed", Ore, feedRate, new MaterialProperties(2000.0, 0.03, 15.0));
        var conveyor = new Conveyor("CV001", Cv001);
        var chute = new TransferChute("Chute", capacityKg: 200.0);
        var pile = new BulkSink("Pile");
        var start = new Switch("Start");
        var safetyReset = new Switch("SafetyReset");
        var pullKey1 = new Switch("Key1");

        feed.Out.ConnectTo(conveyor.Inlet("In"));
        conveyor.Outlet("Out").ConnectTo(chute.In);
        chute.Out.ConnectTo(pile.In);
        start.Out.ConnectTo(conveyor.Input<bool>("Start"));
        safetyReset.Out.ConnectTo(conveyor.Input<bool>("SafetyReset"));
        pullKey1.Out.ConnectTo(conveyor.Input<bool>("PullKey1"));

        Simulation sim = new SimulationBuilder(Options(seed))
            .Add(pile).Add(chute).Add(conveyor).Add(feed).Add(start).Add(safetyReset).Add(pullKey1)
            .Build();
        return new Plant(sim, feed, conveyor, chute, pile, start, safetyReset, pullKey1);
    }

    [Fact]
    public void TheCompositeFlattensToQualifiedLeavesWithNoAlgebraicLoop()
    {
        Plant plant = Build();

        string[] ids = plant.Sim.Components.Select(c => c.Id).ToArray();
        Assert.Contains("CV001.Motor", ids);
        Assert.Contains("CV001.Belt", ids);
        Assert.Contains("CV001.PullKey2", ids);
        Assert.Contains("CV001.Starter", ids);
        // 13 fixed children + 2 pull-keys + a UnitDelay: Starter.Contactor and Motor.ThermalState
        // close a genuine algebraic loop (see Conveyor's constructor comment), broken by delaying
        // the contactor's reflection into the motor by one tick.
        Assert.Equal(16, ids.Count(id => id.StartsWith("CV001.", StringComparison.Ordinal)));
        Assert.Equal(["bearing-friction", "thermal-bias"], plant.Sim.FaultsOf("CV001.Motor").Select(f => f.Id));
    }

    [Fact]
    public void StartsReachesSpeedAndConveysAtTheFeedRate()
    {
        Plant plant = Build();
        plant.StartUp();

        plant.Run(40.0);

        Assert.Contains(plant.Events("CV001.Motor"), r => r.Code == "AT_SPEED");
        Assert.True(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.False(plant.Conveyor.Output<bool>("Tripped").Value);
        Assert.InRange(plant.Conveyor.Output<double>("Speed").Value, 1.75, 1.95);        // 150 / 20 × 0.25 m less droop
        Assert.InRange(plant.Conveyor.Output<double>("TonnesPerHour").Value, 68.0, 76.0); // 20 kg/s = 72 t/h
        Assert.InRange(plant.Conveyor.Output<double>("Current").Value, 1.2, 1.6);
        Assert.InRange(plant.Sim.Telemetry.Read("CV001.Belt.Load"), 95.0, 120.0);
        Assert.True(plant.Pile.MassDestroyed > 500.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 6);
    }

    [Fact]
    public void ABlockedChuteLoadsTheBeltRaisesCurrentAndTripsTheOverload()
    {
        Plant plant = Build();
        plant.StartUp();
        plant.Run(30.0);
        double steadyLoad = plant.Sim.Telemetry.Read("CV001.Belt.Load");
        double steadyCurrent = plant.Sim.Telemetry.Read("CV001.Motor.Current");

        plant.Sim.InjectFaultIn(TimeSpan.Zero, "Chute", TransferChute.Blockage);

        double loadAtTrip = double.NaN;
        double currentAtTrip = double.NaN;
        double tripTime = double.NaN;
        while (plant.Seconds < 250.0)
        {
            plant.Sim.Tick();
            if (plant.Conveyor.Output<bool>("Tripped").Value)
            {
                tripTime = plant.Seconds;
                loadAtTrip = plant.Sim.Telemetry.Read("CV001.Belt.Load");
                currentAtTrip = plant.Sim.Telemetry.Read("CV001.Motor.Current");
                break;
            }
        }

        // The chain: chute full → belt loads → torque → current → thermal state → trip.
        Assert.False(double.IsNaN(tripTime));
        Assert.True(plant.Chute.Full.Value);
        Assert.True(loadAtTrip > 4.0 * steadyLoad);
        Assert.True(loadAtTrip > 500.0);
        Assert.True(currentAtTrip > 2.0);                       // above rated
        Assert.True(currentAtTrip > steadyCurrent + 0.5);
        Assert.Contains(plant.Events("CV001.Starter"), r => r.Code == "OVERLOAD_TRIP");
        long atSpeedTick = plant.Events("CV001.Motor").First(r => r.Code == "AT_SPEED").Tick;
        long tripTick = plant.Events("CV001.Starter").First(r => r.Code == "OVERLOAD_TRIP").Tick;
        Assert.True(atSpeedTick < tripTick);

        // Consequences: contactor open, motor coasts, belt stops, scale reads zero.
        plant.Run(20.0);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.True(plant.Conveyor.Output<bool>("Stopped").Value);
        Assert.True(plant.Conveyor.Output<double>("Speed").Value < 0.05);
        Assert.True(plant.Conveyor.Output<double>("TonnesPerHour").Value < 1.0);
        Assert.Contains(plant.Events("CV001.ZeroSpeed"), r => r.Code == "ZERO_SPEED");
        Assert.Contains(plant.Events("CV001.Motor"), r => r.Code == "DE_ENERGISED");
        // Frozen where it stopped: the feed can only add what room was left before zero speed.
        Assert.InRange(plant.Sim.Telemetry.Read("CV001.Belt.Load"), loadAtTrip - 1e-6, 820.0);
        Assert.Equal(0.0, plant.Sim.MassBalance.Drift, 6);
    }

    [Fact]
    public void APullKeyStopsTheBeltWithNoControllerAndTheLineRestartsAfterReset()
    {
        Plant plant = Build();
        plant.StartUp();
        plant.Run(30.0);

        plant.PullKey1.Value = true;
        plant.Run(15.0);

        Assert.False(plant.Conveyor.Output<bool>("SafetyOk").Value);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.False(plant.Conveyor.Output<bool>("Tripped").Value);
        Assert.True(plant.Conveyor.Output<double>("Speed").Value < 0.05);
        string[] codes = plant.Sim.Events.Records
            .Where(r => r.Tick >= 3000)
            .Select(r => $"{r.Source} {r.Code}")
            .Take(4)
            .ToArray();
        Assert.Equal(
            ["CV001.PullKey1 PULLKEY_PULLED", "CV001.Safety SAFETY_TRIP", "CV001.Starter CONTACTOR_OPENED", "CV001.Motor DE_ENERGISED"],
            codes);

        plant.PullKey1.Value = false;
        plant.Run(1.0);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);   // latched until reset
        plant.SafetyReset.Value = true;
        plant.Run(0.05);
        plant.SafetyReset.Value = false;
        plant.Run(10.0);
        Assert.True(plant.Conveyor.Output<bool>("Contactor").Value);
        Assert.Equal(2, plant.Events("CV001.Motor").Count(r => r.Code == "AT_SPEED"));
    }

    [Fact]
    public void AnInjectedThermalBiasTripsTheSameRelayTheSameWay()
    {
        Plant plant = Build();
        plant.StartUp();
        // Measured: the motor's thermal state at t=40 s (60 s time constant, only ~40 s of
        // run time) is ≈0.383, short of the "θ→0.5" asymptote the plan's arithmetic assumed.
        // 0.7 (the brief's value) lands at ≈1.08, just under the 1.1 trip level, and never
        // trips; 0.8 clears it with margin.
        plant.Sim.InjectFaultAt(TimeSpan.FromSeconds(40), "CV001.Motor", Motor.ThermalBias, new FaultArguments(new FaultArgument("amount", 0.8)));

        plant.Run(41.0);

        SimEventRecord trip = Assert.Single(plant.Events("CV001.Starter"), r => r.Code == "OVERLOAD_TRIP");
        Assert.Equal(4000L, trip.Tick);
        Assert.Contains(plant.Sim.Events.Records, r => r.Code == "FAULT" && r.Source == "CV001.Motor" && r.Tick == 4000L);
        Assert.False(plant.Conveyor.Output<bool>("Contactor").Value);
    }

    [Fact]
    public void TwoRunsOfTheBlockedChuteScenarioAreByteIdentical()
    {
        static (string Log, double Load, double Current) Scenario()
        {
            Plant plant = Build();
            plant.StartUp();
            plant.Sim.InjectFaultAt(TimeSpan.FromSeconds(30), "Chute", TransferChute.Blockage);
            plant.Run(200.0);
            return (plant.Sim.Events.ToText(), plant.Sim.Telemetry.Read("CV001.Belt.Load"), plant.Conveyor.Output<double>("Current").Value);
        }

        var first = Scenario();
        var second = Scenario();

        Assert.Equal(first.Log, second.Log);
        Assert.Equal(first.Load, second.Load);
        Assert.Equal(first.Current, second.Current);
        Assert.Contains("OVERLOAD_TRIP", first.Log);
    }

    [Fact]
    public void ADifferentSeedChangesSensorNoiseButNotTheEventLog()
    {
        static (string Log, double Current) Scenario(ulong seed)
        {
            Plant plant = Build(seed);
            plant.StartUp();
            plant.Run(30.0);
            return (plant.Sim.Events.ToText(), plant.Conveyor.Output<double>("Current").Value);
        }

        var a = Scenario(1UL);
        var b = Scenario(2UL);

        Assert.Equal(a.Log, b.Log);
        Assert.NotEqual(a.Current, b.Current);
    }
}
