using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Telemetry;

namespace Dse.Components.Flow;

/// <summary>
/// A capacity-limited hold between two transports. It accepts the room it has
/// and offers everything it holds, so a blocked outlet fills it and a full
/// chute stops the belt feeding it — spec 7.8's chain, with no code of its own.
/// </summary>
public sealed class TransferChute : FlowComponentBase, IBulkConsumer, IBulkProducer, IMaterialObservable, IFaultTarget, ITagProvider
{
    /// <summary>Material bridges in the chute: nothing discharges until cleared.</summary>
    public const string Blockage = "blockage";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Blockage, "Material bridges in the chute; nothing discharges until the fault is cleared."),
    ];

    private BulkLot _held;
    private bool _blocked;
    private bool _full;
    private TelemetryHandle _heldTelemetry;

    public TransferChute(string id, double capacityKg)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityKg);
        CapacityKg = capacityKg;

        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
        Level = AddOutput<double>("Level");
        Full = AddOutput<bool>("Full");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Held mass over capacity, 0..1, as of the last evaluate.</summary>
    public OutputPort<double> Level { get; }

    public OutputPort<bool> Full { get; }

    /// <summary>kg.</summary>
    public double CapacityKg { get; }

    public BulkLot Contents => _held;

    public override double MassHeld => _held.Mass;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("Level", Level, "fraction", 0.0, 1.0, "Held mass over capacity"),
        TagBinding.Read("Full", Full, "At capacity"),
    ];

    public override void Initialize(in InitContext ctx) =>
        _heldTelemetry = ctx.RegisterTelemetry("Held", "kg");

    public override void Evaluate(in TickContext ctx)
    {
        double level = _held.Mass / CapacityKg;
        Level.Value = level;
        _heldTelemetry.Write(_held.Mass);

        bool full = level >= 1.0 - 1e-9;
        if (full != _full)
        {
            ctx.Log(Id, full ? "FULL" : "CLEARED", full ? "Chute is full." : "Chute has room again.");
        }

        _full = full;
        Full.Value = full;
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, CapacityKg - _held.Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _held = _held.Merge(lot);

    public double OfferMass(FlowOutlet outlet) => _blocked ? 0.0 : _held.Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _held.Take(mass, out BulkLot remaining);
        _held = remaining;
        return taken;
    }

    /// <summary>The contents; position and window are ignored, a chute has no length.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_held.IsEmpty)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(_held.Mass, 0.0, _held.Properties, 0L);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _blocked = true;

    public void ClearFault(string faultId) => _blocked = false;
}
