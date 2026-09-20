using Dse.Core.Flow;

namespace Dse.Core.Graph;

/// <summary>
/// Connects two ports without knowing their value type. Configuration loaders
/// hold a <see cref="Port"/>, not an <c>OutputPort&lt;T&gt;</c>; this is their way in.
/// Code that knows its types keeps using <c>ConnectTo</c>.
/// </summary>
public static class PortConnector
{
    /// <summary>
    /// Connects <paramref name="from"/> to <paramref name="to"/>. Returns false
    /// with a sentence in <paramref name="problem"/> when the two cannot be
    /// connected; never throws for a wiring mistake.
    /// </summary>
    public static bool TryConnect(Port from, Port to, out string problem)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        bool fromIsFlow = from is FlowPort;
        bool toIsFlow = to is FlowPort;
        if (fromIsFlow != toIsFlow)
        {
            Port flow = fromIsFlow ? from : to;
            Port signal = fromIsFlow ? to : from;
            problem =
                $"'{flow.QualifiedName}' carries material and '{signal.QualifiedName}' carries a signal; " +
                $"a link joins two ports of the same layer.";
            return false;
        }

        try
        {
            return fromIsFlow ? ConnectFlow(from, to, out problem) : ConnectSignal(from, to, out problem);
        }
        catch (InvalidOperationException ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    private static bool ConnectFlow(Port from, Port to, out string problem)
    {
        if (from is not FlowOutlet outlet)
        {
            problem = $"'{from.QualifiedName}' is an inlet; a flow link starts at an outlet.";
            return false;
        }

        if (to is not FlowInlet inlet)
        {
            problem = $"'{to.QualifiedName}' is an outlet; a flow link ends at an inlet.";
            return false;
        }

        outlet.ConnectTo(inlet);
        problem = string.Empty;
        return true;
    }

    private static bool ConnectSignal(Port from, Port to, out string problem)
    {
        if (from.IsInput)
        {
            problem = $"'{from.QualifiedName}' is an input; a signal link starts at an output.";
            return false;
        }

        if (!to.IsInput)
        {
            problem = $"'{to.QualifiedName}' is an output; a signal link ends at an input.";
            return false;
        }

        if (to.TryConnectFrom(from))
        {
            problem = string.Empty;
            return true;
        }

        problem =
            $"'{from.QualifiedName}' carries {from.ValueType?.Name} and '{to.QualifiedName}' expects " +
            $"{to.ValueType?.Name}; a signal link joins ports of one value type.";
        return false;
    }
}
