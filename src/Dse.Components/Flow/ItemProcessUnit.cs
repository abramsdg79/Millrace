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
/// A batch unit for discrete items: Idle → Filling → Processing → Discharging.
/// Fills to a fixed count, applies the transforms to every item each tick
/// while the hold is unsatisfied for any of them, then re-types (optional),
/// applies the yield, and discharges in arrival order. A furnace and a press
/// are configurations of this class.
/// </summary>
public sealed class ItemProcessUnit : FlowComponentBase, IItemConsumer, IItemProducer, IMaterialObservable, IFaultTarget, ITagProvider
{
    /// <summary>The discharge fails to open: nothing leaves until cleared.</summary>
    public const string DischargeJam = "discharge-jam";

    private static readonly FaultDescriptor[] Faults =
    [
        new(DischargeJam, "The discharge fails to open; the batch stays in the unit until the fault is cleared."),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "item-process-unit",
        ComponentCategory.Flow,
        "A batch unit for discrete items — a furnace, a press: takes a batch, holds it while applying transforms, discharges, optionally as a new material.",
        (id, p) => new ItemProcessUnit(
            id,
            p.Int("batchSize"),
            p.Object<IHoldCondition>("hold"),
            p.MaterialOrNull("output"),
            p.Double("yield"),
            p.Objects<IMaterialTransform>("transforms")))
    {
        Parameters =
        [
            Param.Int("batchSize", "Items per batch.", "count", min: 1),
            Param.Object("hold", "When the batch is done.", ObjectSlots.Hold),
            Param.Material("output", "What each item becomes. Omit to keep the material.", PayloadKind.Discrete, optional: true),
            Param.Double("yield", "Fraction of each item's mass that comes out.", @default: 1.0, min: 0.0, max: 1.0, exclusiveMin: true),
            Param.ObjectList("transforms", "Applied, in order, every tick while holding.", ObjectSlots.Transform),
        ],
        Ports =
        [
            PortSpec.In<double>("AmbientTemperature", "°C", "Defaults to 20."),
            PortSpec.Out<ProcessPhase>("Phase"),
            PortSpec.Out<int>("ItemCount", "count"),
            PortSpec.Out<double>("Progress", "fraction"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete), PortSpec.Outlet("Out", PayloadKind.Discrete)],
        Faults = Faults,
        Tags =
        [
            new TagEntry("Phase", TagKind.Int64, TagAccess.ReadOnly),
            new TagEntry("ItemCount", TagKind.Int64, TagAccess.ReadOnly, "count"),
            new TagEntry("Progress", TagKind.Double, TagAccess.ReadOnly, "fraction"),
        ],
        Telemetry = [new TelemetryKey("Items", "count"), new TelemetryKey("Lost", "kg"), new TelemetryKey("Cycles", "count")],
        Provides = [typeof(IMaterialObservable)],
    };

    private readonly List<ItemInstance> _items = [];
    private readonly IHoldCondition _hold;
    private readonly double? _holdSeconds;
    private readonly MaterialType? _output;
    private readonly IMaterialTransform[] _transforms;
    private ProcessPhase _phase;
    private double _elapsed;
    private double _lost;
    private long _cycles;
    private bool _jammed;
    private bool _pendingFill;
    private TelemetryHandle _itemsTelemetry;
    private TelemetryHandle _lostTelemetry;
    private TelemetryHandle _cyclesTelemetry;

    public ItemProcessUnit(
        string id,
        int batchSize,
        IHoldCondition hold,
        MaterialType? output = null,
        double yield = 1.0,
        IReadOnlyList<IMaterialTransform>? transforms = null)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentNullException.ThrowIfNull(hold);
        if (output is not null && output.Kind != PayloadKind.Discrete)
        {
            throw new ArgumentException($"Output '{output}' must be a Discrete material.", nameof(output));
        }

