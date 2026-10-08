using Millrace.Components.Mechanical;
using Millrace.Components.Tests.Fakes;
using Millrace.Core;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Components.Tests;

public class MotorStarterTests
{
    private static SimulationOptions Options() => new()
    {
        Seed = 1UL,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(10),
    };

    private sealed record Rig(Simulation Sim, MotorStarter Starter, Switch Command, Switch Safety, Setpoint Thermal, Switch Reset);

    /// <summary>The starter and its drivers; the permit is wired only when a test passes one, so it reads its default otherwise.</summary>
    private static Rig Build(Switch? permit = null, double tripLevel = 1.1)
    {
        var starter = new MotorStarter("K1", tripLevel);
        var command = new Switch("Cmd");
        var safety = new Switch("Safe", true);
        var thermal = new Setpoint("Theta", 0.5);
        var reset = new Switch("Reset");
        command.Out.ConnectTo(starter.Command);
        safety.Out.ConnectTo(starter.SafetyOk);
        thermal.Out.ConnectTo(starter.ThermalState);
        reset.Out.ConnectTo(starter.Reset);
        SimulationBuilder builder = new SimulationBuilder(Options()).Add(starter).Add(command).Add(safety).Add(thermal).Add(reset);
        if (permit is not null)
        {
            permit.Out.ConnectTo(starter.Permit);
            builder.Add(permit);
        }

        return new Rig(builder.Build(), starter, command, safety, thermal, reset);
    }

    [Fact]
    public void ClosesOnCommandAndOpensWhenSafetyDrops()
    {
        Rig rig = Build();
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);

        rig.Command.Value = true;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);

        rig.Safety.Value = false;
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);
        Assert.False(rig.Starter.Tripped.Value);

        rig.Safety.Value = true;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);   // command still held: no trip, so it closes again
        Assert.Equal(["CONTACTOR_CLOSED", "CONTACTOR_OPENED", "CONTACTOR_CLOSED"], rig.Sim.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void TripsOnTheThermalStateAndResetsOnlyAfterCooling()
    {
        Rig rig = Build();
        rig.Command.Value = true;
        rig.Sim.Tick();

        rig.Thermal.Value = 1.1;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Tripped.Value);
        Assert.False(rig.Starter.Contactor.Value);

        rig.Reset.Value = true;                    // still hot
        rig.Sim.Tick();
        Assert.True(rig.Starter.Tripped.Value);
        rig.Reset.Value = false;

        rig.Thermal.Value = 0.8;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Tripped.Value);    // cooled, but no reset edge yet

        rig.Reset.Value = true;
        rig.Sim.Tick();
        Assert.False(rig.Starter.Tripped.Value);
        Assert.True(rig.Starter.Contactor.Value);
        Assert.Equal(
            ["CONTACTOR_CLOSED", "OVERLOAD_TRIP", "CONTACTOR_OPENED", "OVERLOAD_RESET", "CONTACTOR_CLOSED"],
            rig.Sim.Events.Records.Select(r => r.Code));
        Assert.Contains("1.1", rig.Sim.Events.Records[1].Message);
    }

    [Fact]
    public void ContactorFaults()
    {
        Rig rig = Build();
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorWelded);
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);   // no command, yet closed

        rig.Sim.ClearFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorWelded);
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorOpen);
        rig.Command.Value = true;
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);
    }

    [Fact]
    public void AFalsePermitHoldsTheContactorOpenWhateverTheCommand()
    {
        var permit = new Switch("Permit");
        Rig rig = Build(permit);
        rig.Command.Value = true;

        rig.Sim.RunFor(TimeSpan.FromSeconds(1));
        Assert.False(rig.Starter.Contactor.Value);
        Assert.Empty(rig.Sim.Events.Records);           // a refused command logs nothing

        permit.Value = true;
        rig.Sim.Tick();
        Assert.True(rig.Starter.Contactor.Value);

        permit.Value = false;                            // an interlock contact in series: opening it drops a running starter
        rig.Sim.Tick();
        Assert.False(rig.Starter.Contactor.Value);
        Assert.Equal(["CONTACTOR_CLOSED", "CONTACTOR_OPENED"], rig.Sim.Events.Records.Select(r => r.Code));
    }

    [Fact]
    public void AWeldedContactorStaysClosedWithoutAPermit()
    {
        var permit = new Switch("Permit");
        Rig rig = Build(permit);
        rig.Sim.InjectFaultIn(TimeSpan.Zero, "K1", MotorStarter.ContactorWelded);

        rig.Sim.RunFor(TimeSpan.FromSeconds(1));

        Assert.False(rig.Starter.Permit.Value);
        Assert.True(rig.Starter.Contactor.Value);
    }

    [Fact]
    public void RejectsAResetLevelAboveTheTripLevel()
    {
        Assert.Throws<ArgumentException>(() => new MotorStarter("K1", tripLevel: 1.0, resetLevel: 1.0));
    }

    [Theory]
    [InlineData(1.23456, "Thermal state 1.235 reached the trip level 1.1.")]
    [InlineData(1.1000357303409216, "Thermal state 1.100 reached the trip level 1.1.")]
    public void TheOverloadTripMessagePrintsTheThermalStateToThreeDecimals(double thermal, string message)
    {
        Rig rig = Build();
        rig.Command.Value = true;
        rig.Sim.Tick();

        rig.Thermal.Value = thermal;
        rig.Sim.Tick();

        Assert.Equal(message, Assert.Single(rig.Sim.Events.Records, r => r.Code == "OVERLOAD_TRIP").Message);
    }

    [Theory]
    [InlineData(1.125, 1.12504, "Thermal state 1.1250 reached the trip level 1.125.")]
    [InlineData(1.1234, 1.12344, "Thermal state 1.12344 reached the trip level 1.1234.")]
    [InlineData(1.1234, 1.1234, "Thermal state 1.12340 reached the trip level 1.1234.")]
    [InlineData(1.1234561, 1.1234562, "Thermal state 1.123457 reached the trip level 1.1234561.")]
    [InlineData(1.1234561, 1.1234561, "Thermal state 1.123457 reached the trip level 1.1234561.")]
    public void TheOverloadTripMessageNeverReadsBelowTheTripLevel(double tripLevel, double thermal, string message)
    {
        Rig rig = Build(tripLevel: tripLevel);
        rig.Command.Value = true;
        rig.Sim.Tick();

        rig.Thermal.Value = thermal;
        rig.Sim.Tick();

        Assert.Equal(message, Assert.Single(rig.Sim.Events.Records, r => r.Code == "OVERLOAD_TRIP").Message);
    }
}
