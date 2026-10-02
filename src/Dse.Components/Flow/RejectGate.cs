using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Telemetry;
using Dse.Io;

namespace Dse.Components.Flow;

/// <summary>
/// A measuring station with a kicker: holds one item for a dwell, then sends
/// it on through <c>Out</c>, or through <c>RejectOut</c> when the
/// <c>Reject</c> input is true on the tick it leaves. The dwell is what gives
/// an instrument and a PLC time to decide while the item is still on the
/// station. The choice is taken again on every tick the chosen outlet refuses
/// the item.
/// </summary>
public sealed class RejectGate : FlowComponentBase, IItemConsumer, IItemProducer, IMaterialObservable, IFaultTarget, ITagProvider
{
    /// <summary>The kicker does not fire: every item leaves by <c>Out</c> until cleared.</summary>
    public const string Stuck = "stuck";

    // Dwell accumulates dt once per tick; the tolerance absorbs the rounding of
    // many small steps (ten steps of 0.1 s sum to 0.9999999999999999 s).
    private const double DwellTolerance = 1e-9;

    private static readonly FaultDescriptor[] Faults =
    [
        new(Stuck, "The kicker does not fire; every item leaves by Out whatever Reject says, until the fault is cleared."),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "reject-gate",
        ComponentCategory.Flow,
        "A measuring station for discrete items: holds each for a dwell, then passes it on, or diverts it to the reject outlet while Reject is true.",
        (id, p) => new RejectGate(id, p.Double("dwellSeconds")))
    {
        Parameters =
        [
            Param.Double("dwellSeconds", "How long each item stays on the station before it may leave.", "s", min: 0.0, exclusiveMin: true),
        ],
        Ports =
        [
            PortSpec.In<bool>("Reject", description: "True sends the item leaving now to RejectOut. Defaults to false."),
            PortSpec.Out<bool>("Occupied"),
            PortSpec.Out<long>("Passed", "count"),
            PortSpec.Out<long>("Rejected", "count"),
        ],
        FlowPorts =
        [
            PortSpec.Inlet("In", PayloadKind.Discrete),
            PortSpec.Outlet("Out", PayloadKind.Discrete, "Items that pass."),
            PortSpec.Outlet("RejectOut", PayloadKind.Discrete, "Items rejected."),
        ],
        Faults = Faults,
        Tags =
        [
            new TagEntry("Reject", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Occupied", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("Passed", TagKind.Int64, TagAccess.ReadOnly, "count"),
            new TagEntry("Rejected", TagKind.Int64, TagAccess.ReadOnly, "count"),
        ],
        Telemetry = [new TelemetryKey("Passed", "count"), new TelemetryKey("Rejected", "count")],
        Provides = [typeof(IMaterialObservable)],
    };

    private ItemInstance? _item;
    private double _dwelt;
    private long _passed;
    private long _rejected;
    private long _justRejected = -1L;
    private bool _stuck;
    private TelemetryHandle _passedTelemetry;
    private TelemetryHandle _rejectedTelemetry;

    public RejectGate(string id, double dwellSeconds)
        : base(id)
    {
        if (!(dwellSeconds > 0.0) || !double.IsFinite(dwellSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(dwellSeconds), dwellSeconds, "The dwell must be a positive, finite number of seconds.");
        }

        DwellSeconds = dwellSeconds;

        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        RejectOut = AddOutlet("RejectOut", PayloadKind.Discrete);
        Reject = AddInput<bool>("Reject");
        Occupied = AddOutput<bool>("Occupied");
        Passed = AddOutput<long>("Passed");
        Rejected = AddOutput<long>("Rejected");
    }

    public FlowInlet In { get; }

    /// <summary>Where a passed item leaves.</summary>
    public FlowOutlet Out { get; }

    /// <summary>Where a rejected item leaves.</summary>
    public FlowOutlet RejectOut { get; }

    /// <summary>True sends the item leaving now to <see cref="RejectOut"/>. Unconnected reads false.</summary>
    public InputPort<bool> Reject { get; }

    /// <summary>An item is on the station, as of the last evaluate.</summary>
    public OutputPort<bool> Occupied { get; }

    /// <summary>Items that left by <see cref="Out"/>, cumulative.</summary>
    public OutputPort<long> Passed { get; }

    /// <summary>Items that left by <see cref="RejectOut"/>, cumulative.</summary>
    public OutputPort<long> Rejected { get; }

    /// <summary>Seconds each item stays before it may leave.</summary>
    public double DwellSeconds { get; }

    /// <summary>The item on the station, or null.</summary>
    public ItemInstance? Item => _item;

    public override double MassHeld => _item?.Mass ?? 0.0;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Write("Reject", Reject, "Divert the item leaving now to the reject outlet"),
        TagBinding.Read("Occupied", Occupied, "An item is on the station"),
        TagBinding.Read("Passed", Passed, "count", "Items passed"),
        TagBinding.Read("Rejected", Rejected, "count", "Items rejected"),
    ];

    public override void Initialize(in InitContext ctx)
    {
        _passedTelemetry = ctx.RegisterTelemetry("Passed", "count");
        _rejectedTelemetry = ctx.RegisterTelemetry("Rejected", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        Occupied.Value = _item is not null;
        Passed.Value = _passed;
        Rejected.Value = _rejected;
        _passedTelemetry.Write(_passed);
        _rejectedTelemetry.Write(_rejected);
    }

    public override void Advance(in TickContext ctx)
    {
        // WithdrawItem has no context; its links ran just before this, on the same tick (R157).
        if (_justRejected >= 0L)
        {
            ctx.Log(Id, "REJECTED", string.Create(CultureInfo.InvariantCulture, $"Item {_justRejected} rejected."));
            _justRejected = -1L;
        }

        if (_item is not null)
        {
            _dwelt += ctx.Dt;
        }
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) => _item is null;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        if (_item is not null)
        {
            throw new InvalidOperationException($"Reject gate '{Id}' already holds {_item}; it takes one item at a time.");
        }

        _item = item;
        _dwelt = 0.0;
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
    {
        if (_item is not null && _dwelt >= DwellSeconds - DwellTolerance && ReferenceEquals(outlet, Chosen()))
        {
            item = _item;
            return true;
        }

        item = null;
        return false;
    }

    public ItemInstance WithdrawItem(FlowOutlet outlet)
    {
        if (!TryPeekItem(outlet, out ItemInstance? item))
        {
            throw new InvalidOperationException($"Reject gate '{Id}' has nothing ready to leave by '{outlet.Name}'.");
        }

        _item = null;
        _dwelt = 0.0;
        if (ReferenceEquals(outlet, RejectOut))
        {
            _rejected++;
            _justRejected = item.Id;
        }
        else
        {
            _passed++;
        }

        return item;
    }

    /// <summary>The item on the station; position and window are ignored.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_item is null)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(_item.Mass, 0.0, _item.Properties, _item.Id);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        if (string.Equals(faultId, Stuck, StringComparison.Ordinal))
        {
            _stuck = true;
        }
    }

    public void ClearFault(string faultId)
    {
        if (string.Equals(faultId, Stuck, StringComparison.Ordinal))
        {
            _stuck = false;
        }
    }

    /// <summary>The outlet the item leaves by this tick: RejectOut while Reject is true and the kicker works.</summary>
    private FlowOutlet Chosen() => Reject.Value && !_stuck ? RejectOut : Out;
}
