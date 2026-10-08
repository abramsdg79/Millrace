using Millrace.Core;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Randomness;
using Millrace.Core.Tests.Fakes;
using Millrace.Core.Tests.Fakes.Flow;
using Millrace.Core.Time;
using Xunit;

namespace Millrace.Core.Tests;

/// <summary>
/// Spec section 16: mass conservation over randomly generated plants. Each
/// seed builds a different chain and drives it with random speeds and feed
/// rates; the per-tick audit inside the simulation is the assertion.
/// </summary>
public class ConservationPropertyTests
{
    private const int Seeds = 20;
    private const int Ticks = 300;

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);
    private static readonly MaterialType Wheel = new("Wheel", PayloadKind.Discrete);

    private static SimulationOptions Options(ulong seed) => new()
    {
        Seed = seed,
        StartTime = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero),
        TimeStep = TimeSpan.FromMilliseconds(500),
    };

    [Fact]
    public void RandomBulkChainsConserveMass()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var random = new DeterministicRandom(seed);
            var builder = new SimulationBuilder(Options(seed));
            var feeder = new BulkFeeder("Feeder", Ore, 1.0);
            var sink = new BulkSink("Sink");
            var speeds = new List<Setpoint>();
            builder.Add(feeder).Add(sink);

            FlowOutlet upstream = feeder.Out;
            int stages = 1 + (int)(random.NextDouble() * 4);
            for (int i = 0; i < stages; i++)
            {
                if (random.NextDouble() < 0.6)
                {
                    int cells = 1 + (int)(random.NextDouble() * 8);
                    double density = 1.0 + (random.NextDouble() * 9.0);
                    var belt = new BulkBelt($"Belt{i}", cells * 0.5, 0.5, 1.0, density);
                    var speed = new Setpoint($"Speed{i}");
                    speed.Out.ConnectTo(belt.Speed);
                    upstream.ConnectTo(belt.In);
                    upstream = belt.Out;
                    speeds.Add(speed);
                    builder.Add(belt).Add(speed);
                }
                else
                {
                    var buffer = new BulkBuffer($"Hold{i}", 0.5 + (random.NextDouble() * 9.5));
                    upstream.ConnectTo(buffer.In);
                    upstream = buffer.Out;
                    builder.Add(buffer);
                }
            }

            upstream.ConnectTo(sink.In);
            Simulation sim = builder.Build();

            for (int tick = 0; tick < Ticks; tick++)
            {
                foreach (Setpoint speed in speeds)
                {
                    speed.Value = random.NextDouble();
                }

                feeder.RateKgPerSecond = random.NextDouble() < 0.2 ? 0.0 : random.NextDouble() * 5.0;

                try
                {
                    sim.Tick();
                }
                catch (MassConservationException error)
                {
                    Assert.Fail($"Seed {seed}, tick {tick}: {error.Message}");
                }
            }

            MassBalance balance = sim.MassBalance;
            Assert.True(Math.Abs(balance.Drift) <= 1e-9 * Math.Max(1.0, balance.Created), $"Seed {seed}: drift {balance.Drift}");
            Assert.True(balance.Held >= 0.0, $"Seed {seed}: negative inventory");
            Assert.True(sink.TotalReceived <= balance.Created + 1e-9, $"Seed {seed}: sink received more than was sourced");
        }
    }

    [Fact]
    public void RandomDiscreteChainsConserveMassAndKeepItemOrder()
    {
        for (ulong seed = 0; seed < Seeds; seed++)
        {
            var random = new DeterministicRandom(seed);
            var builder = new SimulationBuilder(Options(seed));
            var feeder = new ItemFeeder("Feeder", Wheel, 2.0, 0.5 + (random.NextDouble() * 1.5));
            var sink = new ItemSink("Sink");
            var speeds = new List<Setpoint>();
            builder.Add(feeder).Add(sink);

            FlowOutlet upstream = feeder.Out;
            int belts = 1 + (int)(random.NextDouble() * 3);
            for (int i = 0; i < belts; i++)
            {
                double length = 0.5 + (random.NextDouble() * 4.5);
                var belt = new DiscreteBelt($"Belt{i}", length, 1.0, minSpacing: random.NextDouble() * 0.5);
                var speed = new Setpoint($"Speed{i}");
                speed.Out.ConnectTo(belt.Speed);
                upstream.ConnectTo(belt.In);
                upstream = belt.Out;
                speeds.Add(speed);
                builder.Add(belt).Add(speed);
            }

            var buffer = new ItemBuffer("Hold", 1 + (int)(random.NextDouble() * 5));
            upstream.ConnectTo(buffer.In);
            buffer.Out.ConnectTo(sink.In);
            builder.Add(buffer);
            Simulation sim = builder.Build();

            for (int tick = 0; tick < Ticks; tick++)
            {
                foreach (Setpoint speed in speeds)
                {
                    speed.Value = random.NextDouble();
                }

                try
                {
                    sim.Tick();
                }
                catch (MassConservationException error)
                {
                    Assert.Fail($"Seed {seed}, tick {tick}: {error.Message}");
                }
            }

            MassBalance balance = sim.MassBalance;
            Assert.True(Math.Abs(balance.Drift) <= 1e-9 * Math.Max(1.0, balance.Created), $"Seed {seed}: drift {balance.Drift}");
            long previous = 0;
            foreach (ItemInstance item in sink.Items)
            {
                Assert.True(item.Id > previous, $"Seed {seed}: item {item.Id} arrived after {previous}");
                previous = item.Id;
            }
        }
    }
}
