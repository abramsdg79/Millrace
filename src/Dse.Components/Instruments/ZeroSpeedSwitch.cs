using System.Globalization;
using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Components.Instruments;

/// <summary>
/// A speed switch: <see cref="Stopped"/> goes true once the *reading* has
/// been below the threshold for the delay, and false the moment it is not.
/// Derived from the faulted reading, so a frozen switch never notices a stop.
/// </summary>
public sealed class ZeroSpeedSwitch : InstrumentBase
{
    public static ComponentDescriptor Descriptor { get; } = new(
        "zero-speed-switch",
        ComponentCategory.Instrumentation,
        "Asserts Stopped when the measured speed has stayed below a threshold for a delay.",
        (id, p) => new ZeroSpeedSwitch(id, InstrumentCatalogue.ReadSpec(p.Group("spec")), p.Double("thresholdSpeed"), p.Double("delaySeconds")))
    {
        Parameters =
        [
            Param.Group("spec", "Unit, range, noise and lag.", InstrumentCatalogue.Spec),
            Param.Double("thresholdSpeed", "Speed below which the belt counts as stopped, in the spec's unit.", min: 0.0),
            Param.Double("delaySeconds", "How long the speed must stay below the threshold.", "s", min: 0.0),
        ],
        Ports = [PortSpec.In<double>("Speed", description: "The true speed."), .. InstrumentCatalogue.Outputs, PortSpec.Out<bool>("Stopped")],
        Faults = InstrumentFaults.All,
        Tags = [InstrumentCatalogue.ValueTag, new TagEntry("Stopped", TagKind.Bool, TagAccess.ReadOnly)],
        Telemetry = [InstrumentCatalogue.Truth],
    };

    private double _belowFor;
    private bool _stopped;

    public ZeroSpeedSwitch(string id, InstrumentSpec spec, double thresholdSpeed, double delaySeconds)
        : base(id, spec)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdSpeed);
        ArgumentOutOfRangeException.ThrowIfNegative(delaySeconds);
        ThresholdSpeed = thresholdSpeed;
        DelaySeconds = delaySeconds;

        Speed = AddInput<double>("Speed");
        Stopped = AddOutput<bool>("Stopped");
    }

    /// <summary>True speed, m/s.</summary>
    public InputPort<double> Speed { get; }

    public OutputPort<bool> Stopped { get; }

    /// <summary>m/s.</summary>
    public double ThresholdSpeed { get; }

    /// <summary>s the reading must stay below the threshold.</summary>
    public double DelaySeconds { get; }

    protected override string ValueDescription => "Monitored speed";

    public override IEnumerable<TagBinding> DescribeTags() =>
        base.DescribeTags().Append(TagBinding.Read("Stopped", Stopped, "Below the threshold for the delay"));

    protected override double Measure(in TickContext ctx) => Speed.Value;

    protected override void OnEvaluated(double reading, in TickContext ctx)
    {
        bool below = reading < ThresholdSpeed;
        _belowFor = below ? _belowFor + ctx.Dt : 0.0;
        bool stopped = below && _belowFor >= DelaySeconds - 1e-12;

        if (stopped != _stopped)
        {
            ctx.Log(
                Id,
                stopped ? "ZERO_SPEED" : "MOTION",
                stopped
                    ? string.Create(CultureInfo.InvariantCulture, $"Speed below {ThresholdSpeed} m/s for {DelaySeconds} s.")
                    : "Motion detected.");
        }

        _stopped = stopped;
        Stopped.Value = stopped;
    }
}
