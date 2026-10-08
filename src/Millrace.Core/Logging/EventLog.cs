using System.Globalization;
using System.Text;

namespace Millrace.Core.Logging;

/// <summary>
/// The ordered log of discrete events. This is the primary regression artifact:
/// two runs of the same scenario must produce byte-identical text.
/// </summary>
public sealed class EventLog
{
    private readonly List<SimEventRecord> _records = [];

    public IReadOnlyList<SimEventRecord> Records => _records;

    public void Record(long tick, DateTimeOffset simTime, string source, string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);

        _records.Add(new SimEventRecord(tick, simTime, source, code, message));
    }

    /// <summary>
    /// The golden-file format. Stable by contract — changing it invalidates every
    /// committed expected-output file.
    /// </summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        foreach (SimEventRecord record in _records)
        {
            builder.Append(record.SimTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                   .Append("  ").Append(record.Source)
                   .Append("  ").Append(record.Code)
                   .Append("  ").Append(record.Message)
                   .Append(Environment.NewLine);
        }

        return builder.ToString();
    }
}
