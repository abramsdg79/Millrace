using System.Globalization;
using Dse.Components.Instruments;
using Dse.Components.Mechanical;
using Dse.Components.Safety;
using Dse.Core.Flow;
using Dse.Core.Graph;

namespace Dse.Components.Conveyors;

/// <summary>
/// A bulk conveyor: motor, gearbox, drive and tail pulleys, belt, speed
/// sensor, belt scale, current sensor, zero-speed switch, pull-keys, e-stop,
/// safety relay and starter, wired the way a real one is. Nothing here is
/// special to the engine; it is a composition, and it flattens to leaves at
/// build time. The causal chain — a blocked discharge loads the belt, raises
/// torque, raises current, heats the motor, trips the overload — is a
/// consequence of the wiring, not a rule written anywhere.
/// </summary>
public sealed class Conveyor : CompositeComponent
{
    private const double KgPerSecondToTonnesPerHour = 3.6;

    public Conveyor(string id, ConveyorOptions options)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PullKeys, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegative(options.SpeedMarginFraction, nameof(options));

        double radius = options.PulleyDiameterM / 2.0;
        double noLoadSpeed = options.Motor.RatedSpeedRadPerS / options.GearRatio * radius;
        double maxSpeed = noLoadSpeed * (1.0 + options.SpeedMarginFraction);
        double maxLinearDensity = BeltGeometry.MaxLinearDensity(
            options.BeltWidthM, options.AngleOfReposeDeg, options.MaterialDensityKgM3);

        Motor = AddChild(new Motor("Motor", options.Motor));
        Gearbox = AddChild(new Gearbox("Gearbox", options.GearRatio));
        Drive = AddChild(new DrivePulley("Drive", options.PulleyDiameterM));
        Tail = AddChild(new TailPulley("Tail", options.TailDragN));
        Friction = AddChild(new BeltFriction("Friction", options.EmptyBeltMassKg, options.FrictionCoefficient));
        Belt = AddChild(new BulkBelt("Belt", options.LengthM, options.CellSizeM, maxSpeed, maxLinearDensity));
        SpeedSensor = AddChild(new SpeedSensor("SpeedSensor", new InstrumentSpec("m/s", 0.0, maxSpeed * 1.2, NoiseSigma: 0.002)));
        Scale = AddChild(new BeltScale(
            "Scale",
            Belt,
            options.LengthM / 2.0,
            new InstrumentSpec("t/h", 0.0, maxLinearDensity * maxSpeed * KgPerSecondToTonnesPerHour, NoiseSigma: 0.3, LagSeconds: 1.0)));
        CurrentSensor = AddChild(new CurrentSensor("CurrentSensor", new InstrumentSpec("A", 0.0, options.Motor.RatedCurrentA * 8.0, NoiseSigma: 0.01)));
        ZeroSpeed = AddChild(new ZeroSpeedSwitch("ZeroSpeed", new InstrumentSpec("m/s", 0.0, maxSpeed * 1.2), thresholdSpeed: 0.02, delaySeconds: 1.0));
        EStop = AddChild(new EStop("EStop"));
        Safety = AddChild(new SafetyRelay("Safety", options.PullKeys + 1));
        Starter = AddChild(new MotorStarter("Starter"));

        // Starter.Contactor and Motor.ThermalState close a genuine algebraic
        // loop (contactor gates the motor's energisation, which the thermal
        // state depends on; the thermal state gates the contactor). The
        // thermal path must stay immediate — the overload trip has to land on
        // the tick the thermal state crosses the trip level — so the
        // contactor's reflection into the motor is the one tick this composite
        // delays, the wiring-time tool for a loop neither component
        // anticipated (see "Latched inputs" in docs/architecture.md). A closed
        // or opened contactor reaching the motor 10 ms late is physically
        // irrelevant.
        var contactorDelay = AddChild(new UnitDelay<bool>("ContactorDelay"));

        var keys = new PullKey[options.PullKeys];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = AddChild(new PullKey(string.Create(CultureInfo.InvariantCulture, $"PullKey{i + 1}")));
        }

        PullKeys = keys;

        // Power and drive.
        Starter.Contactor.ConnectTo(contactorDelay.In);
        contactorDelay.Out.ConnectTo(Motor.Energised);
        Motor.Speed.ConnectTo(Gearbox.InputSpeed);
        Gearbox.OutputSpeed.ConnectTo(Drive.ShaftSpeed);
        Drive.BeltSpeed.ConnectTo(Belt.Speed);

        // Reflected load, back to the motor (latched at the gearbox and the motor).
        Belt.Load.ConnectTo(Friction.Load);
        Tail.Drag.ConnectTo(Friction.Drag);
        Friction.Force.ConnectTo(Drive.BeltForce);
        Drive.TorqueDemand.ConnectTo(Gearbox.OutputTorqueDemand);
        Gearbox.InputTorqueDemand.ConnectTo(Motor.TorqueDemand);

        // Protection.
        Motor.ThermalState.ConnectTo(Starter.ThermalState);
        Safety.Ok.ConnectTo(Starter.SafetyOk);
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i].Ok.ConnectTo(Safety.Channel(i + 1));
        }

        EStop.Ok.ConnectTo(Safety.Channel(keys.Length + 1));

        // Instruments.
        Drive.BeltSpeed.ConnectTo(SpeedSensor.Speed);
        Drive.BeltSpeed.ConnectTo(Scale.Speed);
        Drive.BeltSpeed.ConnectTo(ZeroSpeed.Speed);
        Motor.Current.ConnectTo(CurrentSensor.Current);

        // The composite's face.
        Expose("In", Belt.In);
        Expose("Out", Belt.Out);
        Expose("Start", Starter.Command);
        Expose("Reset", Starter.Reset);
        Expose("SafetyReset", Safety.Reset);
        Expose("EStop", EStop.Actuated);
        for (int i = 0; i < keys.Length; i++)
        {
            Expose(string.Create(CultureInfo.InvariantCulture, $"PullKey{i + 1}"), keys[i].Actuated);
        }

        Expose("Speed", SpeedSensor.Value);
        Expose("TonnesPerHour", Scale.Value);
        Expose("Current", CurrentSensor.Value);
        Expose("Stopped", ZeroSpeed.Stopped);
        Expose("Contactor", Starter.Contactor);
        Expose("Tripped", Starter.Tripped);
        Expose("SafetyOk", Safety.Ok);
    }

    public Motor Motor { get; }

    public Gearbox Gearbox { get; }

    public DrivePulley Drive { get; }

    public TailPulley Tail { get; }

    public BeltFriction Friction { get; }

    public BulkBelt Belt { get; }

    public SpeedSensor SpeedSensor { get; }

    public BeltScale Scale { get; }

    public CurrentSensor CurrentSensor { get; }

    public ZeroSpeedSwitch ZeroSpeed { get; }

    public EStop EStop { get; }

    public IReadOnlyList<PullKey> PullKeys { get; }

    public SafetyRelay Safety { get; }

    public MotorStarter Starter { get; }
}
