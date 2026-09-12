using System.Globalization;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Graph;

namespace Dse.Components.Mechanical;

/// <summary>
/// A direct-on-line starter: a contactor and a thermal overload relay. The
/// contactor closes on command when the safety circuit allows and the relay
/// is not tripped. The relay trips when the motor's thermal state crosses
/// the trip level and resets on a reset edge once it has cooled below the
/// reset level. The safety input bypasses everything: a dropped safety relay
/// opens the contactor with no controller involved.
/// </summary>
public sealed class MotorStarter : ComponentBase, IFaultTarget
{
    /// <summary>The contactor is welded closed.</summary>
    public const string ContactorWelded = "contactor-welded";

    /// <summary>The contactor cannot close.</summary>
    public const string ContactorOpen = "contactor-open";

    private static readonly FaultDescriptor[] Faults =
    [
        new(ContactorWelded, "The contactor is welded closed; the motor stays energised whatever the logic says."),
        new(ContactorOpen, "The contactor coil or contacts have failed; the motor cannot be energised."),
    ];

    private bool _tripped;
    private bool _closed;
    private bool _wasReset;
    private bool _welded;
    private bool _open;

    public MotorStarter(string id, double tripLevel = 1.1, double resetLevel = 0.9)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tripLevel);
        if (resetLevel >= tripLevel)
        {
            throw new ArgumentException("The reset level must be below the trip level.", nameof(resetLevel));
        }

        TripLevel = tripLevel;
        ResetLevel = resetLevel;

        Command = AddInput<bool>("Command");
        SafetyOk = AddInput<bool>("SafetyOk", defaultValue: true);
        ThermalState = AddInput<double>("ThermalState");
        Reset = AddInput<bool>("Reset");
        Contactor = AddOutput<bool>("Contactor");
        Tripped = AddOutput<bool>("Tripped");
    }

    /// <summary>Thermal state at which the overload relay trips.</summary>
    public double TripLevel { get; }

    /// <summary>Thermal state below which a reset is accepted.</summary>
    public double ResetLevel { get; }

    /// <summary>Run command from a controller or an operator. Unconnected reads false.</summary>
    public InputPort<bool> Command { get; }

    /// <summary>From the safety relay. Unconnected reads true.</summary>
    public InputPort<bool> SafetyOk { get; }

    /// <summary>The motor's thermal state.</summary>
    public InputPort<double> ThermalState { get; }

    /// <summary>Rising edge resets the overload relay if cooled.</summary>
    public InputPort<bool> Reset { get; }

    /// <summary>True energises the motor.</summary>
    public OutputPort<bool> Contactor { get; }

    public OutputPort<bool> Tripped { get; }

    public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

    public override void Evaluate(in TickContext ctx)
    {
        double thermal = ThermalState.Value;
        bool reset = Reset.Value;
        bool resetEdge = reset && !_wasReset;
        _wasReset = reset;

        if (!_tripped && thermal >= TripLevel)
        {
            _tripped = true;
            ctx.Log(Id, "OVERLOAD_TRIP", string.Create(CultureInfo.InvariantCulture,
                $"Thermal state {thermal} reached the trip level {TripLevel}."));
        }
        else if (_tripped && resetEdge && thermal < ResetLevel)
        {
            _tripped = false;
            ctx.Log(Id, "OVERLOAD_RESET", "Overload relay reset.");
        }

        bool closed = _welded || (Command.Value && SafetyOk.Value && !_tripped && !_open);
        if (closed != _closed)
        {
            ctx.Log(Id, closed ? "CONTACTOR_CLOSED" : "CONTACTOR_OPENED", closed ? "Motor energised." : "Motor de-energised.");
        }

        _closed = closed;
        Contactor.Value = closed;
        Tripped.Value = _tripped;
    }

    public void ApplyFault(string faultId, FaultArguments arguments)
    {
        switch (faultId)
        {
            case ContactorWelded:
                _welded = true;
                break;
            case ContactorOpen:
                _open = true;
                break;
        }
    }

    public void ClearFault(string faultId)
    {
        switch (faultId)
        {
            case ContactorWelded:
                _welded = false;
                break;
            case ContactorOpen:
                _open = false;
                break;
        }
    }
}
