using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Core.Telemetry;
using Millrace.Io;

namespace Millrace.Components.Instruments;

/// <summary>
/// A photo-eye across a discrete belt. Counts every new item id it sees in
/// its window; an item that stops in front of it is counted once. Two items
/// passing through the window within one tick are one count — exactly what
/// a real photo-eye misses.
/// </summary>
public sealed class PartCounter : ComponentBase, IFaultTarget, ITagProvider
{
    /// <summary>The eye is obscured: it sees nothing until cleared.</summary>
    public const string Blinded = "blinded";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Blinded, "The photo-eye is obscured and sees nothing until the fault is cleared."),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "part-counter",
        ComponentCategory.Instrumentation,
        "Counts items entering a window on a belt and reports whether one is in it now.",
        (id, p) => new PartCounter(id, p.Reference<IMaterialObservable>("belt"), p.Double("positionM"), p.Double("windowM")))
    {
        Parameters =
        [
            Param.Reference<IMaterialObservable>("belt", "The belt watched: a discrete belt, or a composite that has one."),
            Param.Double("positionM", "Centre of the window, from the tail.", "m", min: 0.0),
            Param.Double("windowM", "Length of the window.", "m", min: 0.0),
        ],
        Ports = [PortSpec.Out<long>("Count", "count"), PortSpec.Out<bool>("Present")],
        Faults = Faults,
        Tags = [new TagEntry("Count", TagKind.Int64, TagAccess.ReadOnly, "count"), new TagEntry("Present", TagKind.Bool, TagAccess.ReadOnly)],
        Telemetry = [new TelemetryKey("Count", "count")],
    };

    private readonly IMaterialObservable _belt;
    private long _count;
    private long _lastId;
    private bool _blinded;
    private TelemetryHandle _countTelemetry;

    public PartCounter(string id, IMaterialObservable belt, double positionM, double windowM)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(belt);
        ArgumentOutOfRangeException.ThrowIfNegative(positionM);
        ArgumentOutOfRangeException.ThrowIfNegative(windowM);
        _belt = belt;
        PositionM = positionM;
        WindowM = windowM;
        Count = AddOutput<long>("Count");
        Present = AddOutput<bool>("Present");
    }

    /// <summary>Items counted, cumulative.</summary>
    public OutputPort<long> Count { get; }

    /// <summary>True while an item is in the window.</summary>
    public OutputPort<bool> Present { get; }

    /// <summary>Metres from the belt's tail.</summary>
    public double PositionM { get; }

    /// <summary>Metres either side of <see cref="PositionM"/> the eye's window spans.</summary>
    public double WindowM { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Count", Count, "count", "Items counted"),
        TagBinding.Read("Present", Present, "Item in the window"),
    ];

    public override void Initialize(in InitContext ctx) =>
        _countTelemetry = ctx.RegisterTelemetry("Count", "count");

    public override void Evaluate(in TickContext ctx)
    {
        MaterialObservation seen = default;
        bool present = !_blinded && _belt.TryObserve(PositionM, WindowM, out seen);
        if (present && seen.ItemId != _lastId)
        {
            _count++;
            _lastId = seen.ItemId;
        }

        Present.Value = present;
        Count.Value = _count;
        _countTelemetry.Write(_count);
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _blinded = true;

    public void ClearFault(string faultId) => _blinded = false;
}
