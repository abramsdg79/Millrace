using Dse.Io;

namespace Dse.Control;

/// <summary>
/// An IEC 61131-3 timer over one Bool tag. <c>ET</c> accumulates the scan
/// period, so it is quantised to it: a 100 ms timer on a 10 ms plant measures
/// in tenths of a second, exactly as a PLC does.
/// </summary>
public sealed class Timer : IScanBlock
{
    private readonly double _preset;
    private bool _q;
    private bool _previous;
    private double _elapsed;

    /// <summary>Creates a timer.</summary>
    /// <param name="id">The block id; prefixes <c>Q</c> and <c>ET</c>.</param>
    /// <param name="mode">Which of the three timers this is.</param>
    /// <param name="input">The full name of the Bool tag to time.</param>
    /// <param name="preset">The delay or pulse length. Zero is allowed and acts immediately.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Timer(string id, TimerMode mode, string input, TimeSpan preset, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode), mode, "A timer is OnDelay, OffDelay or Pulse.");
        }

        if (preset < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset), preset, "The preset must not be negative.");
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        Id = id;
        Mode = mode;
        Preset = preset;
        ScanPeriod = scanPeriod;
        _preset = preset.TotalSeconds;
        Inputs = [new TagRef(input, TagKind.Bool)];
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <summary>Which of the three timers this is.</summary>
    public TimerMode Mode { get; }

    /// <summary>The delay or pulse length.</summary>
    public TimeSpan Preset { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; } = [];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; } =
    [
        new TagSpec("Q", TagKind.Bool, "", "Timer output"),
        new TagSpec("ET", TagKind.Double, "s", "Elapsed time, quantised to the scan period"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } = [];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        bool input = inputs.Input(0).AsBool;

        switch (Mode)
        {
            case TimerMode.OnDelay:
                if (input)
                {
                    if (_elapsed < _preset)
                    {
                        _elapsed = Math.Min(_preset, _elapsed + inputs.Elapsed);
                    }

                    _q = _elapsed >= _preset;
                }
                else
                {
                    _elapsed = 0.0;
                    _q = false;
                }

                break;

            case TimerMode.OffDelay:
                if (input)
                {
                    _elapsed = 0.0;
                    _q = true;
                }
                else if (_q)
                {
                    _elapsed = Math.Min(_preset, _elapsed + inputs.Elapsed);
                    _q = _elapsed < _preset;
                }

                break;

            default:
                if (input && !_previous && !_q)
                {
                    _q = true;
                    _elapsed = 0.0;
                }
                else if (_q)
                {
                    _elapsed = Math.Min(_preset, _elapsed + inputs.Elapsed);
                    _q = _elapsed < _preset;
                }

                break;
        }

        _previous = input;
        outputs.Set(0, TagValue.Bool(_q));
        outputs.Set(1, TagValue.Double(_elapsed));
    }
}
