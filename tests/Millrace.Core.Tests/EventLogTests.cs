using Millrace.Core.Logging;
using Xunit;

namespace Millrace.Core.Tests;

public class EventLogTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 1, 1, 6, 32, 11, TimeSpan.Zero);

    [Fact]
    public void RecordsArePreservedInOrder()
    {
        var log = new EventLog();

        log.Record(1, Start, "CV001", "STARTED", "Conveyor started");
        log.Record(2, Start.AddSeconds(7), "CV001", "AT_SPEED", "Reached speed");

        Assert.Equal(2, log.Records.Count);
        Assert.Equal("STARTED", log.Records[0].Code);
        Assert.Equal("AT_SPEED", log.Records[1].Code);
        Assert.Equal(2, log.Records[1].Tick);
    }

    [Fact]
    public void TextFormatIsStableAndCultureInvariant()
    {
        var log = new EventLog();
        log.Record(1, Start, "CV001", "STARTED", "Conveyor started");
        log.Record(760, Start.AddSeconds(7.5), "CV001", "AT_SPEED", "Reached speed");

        string expected =
            "06:32:11.000  CV001  STARTED  Conveyor started" + Environment.NewLine +
            "06:32:18.500  CV001  AT_SPEED  Reached speed" + Environment.NewLine;

        Assert.Equal(expected, log.ToText());
    }

    [Fact]
    public void EmptyLogProducesEmptyText()
    {
        Assert.Equal(string.Empty, new EventLog().ToText());
    }

    [Fact]
    public void RejectsBlankSourceOrCode()
    {
        var log = new EventLog();

        Assert.Throws<ArgumentException>(() => log.Record(1, Start, " ", "CODE", "m"));
        Assert.Throws<ArgumentException>(() => log.Record(1, Start, "SRC", "", "m"));
    }
}
