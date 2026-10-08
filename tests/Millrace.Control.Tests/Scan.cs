using Millrace.Io;

namespace Millrace.Control.Tests;

/// <summary>
/// Scans a block the way the host does, with no <c>Simulation</c>: the values
/// set here are what the next scan reads, an output not set in a scan holds its
/// previous value, and <c>Elapsed</c> is zero on the first scan and the block's
/// period on every one after.
/// </summary>
internal sealed class Scan
{
    private readonly IScanBlock _block;
    private readonly TagValue[] _inputs;
    private readonly TagValue[] _commands;
    private readonly TagValue[] _published;
    private ScanOutputs _outputs;
    private TimeSpan _sinceStart;
    private long _tick;
    private bool _scanned;

    public Scan(IScanBlock block, double deltaSeconds = 0.01)
    {
        _block = block;
        DeltaSeconds = deltaSeconds;
        _inputs = new TagValue[block.Inputs.Count];
        _commands = new TagValue[block.Commands.Count];
        _published = new TagValue[block.Outputs.Count];

        for (int i = 0; i < _inputs.Length; i++)
        {
            _inputs[i] = Zero(block.Inputs[i].Kind);
        }

        for (int i = 0; i < _commands.Length; i++)
        {
            _commands[i] = Zero(block.Commands[i].Kind);
        }

        for (int i = 0; i < _published.Length; i++)
        {
            _published[i] = Zero(block.Outputs[i].Kind);
        }

        _outputs = new ScanOutputs(block.Outputs.Count, block.Writes.Count);
    }

    /// <summary>The simulation time step the scans pretend to run at.</summary>
    public double DeltaSeconds { get; }

    /// <summary>The simulation time of the first scan.</summary>
    public static DateTimeOffset Start { get; } = new(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);

    /// <summary>Every event raised since this harness was created, in order.</summary>
    public List<BlockEvent> Events { get; } = [];

    /// <summary>The events the most recent scan raised.</summary>
    public IReadOnlyList<BlockEvent> LastEvents { get; private set; } = [];

    /// <summary>The writes the most recent scan commanded, in the block's <c>Writes</c> order.</summary>
    public IReadOnlyList<KeyValuePair<string, TagValue>> LastWrites { get; private set; } = [];

    public Scan Set(string tag, bool value) => SetInput(tag, TagValue.Bool(value));

    public Scan Set(string tag, double value) => SetInput(tag, TagValue.Double(value));

    public Scan Set(string tag, long value) => SetInput(tag, TagValue.Int64(value));

    /// <summary>Sets an input to a whole tag value, quality included.</summary>
    public Scan Set(string tag, TagValue value) => SetInput(tag, value);

    /// <summary>Sets a command pin, by its relative name.</summary>
    public Scan Command(string pin, bool value)
    {
        _commands[Index(_block.Commands, pin, "command")] = TagValue.Bool(value);
        return this;
    }

    /// <summary>Runs one scan.</summary>
    public Scan Once()
    {
        if (_scanned)
        {
            _sinceStart += _block.ScanPeriod;
            _tick += PeriodTicks;
        }

        double elapsed = _scanned ? _block.ScanPeriod.TotalSeconds : 0.0;
        var inputs = new ScanInputs(_inputs, _commands, _tick, Start + _sinceStart, DeltaSeconds, elapsed);

        _outputs.Reset();
        _block.Scan(in inputs, ref _outputs);
        _scanned = true;

        for (int i = 0; i < _published.Length; i++)
        {
            if (_outputs.TryOutput(i, out TagValue value))
            {
                _published[i] = value;
            }
        }

        var writes = new List<KeyValuePair<string, TagValue>>();
        for (int i = 0; i < _block.Writes.Count; i++)
        {
            if (_outputs.TryWrite(i, out TagValue value))
            {
                writes.Add(new KeyValuePair<string, TagValue>(_block.Writes[i].Name, value));
            }
        }

        LastWrites = writes;
        LastEvents = _outputs.Events.ToArray();
        Events.AddRange(LastEvents);
        return this;
    }

    /// <summary>Runs <paramref name="count"/> scans with the inputs as they stand.</summary>
    public Scan Times(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Once();
        }

        return this;
    }

    /// <summary>The value published on a Bool output.</summary>
    public bool Bool(string pin) => Published(pin).AsBool;

    /// <summary>The value published on a Double output.</summary>
    public double Double(string pin) => Published(pin).AsDouble;

    /// <summary>The value published on an Int64 output.</summary>
    public long Int64(string pin) => Published(pin).AsInt64;

    /// <summary>What the most recent scan commanded on <paramref name="tag"/>, if anything.</summary>
    public bool TryWrite(string tag, out TagValue value)
    {
        foreach (KeyValuePair<string, TagValue> write in LastWrites)
        {
            if (string.Equals(write.Key, tag, StringComparison.Ordinal))
            {
                value = write.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Every event code so far, comma-separated: a readable assertion for a whole sequence.</summary>
    public string Codes() => string.Join(",", Events.Select(e => e.Code));

    private long PeriodTicks => (long)Math.Round(_block.ScanPeriod.TotalSeconds / DeltaSeconds);

    private Scan SetInput(string tag, TagValue value)
    {
        _inputs[Index(_block.Inputs, tag, "input")] = value;
        return this;
    }

    private TagValue Published(string pin) => _published[Index(_block.Outputs, pin, "output")];

    private static TagValue Zero(TagKind kind) => kind switch
    {
        TagKind.Bool => TagValue.Bool(false),
        TagKind.Double => TagValue.Double(0.0),
        _ => TagValue.Int64(0L),
    };

    private static int Index(IReadOnlyList<TagRef> pins, string name, string noun)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (string.Equals(pins[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException(
            $"No {noun} '{name}'. The block has: {string.Join(", ", pins.Select(p => p.Name))}.", nameof(name));
    }

    private static int Index(IReadOnlyList<TagSpec> pins, string name, string noun)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (string.Equals(pins[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException(
            $"No {noun} '{name}'. The block has: {string.Join(", ", pins.Select(p => p.Name))}.", nameof(name));
    }
}
