using Dse.Core.Telemetry;
using Xunit;

namespace Dse.Core.Tests;

public class TelemetryTests
{
    [Fact]
    public void WritesAreReadableByKey()
    {
        var registry = new TelemetryRegistry();
        TelemetryHandle speed = registry.Register("CV001.Belt.Speed", "m/s");

        speed.Write(3.2);

        Assert.Equal(3.2, registry.Read("CV001.Belt.Speed"));
    }

    [Fact]
    public void ChannelsStartAtZero()
    {
        var registry = new TelemetryRegistry();
        registry.Register("CV001.Belt.Speed", "m/s");

        Assert.Equal(0.0, registry.Read("CV001.Belt.Speed"));
    }

    [Fact]
    public void ChannelsAreListedInRegistrationOrderWithUnits()
    {
        var registry = new TelemetryRegistry();
        registry.Register("B", "kg");
        registry.Register("A", "m/s");

        Assert.Equal(new[] { "B", "A" }, registry.Channels.Select(c => c.Key));
        Assert.Equal(new[] { "kg", "m/s" }, registry.Channels.Select(c => c.Unit));
    }

    [Fact]
    public void DuplicateKeysAreRejected()
    {
        var registry = new TelemetryRegistry();
        registry.Register("A", "m/s");

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => registry.Register("A", "m/s"));
        Assert.Contains("A", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingAnUnknownKeyThrowsAndTryReadReportsFalse()
    {
        var registry = new TelemetryRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Read("nope"));
        Assert.False(registry.TryRead("nope", out double value));
        Assert.Equal(0.0, value);
    }
}
