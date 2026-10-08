using Millrace.Components.Mechanical;
using Millrace.Components.Tests.Fakes;
using Millrace.Core.Faults;
using Millrace.Core.Graph;
using Millrace.Core.Logging;
using Xunit;

namespace Millrace.Components.Tests;

public class MotorTests
{
    // 750 W at 150 rad/s: rated torque 5 N·m, rated current 2 A.
    private static readonly MotorRating Rating = new(750.0, 150.0, 2.0);

    private sealed class Rig
    {
        public Rig(double dt = 0.1, MotorRating? rating = null)
        {
            Dt = dt;
            Motor = new Motor("M", rating ?? Rating);
            Motor.Initialize(TestContexts.Init(Motor.Id, dt: dt));
            Energised = new OutputPort<bool>("Out", "Run");
            Energised.ConnectTo(Motor.Energised);
            Demand = new OutputPort<double>("Out", "Load");
            Demand.ConnectTo(Motor.TorqueDemand);
        }

        public double Dt { get; }

        public Motor Motor { get; }

        public OutputPort<bool> Energised { get; }

        public OutputPort<double> Demand { get; }

        public EventLog Log { get; } = new();

        public int Tick { get; private set; }

        public void Run(double seconds)
        {
            int ticks = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                Motor.Evaluate(TestContexts.Tick(Tick++, Dt, Log));
                Motor.Latch();
            }
        }

