using Dse.Io;

namespace Dse.Control;

/// <summary>
/// The conditions that stop a running thing. Any abnormal condition latches
/// <c>Tripped</c>, captures <c>FirstOut</c> and sends the trip writes — on the
/// trip scan only (R71). The latch clears on a rising edge of <c>Reset</c> while
/// every condition is normal, and on nothing else.
/// </summary>
public sealed class Interlock : IScanBlock
{
    private readonly Condition[] _conditions;
    private readonly BlockWrite[] _tripWrites;
    private bool _tripped;
    private bool _previousReset;
    private long _firstOut = -1L;

    /// <summary>Creates an interlock.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c>, <c>Tripped</c>, <c>FirstOut</c> and <c>Reset</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="tripWrites">What to command when the interlock trips; may be empty. One write per tag.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Interlock(
        string id,
        IReadOnlyList<Condition> conditions,
        IReadOnlyList<BlockWrite> tripWrites,
        TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(tripWrites);

        if (conditions.Count == 0)
        {
            throw new ArgumentException("An interlock needs at least one condition.", nameof(conditions));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _conditions = [.. conditions];
        _tripWrites = [.. tripWrites];

        var pins = new TagRef[_conditions.Length];
        for (int i = 0; i < _conditions.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_conditions[i].Tag, nameof(conditions));
            pins[i] = new TagRef(_conditions[i].Tag, TagKind.Bool);
        }

        var writes = new TagRef[_tripWrites.Length];
        for (int i = 0; i < _tripWrites.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_tripWrites[i].Tag, nameof(tripWrites));
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(_tripWrites[i].Tag, _tripWrites[j].Tag, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Tag '{_tripWrites[i].Tag}' is commanded twice. Command each tag once.",
                        nameof(tripWrites));
                }
            }

            writes[i] = new TagRef(_tripWrites[i].Tag, _tripWrites[i].Value.Kind);
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = pins;
        Writes = writes;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; } =
    [
        new TagSpec("Ok", TagKind.Bool, "", "Not tripped"),
        new TagSpec("Tripped", TagKind.Bool, "", "Latched by an abnormal condition"),
        new TagSpec("FirstOut", TagKind.Int64, "", "Index of the condition that tripped, or -1"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } =
    [
        new TagSpec("Reset", TagKind.Bool, "", "Clears the latch on a rising edge when every condition is normal"),
    ];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        bool reset = inputs.Command(0).AsBool;
        bool resetEdge = reset && !_previousReset;
        _previousReset = reset;

        bool allNormal = true;
        int first = -1;

        for (int i = 0; i < _conditions.Length; i++)
        {
            if (inputs.Input(i).AsBool != _conditions[i].Normal)
            {
                allNormal = false;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (!allNormal && !_tripped)
        {
            _tripped = true;
            _firstOut = first;
            outputs.Raise("INTERLOCK_TRIP", $"{_conditions[first].Tag} abnormal.");
            for (int i = 0; i < _tripWrites.Length; i++)
            {
                outputs.Write(i, _tripWrites[i].Value);
            }
        }
        else if (_tripped && resetEdge && allNormal)
        {
            _tripped = false;
            _firstOut = -1L;
            outputs.Raise("INTERLOCK_RESET", "Reset with all conditions normal.");
        }

        outputs.Set(0, TagValue.Bool(!_tripped));
        outputs.Set(1, TagValue.Bool(_tripped));
        outputs.Set(2, TagValue.Int64(_firstOut));
    }
}
