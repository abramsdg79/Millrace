using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Graph;

namespace Dse.Core.Flow;

/// <summary>
/// Discrete transport: each item carries a continuous position advanced by
/// v·dt — no cells, no diffusion, exact. An item that reaches the head and
/// cannot discharge waits there and followers queue behind it at the minimum
/// spacing; the tail accepts a new item once the last one has moved that far.
/// Transforms run on every item each tick, before positions move.
/// </summary>
public sealed class DiscreteBelt : FlowComponentBase, IItemProducer, IItemConsumer
{
    private const double HeadTolerance = 1e-9;

    // Index 0 is nearest the head; items board at the end.
    private readonly List<CarriedItem> _items = [];
    private readonly IMaterialTransform[] _transforms;

    public DiscreteBelt(
        string id,
        double length,
        double maxSpeed,
        double minSpacing = 0.0,
        IReadOnlyList<IMaterialTransform>? transforms = null)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpeed);
        ArgumentOutOfRangeException.ThrowIfNegative(minSpacing);
        if (minSpacing > length)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Minimum spacing {minSpacing} m exceeds the belt length {length} m."),
                nameof(minSpacing));
        }

        Length = length;
        MaxSpeed = maxSpeed;
        MinSpacing = minSpacing;
        _transforms = transforms is null ? [] : transforms.ToArray();

        In = AddInlet("In", PayloadKind.Discrete);
        Out = AddOutlet("Out", PayloadKind.Discrete);
        Speed = AddInput<double>("Speed");
        AmbientTemperature = AddInput<double>("AmbientTemperature", defaultValue: 20.0);
        ItemCount = AddOutput<int>("ItemCount");
    }

    /// <summary>An item on the belt and its distance from the tail, in metres.</summary>
    public readonly record struct CarriedItem(ItemInstance Item, double Position);

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Belt speed in m/s. Unconnected reads zero.</summary>
    public InputPort<double> Speed { get; }

    /// <summary>Ambient temperature handed to the transforms, °C. Unconnected reads 20 °C.</summary>
    public InputPort<double> AmbientTemperature { get; }

    /// <summary>Items on the belt, as of the last evaluate.</summary>
    public OutputPort<int> ItemCount { get; }

    public double Length { get; }

    public double MaxSpeed { get; }

    public double MinSpacing { get; }

    /// <summary>Items from head to tail.</summary>
    public IReadOnlyList<CarriedItem> Items => _items;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            for (int i = 0; i < _items.Count; i++)
            {
                total += _items[i].Item.Mass;
            }

            return total;
        }
    }

    public override void Evaluate(in TickContext ctx) => ItemCount.Value = _items.Count;

    public bool CanAcceptItem(FlowInlet inlet, ItemInstance item) =>
        _items.Count == 0 || _items[^1].Position >= MinSpacing;

    public void DepositItem(FlowInlet inlet, ItemInstance item) => _items.Add(new CarriedItem(item, 0.0));

    public bool TryPeekItem(FlowOutlet outlet, [NotNullWhen(true)] out ItemInstance? item)
    {
        if (_items.Count > 0 && _items[0].Position >= Length - HeadTolerance)
        {
            item = _items[0].Item;
            return true;
        }

        item = null;
        return false;
    }

    public ItemInstance WithdrawItem(FlowOutlet outlet)
    {
        if (!TryPeekItem(outlet, out ItemInstance? item))
        {
            throw new InvalidOperationException($"Belt '{Id}' has no item at its head to withdraw.");
        }

        _items.RemoveAt(0);
        return item;
    }

    public override void Advance(double dt)
    {
        double step = SpeedOrThrow() * dt;
        ApplyTransforms(dt);

        for (int i = 0; i < _items.Count; i++)
        {
            double limit = i == 0 ? Length : _items[i - 1].Position - MinSpacing;
            double current = _items[i].Position;
            double next = Math.Min(current + step, Math.Max(current, limit));
            _items[i] = _items[i] with { Position = next };
        }
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
            ItemInstance item = _items[i].Item;
            MaterialProperties properties = item.Properties;
            foreach (IMaterialTransform transform in _transforms)
            {
                transform.Apply(ref properties, item.State, dt, in context);
            }

            item.Properties = properties;
        }
    }

    private double SpeedOrThrow()
    {
        double speed = Speed.Value;
        if (speed < 0.0)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}' speed {speed} m/s is negative. Reversing belts are not modelled."));
        }

        if (speed > MaxSpeed)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}' speed {speed} m/s exceeds its declared maxSpeed {MaxSpeed} m/s. " +
                    $"Raise maxSpeed or limit the drive."));
        }

        return speed;
    }
}
