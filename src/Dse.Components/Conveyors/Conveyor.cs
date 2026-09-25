using System.Globalization;
using Dse.Components.Instruments;
using Dse.Components.Mechanical;
using Dse.Components.Safety;
using Dse.Core.Catalogue;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Io;

namespace Dse.Components.Conveyors;

/// <summary>
/// A bulk conveyor: motor, gearbox, drive and tail pulleys, belt friction,
/// belt, speed sensor, belt scale, current sensor, zero-speed switch,
/// pull-keys, e-stop, safety relay and starter, wired the way a real one is. Nothing here is
/// special to the engine; it is a composition, and it flattens to leaves at
/// build time. The causal chain — a blocked discharge loads the belt, raises
/// torque, raises current, heats the motor, trips the overload — is a
/// consequence of the wiring, not a rule written anywhere.
/// </summary>
public sealed class Conveyor : CompositeComponent, ICapabilityProvider
{
    private const double KgPerSecondToTonnesPerHour = 3.6;

    private static readonly PortRepeat PerPullKey = new("pullKeys");

    public static ComponentDescriptor Descriptor { get; } = new(
        "conveyor",
        ComponentCategory.Conveyor,
        "A complete belt conveyor: motor, gearbox, drive and tail pulleys, belt friction, belt, speed sensor, belt scale, " +
        "current sensor, zero-speed switch, e-stop, pull-keys, safety relay and starter, wired and ready. Faults are injected " +
        "on the leaves inside it, addressed as <id>.Motor, <id>.Drive, <id>.Scale and so on.",
        (id, p) => new Conveyor(id, new ConveyorOptions(
            LengthM: p.Double("lengthM"),
            CellSizeM: p.Double("cellSizeM"),
            BeltWidthM: p.Double("beltWidthM"),
            AngleOfReposeDeg: p.Double("angleOfReposeDeg"),
            MaterialDensityKgM3: p.Double("materialDensityKgM3"),
            EmptyBeltMassKg: p.Double("emptyBeltMassKg"),
            FrictionCoefficient: p.Double("frictionCoefficient"),
            PulleyDiameterM: p.Double("pulleyDiameterM"),
            GearRatio: p.Double("gearRatio"),
            Motor: MotorRatingGroup.Read(p.Group("motor")),
            TailDragN: p.Double("tailDragN"),
            PullKeys: p.Int("pullKeys"),
            SpeedMarginFraction: p.Double("speedMarginFraction"))))
    {
        Parameters =
        [
            Param.Double("lengthM", "Belt length; must be a whole number of cells.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("cellSizeM", "Length of one belt cell. The belt may not advance more than one cell per tick (DSE006).", "m", min: 0.0, exclusiveMin: true),
            Param.Double("beltWidthM", "Belt width; with the angle of repose and density it sets how much a metre of belt can carry.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("angleOfReposeDeg", "Surcharge angle of the material on the belt.", "°", min: 0.0),
            Param.Double("materialDensityKgM3", "Bulk density used to size the belt's capacity.", "kg/m³", min: 0.0, exclusiveMin: true),
            Param.Double("emptyBeltMassKg", "Mass of the moving belt and idlers with no load.", "kg", min: 0.0),
            Param.Double("frictionCoefficient", "Rolling resistance coefficient.", min: 0.0),
            Param.Double("pulleyDiameterM", "Drive pulley diameter.", "m", min: 0.0, exclusiveMin: true),
            Param.Double("gearRatio", "Motor speed over pulley speed.", min: 0.0, exclusiveMin: true),
            Param.Group("motor", "The drive motor's rating.", MotorRatingGroup.Definition),
            Param.Double("tailDragN", "Tail pulley bearing drag.", "N", @default: 50.0, min: 0.0),
            Param.Int("pullKeys", "Number of pull-wire switches along the belt.", "count", @default: 2, min: 0),
            Param.Double("speedMarginFraction", "How far the belt's declared maximum speed exceeds the no-load speed.", @default: 0.1, min: 0.0),
        ],
        Ports =
        [
            PortSpec.In<bool>("Start", description: "Run command to the starter."),
            PortSpec.In<bool>("Permit", description: "Run permit to the starter; false holds the contactor open. Defaults to true."),
            PortSpec.In<bool>("Reset", description: "Overload reset, rising edge."),
            PortSpec.In<bool>("SafetyReset", description: "Safety relay reset, rising edge."),
            PortSpec.In<bool>("EStop", description: "The e-stop is pressed."),
            PortSpec.In<bool>("PullKey{n}", description: "A pull-key is pulled.", repeat: PerPullKey),
            PortSpec.Out<double>("Speed", "m/s", "Measured belt speed."),
            PortSpec.Out<double>("TonnesPerHour", "t/h", "Measured mass flow at the scale."),
            PortSpec.Out<double>("Current", "A", "Measured motor current."),
            PortSpec.Out<bool>("Stopped", description: "Zero-speed switch."),
            PortSpec.Out<bool>("Contactor"),
            PortSpec.Out<bool>("Tripped"),
            PortSpec.Out<bool>("SafetyOk"),
        ],
        FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk, "The tail."), PortSpec.Outlet("Out", PayloadKind.Bulk, "The head.")],
        Tags =
        [
            new TagEntry("Start", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Permit", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("Reset", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("SafetyReset", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("EStop", TagKind.Bool, TagAccess.ReadWrite),
            new TagEntry("PullKey{n}", TagKind.Bool, TagAccess.ReadWrite, Repeat: PerPullKey),
            new TagEntry("Speed", TagKind.Double, TagAccess.ReadOnly, "m/s"),
            new TagEntry("TonnesPerHour", TagKind.Double, TagAccess.ReadOnly, "t/h"),
            new TagEntry("Current", TagKind.Double, TagAccess.ReadOnly, "A"),
            new TagEntry("Stopped", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("Contactor", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("Tripped", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("SafetyOk", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("ZeroSpeed.Value", TagKind.Double, TagAccess.ReadOnly, "m/s"),
            new TagEntry("EStop.Ok", TagKind.Bool, TagAccess.ReadOnly),
            new TagEntry("PullKey{n}.Ok", TagKind.Bool, TagAccess.ReadOnly, Repeat: PerPullKey),
        ],
        Telemetry =
        [
            new TelemetryKey("Motor.Speed", "rad/s"),
            new TelemetryKey("Motor.Current", "A"),
            new TelemetryKey("Motor.ThermalState"),
            new TelemetryKey("Belt.Load", "kg"),
            new TelemetryKey("SpeedSensor.Truth"),
            new TelemetryKey("Scale.Truth"),
            new TelemetryKey("CurrentSensor.Truth"),
            new TelemetryKey("ZeroSpeed.Truth"),
        ],
        Provides = [typeof(IMaterialObservable)],
    };

    public Conveyor(string id, ConveyorOptions options)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.PullKeys, nameof(options));
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
        Expose("Permit", Starter.Permit);
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

    /// <summary>The belt stands in for the conveyor wherever something observes material.</summary>
    public bool TryGetCapability(Type capability, out object? instance)
    {
        ArgumentNullException.ThrowIfNull(capability);
        instance = capability.IsInstanceOfType(Belt) ? Belt : null;
        return instance is not null;
    }
}
