using System.Diagnostics.CodeAnalysis;
using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Faults;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Core.Telemetry;
using Millrace.Io;

namespace Millrace.Components.Flow;

/// <summary>
/// Turns bulk into discrete items — a dough divider, a billet shear. Bulk
/// collects in a hopper; every cycle one piece of fixed mass is cut off,
/// carrying the hopper's blended properties, and queued for the outlet. The
/// crossing is explicit because real plants contain exactly this machine.
/// </summary>
public sealed class Former : FlowComponentBase, IBulkConsumer, IItemProducer, IMaterialObservable, IFaultTarget, ITagProvider
{
    /// <summary>The cutter jams: nothing is formed until cleared. Bulk still accumulates.</summary>
    public const string Jam = "jam";

    private static readonly FaultDescriptor[] Faults =
    [
        new(Jam, "The cutter jams; no pieces are formed until the fault is cleared."),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "former",
        ComponentCategory.Flow,
        "Turns bulk material into discrete pieces of a fixed mass, one per cycle. The only place material changes kind.",
        (id, p) => new Former(
            id, p.Material("input"), p.Material("output"), p.Double("pieceMassKg"), p.Double("cycleSeconds"), p.Double("hopperCapacityKg"),
            p.IntOr("outputQueueCapacity", int.MaxValue)))
    {
        Parameters =
        [
            Param.Material("input", "The bulk material consumed.", PayloadKind.Bulk),
            Param.Material("output", "The discrete material produced.", PayloadKind.Discrete),
            Param.Double("pieceMassKg", "Mass of one piece.", "kg", min: 0.0, exclusiveMin: true),
            Param.Double("cycleSeconds", "Time to form one piece.", "s", min: 0.0, exclusiveMin: true),
            Param.Double("hopperCapacityKg", "Bulk material the former can hold.", "kg", min: 0.0, exclusiveMin: true),
            Param.Int("outputQueueCapacity", "Pieces that may wait at the outlet. Omit for unlimited.", "count", min: 1, optional: true),
        ],
        Ports = [PortSpec.Out<long>("PiecesFormed", "count"), PortSpec.Out<double>("HopperLevel", "fraction"), PortSpec.Out<int>("Queued", "count")],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk), PortSpec.Outlet("Out", PayloadKind.Discrete)],
        Faults = Faults,
        Tags =
        [
            new TagEntry("PiecesFormed", TagKind.Int64, TagAccess.ReadOnly, "count"),
            new TagEntry("HopperLevel", TagKind.Double, TagAccess.ReadOnly, "fraction"),
            new TagEntry("Queued", TagKind.Int64, TagAccess.ReadOnly, "count"),
        ],
        Telemetry = [new TelemetryKey("Hopper", "kg"), new TelemetryKey("Formed", "count")],
        Provides = [typeof(IMaterialObservable)],
    };

    private readonly Queue<ItemInstance> _ready = new();
    private readonly MaterialType _output;
    private BulkLot _hopper;
    private ItemIdSequence? _ids;
    private double _elapsed;
    private long _formed;
    private bool _jammed;
    private TelemetryHandle _hopperTelemetry;
    private TelemetryHandle _formedTelemetry;

    public Former(
        string id,
        MaterialType input,
        MaterialType output,
        double pieceMassKg,
        double cycleSeconds,
        double hopperCapacityKg,
        int outputQueueCapacity = int.MaxValue)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (input.Kind != PayloadKind.Bulk)
        {
            throw new ArgumentException($"Former input '{input}' must be a Bulk material.", nameof(input));
        }

        if (output.Kind != PayloadKind.Discrete)
        {
            throw new ArgumentException($"Former output '{output}' must be a Discrete material.", nameof(output));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pieceMassKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycleSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hopperCapacityKg);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pieceMassKg, hopperCapacityKg);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputQueueCapacity);

        _output = output;
        PieceMassKg = pieceMassKg;
        CycleSeconds = cycleSeconds;
        HopperCapacityKg = hopperCapacityKg;
        OutputQueueCapacity = outputQueueCapacity;

        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        PiecesFormed = AddOutput<long>("PiecesFormed");
        HopperLevel = AddOutput<double>("HopperLevel");
        Queued = AddOutput<int>("Queued");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    public OutputPort<long> PiecesFormed { get; }

    /// <summary>Hopper mass over capacity, 0..1.</summary>
    public OutputPort<double> HopperLevel { get; }

    public OutputPort<int> Queued { get; }

    /// <summary>kg.</summary>
    public double PieceMassKg { get; }

    /// <summary>s between pieces.</summary>
    public double CycleSeconds { get; }

    /// <summary>kg.</summary>
    public double HopperCapacityKg { get; }

    public int OutputQueueCapacity { get; }

    public BulkLot Hopper => _hopper;

    public override double MassHeld => _hopper.Mass + (_ready.Count * PieceMassKg);

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.Read("PiecesFormed", PiecesFormed, "count", "Pieces formed"),
        TagBinding.Read("HopperLevel", HopperLevel, "fraction", 0.0, 1.0, "Hopper mass over capacity"),
        TagBinding.Read("Queued", Queued, "count", "Pieces waiting at the outlet"),
    ];

    public override void Initialize(in InitContext ctx)
    {
        _ids = ctx.Items;
        _hopperTelemetry = ctx.RegisterTelemetry("Hopper", "kg");
        _formedTelemetry = ctx.RegisterTelemetry("Formed", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        PiecesFormed.Value = _formed;
        HopperLevel.Value = _hopper.Mass / HopperCapacityKg;
        Queued.Value = _ready.Count;
        _hopperTelemetry.Write(_hopper.Mass);
        _formedTelemetry.Write(_formed);
    }

    public override void Advance(in TickContext ctx)
    {
        _elapsed += ctx.Dt;
        if (_elapsed < CycleSeconds - 1e-12)
        {
            return;
        }

        _elapsed -= CycleSeconds;
        if (_jammed || _ready.Count >= OutputQueueCapacity || _hopper.Mass < PieceMassKg - 1e-12)
        {
            return;
        }

        BulkLot piece = _hopper.Take(PieceMassKg, out BulkLot remaining);
        _hopper = remaining;
        _ready.Enqueue(new ItemInstance(_ids!.Next(), _output, piece.Mass, piece.Properties));
        _formed++;
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, HopperCapacityKg - _hopper.Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _hopper = _hopper.Merge(lot);

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item) =>
        _ready.TryPeek(out item);

    public ItemInstance WithdrawItem(FlowOutlet outlet) => _ready.Dequeue();

    /// <summary>The hopper's contents; position and window are ignored.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_hopper.IsEmpty)
        {
            observation = default;
            return false;
        }

        observation = new MaterialObservation(_hopper.Mass, 0.0, _hopper.Properties, 0L);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _jammed = true;

    public void ClearFault(string faultId) => _jammed = false;
}
