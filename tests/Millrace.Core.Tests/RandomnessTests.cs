using Millrace.Core.Randomness;
using Xunit;

namespace Millrace.Core.Tests;

public class RandomnessTests
{
    [Fact]
    public void StringHashMatchesKnownFnv1a64Vectors()
    {
        Assert.Equal(18295790033315820519UL, Hash64.OfString("CV001"));
        Assert.Equal(18295791132827448730UL, Hash64.OfString("CV002"));
    }

    [Fact]
    public void CombineMatchesKnownVector()
    {
        Assert.Equal(10340090026681282474UL, Hash64.Combine(492781UL, "CV001"));
    }

    [Fact]
    public void StreamMatchesKnownSplitMix64Vectors()
    {
        var random = new DeterministicRandom(0UL);

        Assert.Equal(16294208416658607535UL, random.NextUInt64());
        Assert.Equal(7960286522194355700UL, random.NextUInt64());
        Assert.Equal(487617019471545679UL, random.NextUInt64());
    }

    [Fact]
    public void NextDoubleMatchesKnownVectorAndStaysInRange()
    {
        Assert.Equal(0.8833108082136426, new DeterministicRandom(0UL).NextDouble(), 15);

        var random = new DeterministicRandom(12345UL);
        for (int i = 0; i < 10_000; i++)
        {
            double value = random.NextDouble();
            Assert.InRange(value, 0.0, 0.9999999999999999);
        }
    }

    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var a = new DeterministicRandom(99UL);
        var b = new DeterministicRandom(99UL);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void GaussianIsDeterministicAndRoughlyStandardNormal()
    {
        var a = new DeterministicRandom(7UL);
        var b = new DeterministicRandom(7UL);
        Assert.Equal(a.NextGaussian(), b.NextGaussian());

        var random = new DeterministicRandom(7UL);
        double sum = 0.0;
        double sumOfSquares = 0.0;
        const int n = 100_000;
        for (int i = 0; i < n; i++)
        {
            double value = random.NextGaussian();
            sum += value;
            sumOfSquares += value * value;
        }

        double mean = sum / n;
        double variance = (sumOfSquares / n) - (mean * mean);
        Assert.InRange(mean, -0.02, 0.02);
        Assert.InRange(variance, 0.95, 1.05);
    }
}
