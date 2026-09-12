using Dse.Components.Instruments;
using Dse.Components.Tests.Fakes;
using Dse.Core;
using Dse.Core.Faults;
using Dse.Core.Graph;
using Dse.Core.Time;
using Xunit;

namespace Dse.Components.Tests;

public class InstrumentBaseTests
{
    private static readonly InstrumentSpec Clean = new("m/s", 0.0, 10.0);

    private static SimulationOptions Options(ulong seed = 7UL) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(100),
    };

    private static (ProbeInstrument Probe, OutputPort<double> Truth) Probe(InstrumentSpec spec, double truth)
    {
        var probe = new ProbeInstrument("S", spec);
        probe.Initialize(TestContexts.Init(probe.Id, dt: 0.1));
        var source = new OutputPort<double>("Out", "SP");
        source.ConnectTo(probe.In);
        source.Value = truth;
        return (probe, source);
    }

    private static void Run(ProbeInstrument probe, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            probe.Evaluate(TestContexts.Tick(i, dt: 0.1));
            probe.Latch();
        }
    }

    [Fact]
    public void ACleanInstrumentReadsTheTruth()
    {
        var (probe, _) = Probe(Clean, 3.5);
        Run(probe, 3);
        Assert.Equal(3.5, probe.Value.Value);
        Assert.Equal(InstrumentHealth.Good, probe.Health.Value);
    }

    [Fact]
    public void ReadingsAreClampedToTheRange()
    {
        var (probe, _) = Probe(Clean, 12.0);
        Run(probe, 1);
        Assert.Equal(10.0, probe.Value.Value);
        Assert.Equal(InstrumentHealth.Good, probe.Health.Value);
    }

    [Fact]
    public void CalibrationAppliesGainAndOffset()
    {
        var (probe, _) = Probe(Clean, 2.0);
        probe.ApplyFault(InstrumentFaults.Calibration, new FaultArguments(new("gain", 1.5), new("offset", 0.25)));
        Run(probe, 1);
        Assert.Equal(3.25, probe.Value.Value, 9);

        probe.ClearFault(InstrumentFaults.Calibration);
        Run(probe, 1);
        Assert.Equal(2.0, probe.Value.Value, 9);
    }

    [Fact]
    public void DriftAccumulatesAndClearingResetsIt()
    {
        var (probe, _) = Probe(Clean, 2.0);
        probe.ApplyFault(InstrumentFaults.Drift, new FaultArguments(new FaultArgument("rate", 0.5)));
        Run(probe, 10);   // 1 s at 0.5 /s
        Assert.Equal(2.5, probe.Value.Value, 9);

        probe.ClearFault(InstrumentFaults.Drift);
        Run(probe, 1);
        Assert.Equal(2.0, probe.Value.Value, 9);
    }

    [Fact]
    public void LagIsFirstOrder()
    {
        var (probe, truth) = Probe(Clean with { LagSeconds = 1.0 }, 0.0);
        Run(probe, 1);           // the first reading primes the filter at the truth
        truth.Value = 4.0;
        Run(probe, 10);          // one time constant
        Assert.InRange(probe.Value.Value, 4.0 * 0.62, 4.0 * 0.68);   // 1 − e⁻¹ = 0.632; Euler gives 0.651
        Run(probe, 100);
        Assert.Equal(4.0, probe.Value.Value, 3);
    }

    [Fact]
    public void ALagFaultOverridesTheSpecLag()
    {
        var (probe, truth) = Probe(Clean, 0.0);
        Run(probe, 1);
        probe.ApplyFault(InstrumentFaults.Lag, new FaultArguments(new FaultArgument("seconds", 5.0)));
        truth.Value = 4.0;
        Run(probe, 10);
        Assert.True(probe.Value.Value < 1.0);   // 4 × (1 − 0.98¹⁰) ≈ 0.73
    }

    [Fact]
    public void FreezeHoldsTheLastOutput()
    {
        var (probe, truth) = Probe(Clean, 2.0);
        Run(probe, 1);
        probe.ApplyFault(InstrumentFaults.Freeze, FaultArguments.None);
        truth.Value = 6.0;
        Run(probe, 5);
        Assert.Equal(2.0, probe.Value.Value);
        Assert.Equal(InstrumentHealth.Good, probe.Health.Value);

        probe.ClearFault(InstrumentFaults.Freeze);
        Run(probe, 1);
        Assert.Equal(6.0, probe.Value.Value);
    }

    [Fact]
    public void FailHighAndFailLowPinTheRangeAndReportBad()
    {
        var (probe, _) = Probe(Clean, 2.0);
        probe.ApplyFault(InstrumentFaults.FailHigh, FaultArguments.None);
        Run(probe, 1);
        Assert.Equal((10.0, InstrumentHealth.Bad), (probe.Value.Value, probe.Health.Value));

        probe.ClearFault(InstrumentFaults.FailHigh);
        probe.ApplyFault(InstrumentFaults.FailLow, FaultArguments.None);
        Run(probe, 1);
        Assert.Equal((0.0, InstrumentHealth.Bad), (probe.Value.Value, probe.Health.Value));

        probe.ClearFault(InstrumentFaults.FailLow);
        Run(probe, 1);
        Assert.Equal((2.0, InstrumentHealth.Good), (probe.Value.Value, probe.Health.Value));
    }

    [Fact]
    public void NoiseIsDeterministicPerSeedAndAbsentWhenSigmaIsZero()
    {
        static List<double> Sample(ulong seed, double sigma)
        {
            var probe = new ProbeInstrument("S", Clean with { NoiseSigma = sigma });
            var source = new Setpoint("SP", 5.0);
            source.Out.ConnectTo(probe.In);
            Simulation sim = new SimulationBuilder(Options(seed)).Add(probe).Add(source).Build();
            var samples = new List<double>();
            for (int i = 0; i < 50; i++)
            {
                sim.Tick();
                samples.Add(probe.Value.Value);
            }

            return samples;
        }

        Assert.All(Sample(1UL, 0.0), v => Assert.Equal(5.0, v));
        List<double> a = Sample(1UL, 0.1);
        Assert.Equal(a, Sample(1UL, 0.1));
        Assert.NotEqual(a, Sample(2UL, 0.1));
        Assert.Contains(a, v => Math.Abs(v - 5.0) > 0.01);
        Assert.InRange(a.Average(), 4.9, 5.1);
    }

    [Fact]
    public void ANoiseFaultAddsNoiseToACleanInstrument()
    {
        var probe = new ProbeInstrument("S", Clean);
        var source = new Setpoint("SP", 5.0);
        source.Out.ConnectTo(probe.In);
        Simulation sim = new SimulationBuilder(Options()).Add(probe).Add(source).Build();
        sim.InjectFaultAt(TimeSpan.Zero, "S", InstrumentFaults.Noise, new FaultArguments(new FaultArgument("sigma", 0.5)));

        var samples = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            sim.Tick();
            samples.Add(probe.Value.Value);
        }

        Assert.Contains(samples, v => Math.Abs(v - 5.0) > 0.05);
    }

    [Fact]
    public void TruthTelemetryIsUnaffectedByFaults()
    {
        var probe = new ProbeInstrument("S", Clean);
        var source = new Setpoint("SP", 5.0);
        source.Out.ConnectTo(probe.In);
        Simulation sim = new SimulationBuilder(Options()).Add(probe).Add(source).Build();
        sim.InjectFaultAt(TimeSpan.Zero, "S", InstrumentFaults.FailLow);
        sim.Tick();

        Assert.Equal(0.0, probe.Value.Value);
        Assert.Equal(5.0, sim.Telemetry.Read("S.Truth"));
    }

    [Fact]
    public void EveryInstrumentPublishesTheSharedVocabulary()
    {
        var probe = new ProbeInstrument("S", Clean);
        Assert.Equal(
            ["calibration", "noise", "drift", "lag", "freeze", "fail-high", "fail-low"],
            probe.SupportedFaults.Select(f => f.Id));
    }
}
