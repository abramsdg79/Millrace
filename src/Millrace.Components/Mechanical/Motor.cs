using System.Globalization;
using Millrace.Core.Catalogue;
using Millrace.Core.Contexts;
using Millrace.Core.Faults;
using Millrace.Core.Graph;
using Millrace.Core.Telemetry;

namespace Millrace.Components.Mechanical;

/// <summary>
/// An induction motor as a causal model: torque demand sets current, current
/// heats an I²t thermal state, and the starter's overload relay trips on that
/// state. "Inject motor overload" is a bias on the thermal state or the load;
/// the trip that follows is the same trip that material accumulation causes
/// unaided. The torque input is latched — it is a reflection of the load one
/// tick ago — so a drivetrain needs no explicit delay to be solvable.
/// </summary>
public sealed class Motor : ComponentBase, IFaultTarget
{
    /// <summary>Extra torque the motor must overcome, N·m.</summary>
    public const string BearingFriction = "bearing-friction";

    /// <summary>A one-shot step added to the thermal state.</summary>
    public const string ThermalBias = "thermal-bias";

    private static readonly FaultDescriptor[] Faults =
    [
        new(BearingFriction, "Extra torque the motor must overcome, as a worn bearing adds.",
            new FaultParameter("torque", "N·m", 0.0, "Added to the torque demand while active.")),
        new(ThermalBias, "A step in the thermal state, as a hot start or a blocked fan gives.",
            new FaultParameter("amount", "", 0.5, "Added once, on injection; clearing has no effect, the state decays on its own.")),
    ];

    public static ComponentDescriptor Descriptor { get; } = new(
        "motor",
        ComponentCategory.Mechanical,
        "An induction motor: first-order speed response, current from torque, an I²t thermal state and a breakdown stall.",
        (id, p) => new Motor(id, MotorRatingGroup.Read(p.Group("rating"))))
    {
        Parameters = [Param.Group("rating", "The nameplate and model constants.", MotorRatingGroup.Definition)],
        Ports =
        [
            PortSpec.In<bool>("Energised", description: "The contactor feeding the motor is closed."),
            PortSpec.In<double>("TorqueDemand", "N·m", "Load torque reflected to the shaft. Latched: read as it stood at the end of the previous tick."),
            PortSpec.Out<double>("Speed", "rad/s"),
            PortSpec.Out<double>("Torque", "N·m"),
            PortSpec.Out<double>("Current", "A"),
            PortSpec.Out<double>("ThermalState", description: "I²t state; 1.0 is the rated continuous limit."),
            PortSpec.Out<bool>("AtSpeed"),
        ],
        Faults = Faults,
        Telemetry = [new TelemetryKey("Speed", "rad/s"), new TelemetryKey("Current", "A"), new TelemetryKey("ThermalState")],
    };

    private readonly MotorRating _rating;
    private double _speed;
    private double _thermal;
    private double _friction;
    private bool _wasEnergised;
    private bool _atSpeed;
    private bool _stalled;
    private bool _stopped = true;
    private TelemetryHandle _speedTelemetry;
    private TelemetryHandle _currentTelemetry;
    private TelemetryHandle _thermalTelemetry;