        public IEnumerable<string> Codes => Log.Records.Select(r => r.Code);
    }

    [Fact]
    public void RatingDerivesTorque()
    {
        Assert.Equal(5.0, Rating.RatedTorque, 9);
    }

    [Fact]
    public void AnUnloadedMotorReachesRatedSpeedAndDrawsNoLoadCurrent()
    {
        var rig = new Rig();
        rig.Energised.Value = true;

        rig.Run(10.0);

        Assert.Equal(150.0, rig.Motor.Speed.Value, 1);
        Assert.Equal(0.6, rig.Motor.Current.Value, 2);
        Assert.True(rig.Motor.AtSpeed.Value);
        Assert.Equal(["ENERGISED", "AT_SPEED"], rig.Codes);
    }

    [Fact]
    public void StartingCurrentIsLockedRotorAndDecaysWithSpeed()
    {
        var rig = new Rig(dt: 0.01);
        rig.Energised.Value = true;

        rig.Run(0.01);
        Assert.InRange(rig.Motor.Current.Value, 11.5, 12.0);   // 6 × 2 A at standstill
        rig.Run(3.0);
        Assert.True(rig.Motor.Current.Value < 1.0);
    }

    [Fact]
    public void RatedTorqueDrawsRatedCurrentAndSettlesTheThermalStateAtOne()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Demand.Value = 5.0;

        rig.Run(600.0);   // ten thermal time constants

        Assert.Equal(2.0, rig.Motor.Current.Value, 2);
        Assert.Equal(145.5, rig.Motor.Speed.Value, 1);   // 3 % droop
        Assert.Equal(1.0, rig.Motor.ThermalState.Value, 2);
        Assert.Equal(5.0, rig.Motor.Torque.Value, 9);
    }

    [Fact]
    public void TwiceRatedTorqueCrossesTheTripLevelOnACurve()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(5.0);
        rig.Demand.Value = 10.0;   // I = 2 × (0.3 + 0.7 × 2) = 3.4 A; (I/Ir)² = 2.89

        double crossedAt = double.NaN;
        for (double t = 0.0; t < 120.0; t += rig.Dt)
        {
            rig.Run(rig.Dt);
            if (rig.Motor.ThermalState.Value >= 1.1)
            {
                crossedAt = t;
                break;
            }
        }

        // The direct-on-line start (12 A decaying over ~3 s) leaves θ ≈ 0.3; from there
        // θ → 2.89 with τ = 60 s crosses 1.1 near 60 × ln((2.89 − 0.3) / (2.89 − 1.1)) ≈ 23 s.
        Assert.InRange(crossedAt, 18.0, 30.0);
        Assert.Equal(3.4, rig.Motor.Current.Value, 2);
    }

    [Fact]
    public void BeyondBreakdownTorqueTheMotorStallsAtLockedRotorCurrent()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(5.0);
        rig.Demand.Value = 13.0;   // 2.6 × rated > 2.5 breakdown

        rig.Run(10.0);

        Assert.True(rig.Motor.Speed.Value < 1.0);
        Assert.Equal(12.0, rig.Motor.Current.Value, 1);
        Assert.Equal(0.0, rig.Motor.Torque.Value);
        Assert.Contains("STALLED", rig.Codes);
        Assert.False(rig.Motor.AtSpeed.Value);
    }

    [Fact]
    public void AStallThatRecoversLogsAtSpeedAgain()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);   // AT_SPEED once

        rig.Demand.Value = 13.0;   // above breakdown
        rig.Run(10.0);   // STALLED; AtSpeed clears; speed decays toward 0

        Assert.True(rig.Motor.Speed.Value < 1.0);
        Assert.False(rig.Motor.AtSpeed.Value);

        rig.Demand.Value = 0.0;   // demand change is seen one tick late (latched)
        rig.Run(10.0);   // recovers to speed; AT_SPEED logs again

        Assert.Equal(["ENERGISED", "AT_SPEED", "STALLED", "AT_SPEED"], rig.Codes);
        Assert.True(rig.Motor.AtSpeed.Value);
    }

    [Fact]
    public void DeEnergisingCoastsToAStop()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        rig.Energised.Value = false;

        rig.Run(3.0);
        Assert.InRange(rig.Motor.Speed.Value, 50.0, 60.0);   // one coast time constant: 150 × e⁻¹ ≈ 55
        Assert.Equal(0.0, rig.Motor.Current.Value);
        rig.Run(30.0);
        Assert.True(rig.Motor.Speed.Value < 0.75);
        Assert.Equal(["ENERGISED", "AT_SPEED", "DE_ENERGISED", "STOPPED"], rig.Codes);
    }

    [Fact]
    public void BearingFrictionRaisesCurrentWithNoExternalLoad()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        double before = rig.Motor.Current.Value;

        rig.Motor.ApplyFault(Motor.BearingFriction, new FaultArguments(new FaultArgument("torque", 2.5)));
        rig.Run(10.0);

        Assert.Equal(1.3, rig.Motor.Current.Value, 2);   // 2 × (0.3 + 0.7 × 0.5)
        Assert.True(rig.Motor.Current.Value > before);

        rig.Motor.ClearFault(Motor.BearingFriction);
        rig.Run(10.0);
        Assert.Equal(before, rig.Motor.Current.Value, 2);
    }

    [Fact]
    public void AThermalBiasIsAOneShotStepInTheThermalState()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        double before = rig.Motor.ThermalState.Value;

        rig.Motor.ApplyFault(Motor.ThermalBias, new FaultArguments(new FaultArgument("amount", 0.7)));
        rig.Run(rig.Dt);

        Assert.InRange(rig.Motor.ThermalState.Value, before + 0.65, before + 0.71);
    }

    [Fact]
    public void TorqueDemandIsLatched()
    {
        var motor = new Motor("M", Rating);
        Assert.True(motor.TorqueDemand.IsLatched);
        Assert.False(motor.Energised.IsLatched);
        Assert.Equal(["bearing-friction", "thermal-bias"], motor.SupportedFaults.Select(f => f.Id));
    }

    [Fact]
    public void TheAtSpeedAndStalledMessagesPrintSpeedToOneDecimalAndTorqueToWholeNewtonMetres()
    {
        var rig = new Rig();
        rig.Energised.Value = true;
        rig.Run(10.0);
        rig.Demand.Value = 13.0;   // 2.6 × rated > 2.5 × 5 = 12.5 N·m breakdown
        rig.Run(10.0);

        // AT_SPEED at 95 % of 150 rad/s (measured 142.93480695413064). The breakdown
        // torque 12.5 is an exact binary midpoint, which F0 rounds to even: 12.
        Assert.Equal(
            ["Contactor closed.", "Reached 142.9 rad/s.", "Torque demand 13 N·m exceeds breakdown torque 12 N·m."],
            rig.Log.Records.Select(r => r.Message));
    }

    [Theory]
    [InlineData(2.44, 12.3, "Torque demand 13 N·m exceeds breakdown torque 12 N·m.")]
    [InlineData(2.52, 12.7, "Torque demand 14 N·m exceeds breakdown torque 13 N·m.")]
    [InlineData(2.5, 14.2, "Torque demand 15 N·m exceeds breakdown torque 12 N·m.")]
    [InlineData(2.5, 1e30, "Torque demand 1000000000000000019884624838656 N·m exceeds breakdown torque 12 N·m.")]
    public void AStallJustAboveTheBreakdownTorqueNeverReadsAtOrBelowIt(double breakdownMultiple, double demand, string message)
    {
        var rig = new Rig(rating: Rating with { BreakdownTorqueMultiple = breakdownMultiple });
        rig.Energised.Value = true;
        rig.Run(10.0);
        rig.Demand.Value = demand;
        rig.Run(1.0);

        Assert.Equal(message, Assert.Single(rig.Log.Records, r => r.Code == "STALLED").Message);
    }
}
