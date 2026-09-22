using System.Globalization;
using Dse.Io;

namespace Dse.Control;

/// <summary>
/// An analog alarm over one Double tag. Each configured limit publishes the
/// ISA-18.2 pair <c>&lt;Kind&gt;.Active</c> and <c>&lt;Kind&gt;.Acked</c>:
/// (false, true) is normal, (true, false) an unacknowledged alarm, (true, true)
/// an acknowledged one, and (false, false) "cleared, unacknowledged". A rising
/// edge of <c>Ack</c> acknowledges every limit that has anything outstanding,
/// whether it is still active or has already returned to normal.
/// </summary>
public sealed class Alarm : IScanBlock
{
    private readonly AlarmLimit[] _limits;
    private readonly bool[] _active;
    private readonly bool[] _acked;
    private readonly bool[] _crossing;
    private readonly double[] _delay;
    private bool _previousAck;

    /// <summary>Creates an alarm.</summary>
    /// <param name="id">The block id; prefixes every owned tag.</param>
    /// <param name="input">The full name of the Double tag to watch.</param>
    /// <param name="limits">One to four limits, which must ascend LoLo &lt; Lo &lt; Hi &lt; HiHi.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    public Alarm(string id, string input, IReadOnlyList<AlarmLimit> limits, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentNullException.ThrowIfNull(limits);

        if (limits.Count == 0)
        {
            throw new ArgumentException("An alarm needs at least one limit.", nameof(limits));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _limits = [.. limits.OrderBy(limit => limit.Kind)];
        var specs = new TagSpec[_limits.Length * 2];

        for (int i = 0; i < _limits.Length; i++)
        {
            AlarmLimit limit = _limits[i];

            if (!Enum.IsDefined(limit.Kind))
            {
                throw new ArgumentException(
                    $"'{limit.Kind}' is not a limit kind. Use LoLo, Lo, Hi or HiHi.", nameof(limits));
            }

            if (!double.IsFinite(limit.Value))
            {
                throw new ArgumentException(
                    $"The {limit.Kind} limit must be a finite number.", nameof(limits));
            }

            if (!double.IsFinite(limit.Deadband) || limit.Deadband < 0.0)
            {
                throw new ArgumentException(
                    $"The {limit.Kind} deadband must be zero or a finite positive number.", nameof(limits));
            }

            if (limit.OnDelay < TimeSpan.Zero)
            {
                throw new ArgumentException(
                    $"The {limit.Kind} on-delay must not be negative.", nameof(limits));
            }

            if (i > 0 && _limits[i - 1].Kind == limit.Kind)
            {
                throw new ArgumentException(
                    $"The {limit.Kind} limit is configured twice. Configure each limit once.", nameof(limits));
            }

            if (i > 0 && _limits[i - 1].Value >= limit.Value)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Limits must ascend LoLo < Lo < Hi < HiHi, but {_limits[i - 1].Kind} is " +
                        $"{_limits[i - 1].Value} and {limit.Kind} is {limit.Value}."),
                    nameof(limits));
            }

            string kind = limit.Kind.ToString();
            specs[2 * i] = new TagSpec($"{kind}.Active", TagKind.Bool, "", $"The {kind} limit is in alarm");
            specs[(2 * i) + 1] = new TagSpec($"{kind}.Acked", TagKind.Bool, "", $"Nothing is outstanding on the {kind} limit");
        }

        _active = new bool[_limits.Length];
        _acked = new bool[_limits.Length];
        _crossing = new bool[_limits.Length];
        _delay = new double[_limits.Length];
        Array.Fill(_acked, true);

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = [new TagRef(input, TagKind.Double)];
        Outputs = specs;
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
    public IReadOnlyList<TagSpec> Outputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } =
    [
        new TagSpec("Ack", TagKind.Bool, "", "Acknowledges every outstanding limit on a rising edge"),
    ];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        double value = inputs.Input(0).AsDouble;
        bool ack = inputs.Command(0).AsBool;
        bool ackEdge = ack && !_previousAck;
        _previousAck = ack;

        for (int i = 0; i < _limits.Length; i++)
        {
            AlarmLimit limit = _limits[i];
            bool high = limit.Kind is AlarmLimitKind.Hi or AlarmLimitKind.HiHi;

            if (!_active[i])
            {
                if (high ? value > limit.Value : value < limit.Value)
                {
                    _delay[i] = _crossing[i] ? _delay[i] + inputs.Elapsed : 0.0;
                    _crossing[i] = true;

                    if (_delay[i] >= limit.OnDelay.TotalSeconds)
                    {
                        _active[i] = true;
                        _acked[i] = false;
                        _crossing[i] = false;
                        _delay[i] = 0.0;
                        outputs.Raise("ALARM_RAISED", string.Create(CultureInfo.InvariantCulture,
                            $"{limit.Kind}: {value} {(high ? "above" : "below")} {limit.Value}."));
                    }
                }
                else
                {
                    _crossing[i] = false;
                    _delay[i] = 0.0;
                }
            }
            else if (high ? value <= limit.Value - limit.Deadband : value >= limit.Value + limit.Deadband)
            {
                _active[i] = false;
                outputs.Raise("ALARM_CLEARED", string.Create(CultureInfo.InvariantCulture,
                    $"{limit.Kind}: {value} back within limits."));
            }

            if (ackEdge && !_acked[i])
            {
                _acked[i] = true;
                outputs.Raise("ALARM_ACKED", $"{limit.Kind} acknowledged.");
            }

            outputs.Set(2 * i, TagValue.Bool(_active[i]));
            outputs.Set((2 * i) + 1, TagValue.Bool(_acked[i]));
        }
    }
}
