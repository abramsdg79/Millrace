using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// The conditions something needs before it may start. <c>Ok</c> is
/// re-evaluated on every scan and never latches; <c>FirstOut</c> is the index
/// of the first condition to leave normal while <c>Ok</c> was true, and −1 when
/// nothing is out.
/// </summary>
public sealed class Permissive : IScanBlock
{
    private readonly Condition[] _conditions;
    private bool _ok = true;
    private long _firstOut = -1L;

    /// <summary>Creates a permissive.</summary>
    /// <param name="id">The block id; prefixes <c>Ok</c> and <c>FirstOut</c>.</param>
    /// <param name="conditions">At least one Bool tag with its normal polarity, in report order.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Permissive(string id, IReadOnlyList<Condition> conditions, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(conditions);

        if (conditions.Count == 0)
        {
            throw new ArgumentException("A permissive needs at least one condition.", nameof(conditions));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _conditions = [.. conditions];
        var pins = new TagRef[_conditions.Length];
        for (int i = 0; i < _conditions.Length; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(_conditions[i].Tag, nameof(conditions));
            pins[i] = new TagRef(_conditions[i].Tag, TagKind.Bool);
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = pins;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; } =
    [
        new TagSpec("Ok", TagKind.Bool, "", "Every condition is normal"),
        new TagSpec("FirstOut", TagKind.Int64, "", "Index of the first condition to leave normal, or -1"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } = [];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        bool ok = true;
        int first = -1;

        for (int i = 0; i < _conditions.Length; i++)
        {
            if (inputs.Input(i).AsBool != _conditions[i].Normal)
            {
                ok = false;
                if (first < 0)
                {
                    first = i;
                }
            }
        }

        if (!ok && _ok)
        {
            _firstOut = first;
            outputs.Raise("PERMISSIVE_LOST", $"{_conditions[first].Tag} dropped.");
        }
        else if (ok && !_ok)
        {
            _firstOut = -1L;
            outputs.Raise("PERMISSIVE_OK", "All conditions normal.");
        }

        _ok = ok;
        outputs.Set(0, TagValue.Bool(ok));
        outputs.Set(1, TagValue.Int64(_firstOut));
    }
}
