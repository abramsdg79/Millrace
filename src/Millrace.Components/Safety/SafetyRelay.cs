using System.Globalization;
using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Faults;
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Io;

namespace Millrace.Components.Safety;

/// <summary>
/// A latching safety relay. Any open channel drops it and it stays dropped
/// until every channel is healthy and a reset edge arrives — starting from
/// power-up, when it needs its first reset. Its output goes straight to the
/// starter's safety input, so the circuit works with no controller at all.
/// </summary>
public sealed class SafetyRelay : ComponentBase, IFaultTarget, ITagProvider
{
    /// <summary>The relay stays energised whatever the channels say.</summary>
    public const string StuckEnergised = "stuck-energised";

    /// <summary>The coil is open: the relay cannot energise.</summary>
    public const string CoilFailure = "coil-failure";

    private static readonly FaultDescriptor[] Faults =
    [
        new(StuckEnergised, "The relay contacts are welded; it stays energised whatever the channels say."),
        new(CoilFailure, "The coil is open; the relay cannot energise until the fault is cleared."),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "safety-relay",
        ComponentCategory.Safety,
        "Energises only while every channel is healthy; once dropped, stays dropped until a reset edge.",
        (id, p) => new SafetyRelay(id, p.Int("channels")))
    {
        Parameters = [Param.Int("channels", "Number of monitored channels.", min: 1)],
        Ports =
        [
            PortSpec.In<bool>("Channel{n}", description: "One monitored loop; healthy when unwired.", repeat: new PortRepeat("channels")),
            PortSpec.In<bool>("Reset", description: "Safety reset, rising edge."),
            PortSpec.Out<bool>("Ok"),
        ],
        Faults = Faults,
        Tags = [new TagEntry("Reset", TagKind.Bool, TagAccess.ReadWrite), new TagEntry("Ok", TagKind.Bool, TagAccess.ReadOnly)],
    };

    private readonly InputPort<bool>[] _channels;
    private bool _energised;
    private bool _wasReset;
    private bool _stuck;
    private bool _coilFailed;

    public SafetyRelay(string id, int channels)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        _channels = new InputPort<bool>[channels];
        for (int i = 0; i < channels; i++)
        {
            _channels[i] = AddInput<bool>(
                string.Create(CultureInfo.InvariantCulture, $"Channel{i + 1}"),
                defaultValue: true);
        }

        Reset = AddInput<bool>("Reset");
        Ok = AddOutput<bool>("Ok");
    }

    public int ChannelCount => _channels.Length;

    /// <summary>A safety channel, 1-based. Unconnected reads true (healthy).</summary>
    public InputPort<bool> Channel(int index)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _channels.Length);
        return _channels[index - 1];
    }

    /// <summary>Rising edge re-energises the relay if every channel is healthy.</summary>
    public InputPort<bool> Reset { get; }

    /// <summary>True while energised: the contactor may close.</summary>
    public OutputPort<bool> Ok { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Reset", Reset, "Safety reset, rising edge"),
        TagBinding.Read("Ok", Ok, "Relay energised"),
    ];

    public override void Evaluate(in TickContext ctx)
    {
        int open = -1;
        for (int i = 0; i < _channels.Length; i++)
        {
            if (!_channels[i].Value)
            {
                open = i;
                break;
            }
        }

        bool reset = Reset.Value;
        bool resetEdge = reset && !_wasReset;
        _wasReset = reset;

        if (open >= 0)
        {
            if (_energised)
            {
                ctx.Log(Id, "SAFETY_TRIP", $"{_channels[open].Name} open; relay de-energised.");
            }

            _energised = false;
        }
        else if (!_energised && resetEdge && !_coilFailed)
        {
            _energised = true;
            ctx.Log(Id, "SAFETY_RESET", "All channels healthy; relay energised.");
        }

        Ok.Value = _stuck || (_energised && !_coilFailed);
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case StuckEnergised:
                _stuck = true;
                break;
            case CoilFailure:
                _coilFailed = true;
                _energised = false;
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case StuckEnergised:
                _stuck = false;
                break;
            case CoilFailure:
                _coilFailed = false;
                break;
        }
    }
}
