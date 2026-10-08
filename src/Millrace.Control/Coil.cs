using Millrace.Io;

namespace Millrace.Control;

/// <summary>
/// An output coil: energised while one Bool tag equals its normal value, and
/// driving one read-write Bool tag to follow. It writes on its first scan and
/// on every scan where the value changes — never otherwise, so the log carries
/// one <c>WRITE</c> per transition. Claim the output so nothing else changes
/// it between transitions.
/// </summary>
public sealed class Coil : IScanBlock
{
    private readonly Condition _condition;
    private bool _scanned;
    private bool _written;

    /// <summary>Creates a coil.</summary>
    /// <param name="id">The block id; prefixes <c>Energised</c>.</param>
    /// <param name="condition">The Bool tag watched, and the value that energises the coil.</param>
    /// <param name="output">The read-write Bool tag the coil drives.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Coil(string id, Condition condition, string output, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentException.ThrowIfNullOrWhiteSpace(condition.Tag, nameof(condition));
        ArgumentException.ThrowIfNullOrWhiteSpace(output);

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _condition = condition;
        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = [new TagRef(condition.Tag, TagKind.Bool)];
        Writes = [new TagRef(output, TagKind.Bool)];
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
        new TagSpec("Energised", TagKind.Bool, "", "The condition is at its normal value; the output is driven true"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } = [];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        // The value is used whatever its quality, as the interlock and the permissive do (R160).
        bool energised = inputs.Input(0).AsBool == _condition.Normal;

        if (!_scanned || energised != _written)
        {
            outputs.Write(0, TagValue.Bool(energised));
            _written = energised;
            _scanned = true;
        }

        outputs.Set(0, TagValue.Bool(energised));
    }
}
