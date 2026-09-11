using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Telemetry;
using Dse.Core.Validation;

namespace Dse.Core.Flow;

/// <summary>
/// Bulk transport as an array of cells. Each tick a fraction v·dt/cellSize of
/// every cell moves to its neighbour (Eulerian advection), resolved from the
/// head backwards so a blocked discharge builds load along the belt instead of
/// overfilling one cell. Stopping the belt freezes the load profile in place;
/// restarting resumes it. Diffusion is tunable by cell size.
/// </summary>
public sealed class BulkBelt : FlowComponentBase, IBulkProducer, IBulkConsumer
{
    private readonly BulkLot[] _cells;
    private readonly double _cellCapacity;
    private double _dt;
    private TelemetryHandle _loadTelemetry;

    public BulkBelt(string id, double length, double cellSize, double maxSpeed, double maxLinearDensity)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpeed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLinearDensity);

        double cells = length / cellSize;
        int cellCount = (int)Math.Round(cells);
        if (cellCount < 1 || Math.Abs(cells - cellCount) > 1e-9)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Length {length} m is not a whole number of {cellSize} m cells."),
                nameof(cellSize));
        }

        Length = length;
        CellSize = cellSize;
        MaxSpeed = maxSpeed;
        MaxLinearDensity = maxLinearDensity;
        _cellCapacity = maxLinearDensity * cellSize;
        _cells = new BulkLot[cellCount];

        In = AddInlet("In", PayloadKind.Bulk);
        Out = AddOutlet("Out", PayloadKind.Bulk);
        Speed = AddInput<double>("Speed");
        Load = AddOutput<double>("Load");
        PeakLinearDensity = AddOutput<double>("PeakLinearDensity");
    }

    public FlowInlet In { get; }

    public FlowOutlet Out { get; }

    /// <summary>Belt speed in m/s. Unconnected reads zero: a belt with no drive does not move.</summary>
    public InputPort<double> Speed { get; }

    /// <summary>Total mass on the belt, kg, as of the last evaluate.</summary>
    public OutputPort<double> Load { get; }

    /// <summary>The fullest cell's linear density, kg/m, as of the last evaluate.</summary>
    public OutputPort<double> PeakLinearDensity { get; }

    public double Length { get; }

    public double CellSize { get; }

    public double MaxSpeed { get; }

    /// <summary>kg/m. A cell never holds more than this times the cell size.</summary>
    public double MaxLinearDensity { get; }

    public int CellCount => _cells.Length;

    /// <summary>The cells from tail (index 0, where material arrives) to head.</summary>
    public ReadOnlySpan<BulkLot> Cells => _cells;

    public override double MassHeld
    {
        get
        {
            double total = 0.0;
            for (int i = 0; i < _cells.Length; i++)
            {
                total += _cells[i].Mass;
            }

            return total;
        }
    }

    /// <summary>Linear density, kg/m, of the cell under <paramref name="position"/> metres from the tail.</summary>
    public double LinearDensityAt(double position)
    {
        int index = Math.Clamp((int)(position / CellSize), 0, _cells.Length - 1);
        return _cells[index].Mass / CellSize;
    }

    public override void Initialize(in InitContext ctx)
    {
        _dt = ctx.Dt;
        _loadTelemetry = ctx.RegisterTelemetry("Load", "kg");
    }

    public override void Evaluate(in TickContext ctx)
    {
        double load = 0.0;
        double peak = 0.0;
        for (int i = 0; i < _cells.Length; i++)
        {
            double mass = _cells[i].Mass;
            load += mass;
            peak = Math.Max(peak, mass);
        }

        Load.Value = load;
        PeakLinearDensity.Value = peak / CellSize;
        _loadTelemetry.Write(load);
    }

    public override IEnumerable<ValidationError> ValidateFlow(double dt)
    {
        double minimumCell = MaxSpeed * dt;
        if (CellSize < minimumCell)
        {
            yield return new ValidationError(
                "DSE006",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Belt '{Id}': cell size {CellSize} m is smaller than maxSpeed × dt = " +
                    $"{minimumCell} m, so a cell could empty in less than one tick. Use cells of " +
                    $"at least {minimumCell} m, lower maxSpeed, or shorten the time step."),
                [Id]);
        }
    }

    public double AcceptMass(FlowInlet inlet) => Math.Max(0.0, _cellCapacity - _cells[0].Mass);

    public void Deposit(FlowInlet inlet, in BulkLot lot) => _cells[0] = _cells[0].Merge(lot);

    public double OfferMass(FlowOutlet outlet) => Fraction(_dt) * _cells[^1].Mass;

    public BulkLot Withdraw(FlowOutlet outlet, double mass)
    {
        BulkLot taken = _cells[^1].Take(mass, out BulkLot remaining);
        _cells[^1] = remaining;
        return taken;
    }

    public override void Advance(double dt)
    {
        double fraction = Fraction(dt);
        if (fraction <= 0.0)
        {
            return;
        }

        // Head first: each cell moves into the room its neighbour has left.
        for (int i = _cells.Length - 2; i >= 0; i--)
        {
            double wanted = fraction * _cells[i].Mass;
            double room = _cellCapacity - _cells[i + 1].Mass;
            double moving = Math.Min(wanted, room);
            if (moving <= 0.0)
            {
                continue;
            }

            BulkLot taken = _cells[i].Take(moving, out BulkLot remaining);
            _cells[i] = remaining;
            _cells[i + 1] = _cells[i + 1].Merge(taken);
        }
    }

    private double Fraction(double dt)
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
                    $"Raise maxSpeed (and re-check the CFL condition) or limit the drive."));
        }

        return speed * dt / CellSize;
    }
}