    public Motor(string id, MotorRating rating)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.RatedPowerW, nameof(rating));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.RatedSpeedRadPerS, nameof(rating));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.RatedCurrentA, nameof(rating));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rating.ThermalTimeConstantS, nameof(rating));
        _rating = rating;

        Energised = AddInput<bool>("Energised");
        TorqueDemand = AddInput<double>("TorqueDemand", latched: true);
        Speed = AddOutput<double>("Speed");
        Torque = AddOutput<double>("Torque");
        Current = AddOutput<double>("Current");
        ThermalState = AddOutput<double>("ThermalState");
        AtSpeed = AddOutput<bool>("AtSpeed");
    }

    /// <summary>Contactor closed. Unconnected reads false.</summary>
    public InputPort<bool> Energised { get; }

    /// <summary>Torque the load reflects onto the shaft, N·m. Latched: read one tick late.</summary>
    public InputPort<double> TorqueDemand { get; }

    /// <summary>Shaft speed, rad/s.</summary>
    public OutputPort<double> Speed { get; }

    /// <summary>Torque delivered, N·m. Zero when stalled or de-energised.</summary>
    public OutputPort<double> Torque { get; }

    /// <summary>Line current, A.</summary>
    public OutputPort<double> Current { get; }

    /// <summary>I²t thermal state; 1.0 is equilibrium at rated current.</summary>
    public OutputPort<double> ThermalState { get; }

    /// <summary>
    /// True once within 5 % of the target speed. Clears on a stall and sets
    /// again on recovery: a stall is a genuine loss of speed, so a run-up
    /// that stalls and then recovers reaches at-speed twice.
    /// </summary>
    public OutputPort<bool> AtSpeed { get; }

    public MotorRating Rating => _rating;

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Initialize(in InitContext ctx)
    {
        _speedTelemetry = ctx.RegisterTelemetry("Speed", "rad/s");
        _currentTelemetry = ctx.RegisterTelemetry("Current", "A");
        _thermalTelemetry = ctx.RegisterTelemetry("ThermalState", "");
    }

    public override void Evaluate(in TickContext ctx)
    {
        double dt = ctx.Dt;
        bool energised = Energised.Value;
        double demand = Math.Max(0.0, TorqueDemand.Value) + _friction;
        double ratedTorque = _rating.RatedTorque;
        double load = Math.Min(demand / ratedTorque, _rating.BreakdownTorqueMultiple);
        double ratedCurrent = _rating.RatedCurrentA;

        double current;
        double torque;
        if (energised)
        {
            if (!_wasEnergised)
            {
                ctx.Log(Id, "ENERGISED", "Contactor closed.");
                _atSpeed = false;
                _stopped = false;
            }

            bool stalled = demand > _rating.BreakdownTorqueMultiple * ratedTorque;
            double target = stalled ? 0.0 : _rating.RatedSpeedRadPerS * (1.0 - (_rating.SpeedDroopFraction * load));
            _speed += (target - _speed) * Math.Min(1.0, dt / _rating.AccelerationTimeConstantS);

            double running = ratedCurrent * (_rating.NoLoadCurrentFraction + ((1.0 - _rating.NoLoadCurrentFraction) * load));
            double start = target > 0.0 ? Math.Max(0.0, 1.0 - (_speed / target)) : 1.0;
            current = Math.Max(running, _rating.LockedRotorCurrentMultiple * ratedCurrent * start);
            torque = stalled ? 0.0 : demand;

            if (stalled && !_stalled)
            {
                string breakdown = (_rating.BreakdownTorqueMultiple * ratedTorque).ToString("F0", CultureInfo.InvariantCulture);
                ctx.Log(Id, "STALLED",
                    $"Torque demand {ThresholdText.Exceeding(demand, breakdown, 0)} N·m exceeds breakdown torque {breakdown} N·m.");
            }

            _stalled = stalled;

            // Logged once per run-up: on the initial approach to speed after
            // energising, and again after a stall clears _atSpeed below and
            // the motor recovers — a stall is a genuine loss of speed.
            if (!_atSpeed && target > 0.0 && _speed >= 0.95 * target)
            {
                _atSpeed = true;
                ctx.Log(Id, "AT_SPEED", string.Create(CultureInfo.InvariantCulture, $"Reached {_speed:F1} rad/s."));
            }

            if (stalled)
            {
                _atSpeed = false;
            }
        }
        else
        {
            if (_wasEnergised)
            {
                ctx.Log(Id, "DE_ENERGISED", "Contactor open; coasting.");
            }

            _speed += (0.0 - _speed) * Math.Min(1.0, dt / _rating.CoastTimeConstantS);
            current = 0.0;
            torque = 0.0;
            _atSpeed = false;
            _stalled = false;

            if (!_stopped && _speed < 0.005 * _rating.RatedSpeedRadPerS)
            {
                _stopped = true;
                ctx.Log(Id, "STOPPED", "Shaft at rest.");
            }
        }

        double ratio = current / ratedCurrent;
        _thermal += ((ratio * ratio) - _thermal) * Math.Min(1.0, dt / _rating.ThermalTimeConstantS);

        _wasEnergised = energised;
        Speed.Value = _speed;
        Torque.Value = torque;
        Current.Value = current;
        ThermalState.Value = _thermal;
        AtSpeed.Value = _atSpeed;
        _speedTelemetry.Write(_speed);
        _currentTelemetry.Write(current);
        _thermalTelemetry.Write(_thermal);
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case BearingFriction:
                _friction = Math.Max(0.0, arguments.Get("torque"));
                break;
            case ThermalBias:
                _thermal = Math.Max(0.0, _thermal + arguments.Get("amount"));
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        if (faultId == BearingFriction)
        {
            _friction = 0.0;
        }
    }
}
