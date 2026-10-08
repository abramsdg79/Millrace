namespace Millrace.Io;

/// <summary>
/// What one scan answers: values for the tags the block owns, values to command
/// on the tags it may write, and events for the log. An output not set in a
/// scan holds its previous value, as a PLC output does.
/// </summary>
/// <remarks>
/// Every field is a reference, so a copy of this struct writes through to the
/// same buffers; the host allocates one per block and calls <see cref="Reset"/>
/// before each scan. <c>default(ScanOutputs)</c> has no buffers and cannot be
/// used — always construct one.
/// </remarks>
public struct ScanOutputs
{
    private readonly TagValue[] _outputs;
    private readonly bool[] _outputSet;
    private readonly TagValue[] _writes;
    private readonly bool[] _writeSet;
    private readonly List<BlockEvent> _events;

    /// <summary>Allocates the buffers for a block with the given pin counts. Called once per block.</summary>
    public ScanOutputs(int outputCount, int writeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(outputCount);
        ArgumentOutOfRangeException.ThrowIfNegative(writeCount);

        _outputs = new TagValue[outputCount];
        _outputSet = new bool[outputCount];
        _writes = new TagValue[writeCount];
        _writeSet = new bool[writeCount];
        _events = [];
    }

    /// <summary>How many owned outputs this buffer covers.</summary>
    public readonly int OutputCount => _outputs.Length;

    /// <summary>How many commandable tags this buffer covers.</summary>
    public readonly int WriteCount => _writes.Length;

    /// <summary>The events raised in this scan, in the order they were raised.</summary>
    public readonly IReadOnlyList<BlockEvent> Events => _events;

    /// <summary>Publishes a value on the owned output at <paramref name="index"/>.</summary>
    public readonly void Set(int index, TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _outputs.Length);
        _outputs[index] = value;
        _outputSet[index] = true;
    }

    /// <summary>Commands the plant tag at <paramref name="index"/> in the block's writes.</summary>
    public readonly void Write(int index, TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _writes.Length);
        _writes[index] = value;
        _writeSet[index] = true;
    }

    /// <summary>Records an event under the block's id. The message is a sentence ending in a full stop.</summary>
    public readonly void Raise(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _events.Add(new BlockEvent(code, message));
    }

    /// <summary>Whether the output at <paramref name="index"/> was set in this scan, and its value.</summary>
    public readonly bool TryOutput(int index, out TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _outputs.Length);
        value = _outputs[index];
        return _outputSet[index];
    }

    /// <summary>Whether the write at <paramref name="index"/> was commanded in this scan, and its value.</summary>
    public readonly bool TryWrite(int index, out TagValue value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _writes.Length);
        value = _writes[index];
        return _writeSet[index];
    }

    /// <summary>Forgets what the previous scan set, wrote and raised. The buffers are kept.</summary>
    public readonly void Reset()
    {
        Array.Clear(_outputSet);
        Array.Clear(_writeSet);
        _events.Clear();
    }
}
