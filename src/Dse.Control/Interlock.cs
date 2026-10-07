using Dse.Io;

namespace Dse.Control;

/// <summary>
/// The conditions that stop a running thing. Any abnormal condition latches
/// <c>Tripped</c>, captures <c>FirstOut</c> and sends the trip writes — on the
/// trip scan only (R71). The latch clears on a rising edge of <c>Reset</c> while
/// every condition is normal, and on nothing else; the scan that clears it sends
/// the reset writes, once. A rising edge that finds it tripped with a condition
/// still abnormal is refused: no write, and <c>RESET_REFUSED</c> names the first
/// abnormal condition in declared order (plan 7, R184). A tag may be in both
/// lists: that is how an interlock holds a device's run permit off while it is
/// tripped (R122).
/// </summary>
public sealed class Interlock : IScanBlock
{
    private readonly Condition[] _conditions;
    private readonly BlockWrite[] _tripWrites;
    private readonly BlockWrite[] _resetWrites;
    private readonly int[] _resetWriteIndex;
    private bool _tripped;
    private bool _previousReset;
    private long _firstOut = -1L;

    /// <summary>Creates an interlock with no reset writes.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c>, <c>Tripped</c>, <c>FirstOut</c> and <c>Reset</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="tripWrites">What to command when the interlock trips; may be empty. One write per tag.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Interlock(
        string id,
        IReadOnlyList<Condition> conditions,
        IReadOnlyList<BlockWrite> tripWrites,
        TimeSpan scanPeriod)
        : this(id, conditions, tripWrites, scanPeriod, [])
    {
    }

    /// <summary>Creates an interlock.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c>, <c>Tripped</c>, <c>FirstOut</c> and <c>Reset</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="tripWrites">What to command when the interlock trips; may be empty. One write per tag.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    /// <param name="resetWrites">
    /// What to command on the scan that accepts a reset; may be empty. One write per tag; a tag may also be a
    /// trip write, with the same kind, and then the block has one write pin for it.
    /// </param>
    public Interlock(
        string id,
        IReadOnlyList<Condition> conditions,
        IReadOnlyList<BlockWrite> tripWrites,
        TimeSpan scanPeriod,
        IReadOnlyList<BlockWrite> resetWrites)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(tripWrites);
        ArgumentNullException.ThrowIfNull(resetWrites);

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
        _resetWrites = [.. resetWrites];

        var pins = new TagRef[_conditions.Length];
        for (int i = 0; i < _conditions.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_conditions[i].Tag, nameof(conditions));
            pins[i] = new TagRef(_conditions[i].Tag, TagKind.Bool);
        }

        // Trip writes take pins 0..n-1 in order, so an interlock with no reset
        // writes declares exactly the pins it always has.
        var writes = new List<TagRef>(_tripWrites.Length + _resetWrites.Length);
        RejectRepeats(_tripWrites, "on trip", nameof(tripWrites));
        foreach (BlockWrite write in _tripWrites)
        {
            writes.Add(new TagRef(write.Tag, write.Value.Kind));
        }

        RejectRepeats(_resetWrites, "on reset", nameof(resetWrites));
        _resetWriteIndex = new int[_resetWrites.Length];
        for (int i = 0; i < _resetWrites.Length; i++)
        {
            _resetWriteIndex[i] = Pin(writes, new TagRef(_resetWrites[i].Tag, _resetWrites[i].Value.Kind), nameof(resetWrites));
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = pins;
        Writes = [.. writes];
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
            for (int i = 0; i < _resetWrites.Length; i++)
            {
                outputs.Write(_resetWriteIndex[i], _resetWrites[i].Value);
            }
        }
        else if (_tripped && resetEdge)
        {
            // Tripped before this scan (the trip branch above did not run) and a
            // condition still abnormal, so `first` is set.
            outputs.Raise("RESET_REFUSED", $"Reset refused: {_conditions[first].Tag} is not normal.");
        }

        outputs.Set(0, TagValue.Bool(!_tripped));
        outputs.Set(1, TagValue.Bool(_tripped));
        outputs.Set(2, TagValue.Int64(_firstOut));
    }

    /// <summary>Rejects a blank tag, and a tag commanded twice within one list.</summary>
    private static void RejectRepeats(BlockWrite[] list, string when, string parameter)
    {
        for (int i = 0; i < list.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(list[i].Tag, parameter);
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(list[i].Tag, list[j].Tag, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Tag '{list[i].Tag}' is commanded twice {when}. Command each tag once in each list.",
                        parameter);
                }
            }
        }
    }

    /// <summary>The index of the pin on this tag, added if it is new. A trip and a reset write on one tag must agree on the kind.</summary>
    private static int Pin(List<TagRef> pins, TagRef pin, string parameter)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (!string.Equals(pins[i].Name, pin.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (pins[i].Kind != pin.Kind)
            {
                throw new ArgumentException(
                    $"Tag '{pin.Name}' is commanded as a {pins[i].Kind} on trip and as a {pin.Kind} on reset. Use one kind.",
                    parameter);
            }

            return i;
        }

        pins.Add(pin);
        return pins.Count - 1;
    }
}
