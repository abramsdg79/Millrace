using Dse.Components.Flow;
using Dse.Components.Mechanical;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class DrivetrainTests
{
    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static SimulationOptions Options(double stepSeconds = 0.01) => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromSeconds(stepSeconds),
    };

    [Fact]
    public void GearboxScalesSpeedDownAndTorqueUp()
    {
        var gearbox = new Gearbox("GB", ratio: 20.0, efficiency: 0.95);
        var speed = new OutputPort<double>("Out", "W");
        var torque = new OutputPort<double>("Out", "T");
        speed.ConnectTo(gearbox.InputSpeed);
        torque.ConnectTo(gearbox.OutputTorqueDemand);
        speed.Value = 150.0;
        torque.Value = 190.0;

        gearbox.Evaluate(TestContexts.Tick(0));
        Assert.Equal(7.5, gearbox.OutputSpeed.Value, 9);
        Assert.Equal(0.0, gearbox.InputTorqueDemand.Value);   // latched: last tick's demand
        gearbox.Latch();
        gearbox.Evaluate(TestContexts.Tick(1));
        Assert.Equal(10.0, gearbox.InputTorqueDemand.Value, 9);
        Assert.True(gearbox.OutputTorqueDemand.IsLatched);
    }

    [Fact]
    public void DrivePulleyConvertsSpeedAndForceAndCanSlip()
    {
        var pulley = new DrivePulley("DP", diameterM: 0.5, bearingDragN: 10.0);
        var shaft = new OutputPort<double>("Out", "W");
        var force = new OutputPort<double>("Out", "F");
        shaft.ConnectTo(pulley.ShaftSpeed);
        force.ConnectTo(pulley.BeltForce);
        shaft.Value = 8.0;
        force.Value = 190.0;

        pulley.Evaluate(TestContexts.Tick(0));
        Assert.Equal(2.0, pulley.BeltSpeed.Value, 9);
        Assert.Equal(50.0, pulley.TorqueDemand.Value, 9);   // (190 + 10) × 0.25

        pulley.ApplyFault(DrivePulley.BeltSlip, new FaultArguments(new FaultArgument("fraction", 0.25)));
        pulley.ApplyFault(DrivePulley.BearingFriction, new FaultArguments(new FaultArgument("drag", 40.0)));
        pulley.Evaluate(TestContexts.Tick(1));
        Assert.Equal(1.5, pulley.BeltSpeed.Value, 9);
        Assert.Equal(60.0, pulley.TorqueDemand.Value, 9);
    }

    [Fact]
    public void TailPulleyDragAddsToBeltForce()
    {
        var tail = new TailPulley("TP", bearingDragN: 30.0);
        var friction = new BeltFriction("BF", emptyBeltMassKg: 100.0, frictionCoefficient: 0.05);
        var load = new OutputPort<double>("Out", "L");
        load.ConnectTo(friction.Load);
        tail.Drag.ConnectTo(friction.Drag);
        load.Value = 300.0;

        tail.Evaluate(TestContexts.Tick(0));
        friction.Evaluate(TestContexts.Tick(0));
        Assert.Equal((0.05 * 9.80665 * 400.0) + 30.0, friction.Force.Value, 9);

        tail.ApplyFault(TailPulley.BearingFriction, new FaultArguments(new FaultArgument("drag", 20.0)));
        tail.Evaluate(TestContexts.Tick(1));
        friction.Evaluate(TestContexts.Tick(1));
        Assert.Equal((0.05 * 9.80665 * 400.0) + 50.0, friction.Force.Value, 9);
    }

    [Fact]
    public void BeltGeometryGivesAMaximumLinearDensity()
    {
        // b = 0.9 × 0.8 − 0.05 = 0.67 m; area = 0.67² × tan 20° / 4 = 0.04085 m²; × 2000 kg/m³.
        Assert.Equal(81.7, BeltGeometry.MaxLinearDensity(0.8, 20.0, 2000.0), 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => BeltGeometry.MaxLinearDensity(0.05, 20.0, 2000.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BeltGeometry.MaxLinearDensity(0.8, 0.0, 2000.0));
    }

    [Fact]
    public void TheReflectedTorqueChainValidatesAndLoadsTheMotor()
    {
        // Motor → gearbox → drive pulley → belt speed; belt load → friction → force → pulley torque → gearbox → motor.
        var motor = new Motor("M", new MotorRating(750.0, 150.0, 2.0));
        var gearbox = new Gearbox("GB", 20.0);
        var pulley = new DrivePulley("DP", 0.5);
        var tail = new TailPulley("TP", 20.0);
        var friction = new BeltFriction("BF", 250.0, 0.04);
        var belt = new BulkBelt("Belt", length: 10.0, cellSize: 0.5, maxSpeed: 2.0, maxLinearDensity: 80.0);
        var feed = new BulkSource("Feed", Ore, 40.0);
        var pile = new BulkSink("Pile");
        var run = new Switch("Run", true);

        run.Out.ConnectTo(motor.Energised);
        motor.Speed.ConnectTo(gearbox.InputSpeed);
        gearbox.OutputSpeed.ConnectTo(pulley.ShaftSpeed);
        pulley.BeltSpeed.ConnectTo(belt.Speed);
        belt.Load.ConnectTo(friction.Load);
        tail.Drag.ConnectTo(friction.Drag);
        friction.Force.ConnectTo(pulley.BeltForce);
        pulley.TorqueDemand.ConnectTo(gearbox.OutputTorqueDemand);
        gearbox.InputTorqueDemand.ConnectTo(motor.TorqueDemand);
        feed.Out.ConnectTo(belt.In);
        belt.Out.ConnectTo(pile.In);

        Simulation sim = new SimulationBuilder(Options())
            .Add(pile).Add(belt).Add(feed).Add(run).Add(motor).Add(gearbox).Add(pulley).Add(tail).Add(friction)
            .Build();

        sim.RunFor(TimeSpan.FromSeconds(5));
        double emptyCurrent = motor.Current.Value;
        Assert.InRange(pulley.BeltSpeed.Value, 1.8, 1.9);   // 150 / 20 × 0.25 less droop

        sim.RunFor(TimeSpan.FromSeconds(30));               // belt fills to ~215 kg steady load

        // Empty: 118 N → 1.55 N·m → 1.03 A. Loaded: 202 N → 2.66 N·m → 1.35 A.
        Assert.True(belt.Load.Value > 180.0);
        Assert.True(motor.Current.Value > emptyCurrent + 0.2);
        Assert.True(motor.TorqueDemand.Value > 2.0);
        Assert.Equal(0.0, sim.MassBalance.Drift, 6);
    }
}