        if (yield <= 0.0 || yield > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(yield), yield, "Yield must be in (0, 1].");
        }

        BatchSize = batchSize;
        _hold = hold;
        _holdSeconds = Hold.TimedSeconds(hold);
        _output = output;
        Yield = yield;
        _transforms = transforms is null ? [] : transforms.ToArray();

        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        AmbientTemperature = AddInput<double>("AmbientTemperature", defaultValue: 20.0);
        Phase = AddOutput<ProcessPhase>("Phase");
        ItemCount = AddOutput<int>("ItemCount");
        Progress = AddOutput<double>("Progress");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Zone temperature handed to the transforms, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> AmbientTemperature { get; }

    public OutputPort<ProcessPhase> Phase { get; }

    /// <summary>Items currently held in the unit, count.</summary>
    public OutputPort<int> ItemCount { get; }

    /// <summary>0..1 through the current phase where that is meaningful.</summary>
    public OutputPort<double> Progress { get; }

    public int BatchSize { get; }

    /// <summary>Fraction of each item's mass that survives release.</summary>
    public double Yield { get; }

    public ProcessPhase CurrentPhase => _phase;

    /// <summary>Items in the unit, in arrival order.</summary>
    public IReadOnlyList<ItemInstance> Items => _items;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            for (int i = 0; i < _items.Count; i++)
            {
                total += _items[i].Mass;
            }

            return total;
        }
    }

    public override double MassDestroyed => _lost;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public IEnumerable<TagBinding> DescribeTags() =>
    [
        TagBinding.ReadEnum("Phase", Phase, "Unit phase"),
        TagBinding.Read("ItemCount", ItemCount, "count", "Items in the unit"),
        TagBinding.Read("Progress", Progress, "fraction", 0.0, 1.0, "Progress through the current phase"),
    ];

    public override void Initialize(in InitContext ctx)
    {
        _itemsTelemetry = ctx.RegisterTelemetry("Items", "count");
        _lostTelemetry = ctx.RegisterTelemetry("Lost", "kg");
        _cyclesTelemetry = ctx.RegisterTelemetry("Cycles", "count");
    }

    public override void Evaluate(in TickContext ctx)
    {
        Phase.Value = _phase;
        ItemCount.Value = _items.Count;
        Progress.Value = _phase switch
        {
            ProcessPhase.Filling => (double)_items.Count / BatchSize,
            ProcessPhase.Processing => _holdSeconds is { } seconds && seconds > 0.0 ? Math.Min(1.0, _elapsed / seconds) : 0.0,
            ProcessPhase.Discharging => 1.0 - ((double)_items.Count / BatchSize),
            _ => 0.0,
        };
        _itemsTelemetry.Write(_items.Count);
        _lostTelemetry.Write(_lost);
        _cyclesTelemetry.Write(_cycles);
    }

    public override void Advance(in TickContext ctx)
    {
        switch (_phase)
        {
            case ProcessPhase.Idle when _pendingFill:
                _pendingFill = false;
                Transition(ProcessPhase.Filling, "First item received; filling.", in ctx);
                if (_items.Count >= BatchSize)
                {
                    StartProcessing(in ctx);
                }

                break;

            case ProcessPhase.Filling when _items.Count >= BatchSize:
                StartProcessing(in ctx);
                break;

            case ProcessPhase.Processing:
                ApplyTransforms(ctx.Dt);
                _elapsed += ctx.Dt;
                if (AllSatisfied())
                {
                    Release(in ctx);
                }

                break;

            case ProcessPhase.Discharging when _items.Count == 0:
                Transition(ProcessPhase.Idle, "Batch discharged; ready for the next.", in ctx);
                _cycles++;
                break;
        }
    }

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) =>
        _phase is ProcessPhase.Idle or ProcessPhase.Filling && _items.Count < BatchSize;

    public void DepositItem(FlowInlet inlet, ItemInstance item)
    {
        _items.Add(item);
        _pendingFill |= _phase == ProcessPhase.Idle;
    }

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
    {
        if (_phase == ProcessPhase.Discharging && !_jammed && _items.Count > 0)
        {
            item = _items[0];
            return true;
        }

        item = null;
        return false;
    }

    public ItemInstance WithdrawItem(FlowOutlet outlet)
    {
        if (!TryPeekItem(outlet, out ItemInstance? item))
        {
            throw new InvalidOperationException($"'{Id}' has nothing to discharge.");
        }

        _items.RemoveAt(0);
        return item;
    }

    /// <summary>The head item; position and window are ignored.</summary>
    public bool TryObserve(double position, double window, out MaterialObservation observation)
    {
        if (_items.Count == 0)
        {
            observation = default;
            return false;
        }

        ItemInstance head = _items[0];
        observation = new MaterialObservation(head.Mass, 0.0, head.Properties, head.Id);
        return true;
    }

    public void ApplyFault(string faultId, FaultArguments arguments) => _jammed = true;

    public void ClearFault(string faultId) => _jammed = false;

    private void StartProcessing(in TickContext ctx)
    {
        _elapsed = 0.0;
        Transition(ProcessPhase.Processing, "Batch complete; processing.", in ctx);
    }

    private void Release(in TickContext ctx)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            ItemInstance item = _items[i];
            if (_output is not null)
            {
                item.ChangeType(_output);
            }

            double loss = item.Mass * (1.0 - Yield);
            if (loss > 0.0)
            {
                item.Mass -= loss;
                _lost += loss;
            }
        }

        Transition(
            ProcessPhase.Discharging,
            string.Create(CultureInfo.InvariantCulture, $"Hold satisfied after {_elapsed:F2} s; discharging {_items.Count} items."),
            in ctx);
    }

    private void Transition(ProcessPhase next, string message, in TickContext ctx)
    {
        _phase = next;
        ctx.Log(Id, next.ToString().ToUpperInvariant(), message);
    }

    private bool AllSatisfied()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            ItemInstance item = _items[i];
            if (!_hold.IsSatisfied(_elapsed, item.Properties, item.State))
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyTransforms(double dt)
    {
        if (_transforms.Length == 0)
        {
            return;
        }

        var context = new TransformContext(AmbientTemperature.Value);
        for (int i = 0; i < _items.Count; i++)
        {
            ItemInstance item = _items[i];
            MaterialProperties properties = item.Properties;
            foreach (IMaterialTransform transform in _transforms)
            {
                transform.Apply(ref properties, item.State, dt, in context);
            }

            item.Properties = properties;
        }
    }
}
