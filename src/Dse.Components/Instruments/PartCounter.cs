using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Telemetry;

namespace Dse.Components.Instruments;

/// <summary>
/// A photo-eye across a discrete belt. Counts every new item id it sees in
/// its window; an item that stops in front of it is counted once. Two items
/// passing through the window within one tick are one count — exactly what
/// a real photo-eye misses.
/// </summary>
public sealed class PartCounter : ComponentBase, IFaultTarget
{
    /// <summary>The eye is obscured: it sees nothing until cleared.</summary>
    public const string Blinded = "blinded";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Blinded, "The photo-eye is obscured and sees nothing until the fault is cleared."),
    ];

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
