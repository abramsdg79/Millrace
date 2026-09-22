namespace Dse.Io;

/// <summary>
/// What one scan is given. Values are as of the end of the previous tick — the
/// PLC asymmetry — indexed by position in the block's <see cref="IScanBlock.Inputs"/>
/// and <see cref="IScanBlock.Commands"/> lists.
/// </summary>
public readonly struct ScanInputs
{
    private readonly ReadOnlyMemory<TagValue> _inputs;
    private readonly ReadOnlyMemory<TagValue> _commands;

    /// <summary>Creates the inputs of one scan. The host reuses its buffers; a test may pass arrays.</summary>
    /// <param name="inputs">One value per <see cref="IScanBlock.Inputs"/> entry, in order.</param>
    /// <param name="commands">One value per <see cref="IScanBlock.Commands"/> entry, in order.</param>
    /// <param name="tick">The tick this scan is running on.</param>
    /// <param name="now">The simulation time of that tick.</param>
    /// <param name="deltaSeconds">The simulation time step in seconds.</param>
    /// <param name="elapsed">Simulation seconds since this block's previous scan; zero on the first.</param>
    public ScanInputs(
        ReadOnlyMemory<TagValue> inputs,
        ReadOnlyMemory<TagValue> commands,
        long tick,
        DateTimeOffset now,
        double deltaSeconds,
        double elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsed);

        _inputs = inputs;
        _commands = commands;
        Tick = tick;
        Now = now;
        DeltaSeconds = deltaSeconds;
        Elapsed = elapsed;
    }

    /// <summary>The tick this scan is running on.</summary>
    public long Tick { get; }

    /// <summary>The simulation time of that tick. Never wall-clock.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>The simulation time step in seconds.</summary>
    public double DeltaSeconds { get; }

    /// <summary>Simulation seconds since this block's previous scan; zero on the first.</summary>
    public double Elapsed { get; }

    /// <summary>How many input pins this scan carries.</summary>
    public int InputCount => _inputs.Length;

    /// <summary>How many command pins this scan carries.</summary>
    public int CommandCount => _commands.Length;

    /// <summary>The value of the input pin at <paramref name="index"/>.</summary>
    public TagValue Input(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _inputs.Length);
        return _inputs.Span[index];
    }

    /// <summary>The value of the command pin at <paramref name="index"/>.</summary>
    public TagValue Command(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _commands.Length);
        return _commands.Span[index];
    }
}
