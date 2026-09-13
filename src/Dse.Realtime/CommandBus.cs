using Dse.Io;

namespace Dse.Realtime;

/// <summary>
/// The inbound channel (spec 10.6): validates a write against the tag
/// directory and, if it is sound, queues it through the simulation's
/// <see cref="ITagWriter"/> for phase 1 of the next tick. Never throws on bad
/// input — remote callers send junk — and reports every command to the
/// optional recorder.
/// </summary>
public sealed class CommandBus
{
    private readonly ITagWriter _writer;
    private readonly ICommandRecorder? _recorder;
    private long _accepted;
    private long _rejected;

    /// <summary>Creates a bus over <paramref name="writer"/>, validating against its directory.</summary>
    public CommandBus(ITagWriter writer, ICommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        _recorder = recorder;
    }

    /// <summary>The directory commands are validated against.</summary>
    public ITagDirectory Directory => _writer.Directory;

    /// <summary>Commands forwarded to the writer.</summary>
    public long Accepted => Volatile.Read(ref _accepted);

    /// <summary>Commands refused.</summary>
    public long Rejected => Volatile.Read(ref _rejected);

    /// <summary>Validates and forwards one write.</summary>
    public CommandOutcome Write(string tag, TagValue value)
    {
        ArgumentNullException.ThrowIfNull(tag);

        CommandOutcome outcome = Validate(tag, value, out TagDescriptor? descriptor);
        if (outcome == CommandOutcome.Accepted)
        {
            _writer.Write(descriptor!.Index, value);
            Interlocked.Increment(ref _accepted);
        }
        else
        {
            Interlocked.Increment(ref _rejected);
        }

        _recorder?.Record(new TagCommand(tag, value, outcome));
        return outcome;
    }

    /// <summary>Validates and forwards a bool.</summary>
    public CommandOutcome WriteBool(string tag, bool value) => Write(tag, TagValue.Bool(value));

    /// <summary>Validates and forwards a double.</summary>
    public CommandOutcome WriteDouble(string tag, double value) => Write(tag, TagValue.Double(value));

    /// <summary>Validates and forwards a long.</summary>
    public CommandOutcome WriteInt64(string tag, long value) => Write(tag, TagValue.Int64(value));

    private CommandOutcome Validate(string tag, TagValue value, out TagDescriptor? descriptor)
    {
        if (!Directory.TryFind(tag, out TagDescriptor found))
        {
            descriptor = null;
            return CommandOutcome.UnknownTag;
        }

        descriptor = found;
        if (found.Access != TagAccess.ReadWrite)
        {
            return CommandOutcome.ReadOnly;
        }

        if (found.Kind != value.Kind)
        {
            return CommandOutcome.KindMismatch;
        }

        if (found.Kind == TagKind.Double)
        {
            double d = value.AsDouble;
            if (!double.IsFinite(d))
            {
                return CommandOutcome.OutOfRange;
            }

            if (found.HasRange && (d < found.RangeLow || d > found.RangeHigh))
            {
                return CommandOutcome.OutOfRange;
            }
        }

        return CommandOutcome.Accepted;
    }
}
