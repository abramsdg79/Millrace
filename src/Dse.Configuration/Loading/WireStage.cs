using Dse.Core.Catalogue;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;

namespace Dse.Configuration.Loading;

/// <summary>Stage 5: addresses become ports; ports get connected; tags get bound.</summary>
internal static class WireStage
{
    public static void Run(LoadState state)
    {
        var nodes = new Dictionary<string, ISimNode>(StringComparer.Ordinal);
        foreach (ComponentEntry entry in state.Components)
        {
            nodes[entry.Id] = entry.Node!;
            if (entry.Node is CompositeComponent composite)
            {
                foreach (ISimComponent leaf in composite.LeafComponents)
                {
                    nodes[leaf.Id] = leaf;
                }
            }
        }

        foreach (LinkEntry link in state.Signals)
        {
            Connect(state, nodes, link, flows: false);
        }

        foreach (LinkEntry link in state.Flows)
        {
            Connect(state, nodes, link, flows: true);
        }

        foreach (TagRequest tag in state.Tags)
        {
            Bind(state, nodes, tag);
        }
    }

    private static void Connect(LoadState state, Dictionary<string, ISimNode> nodes, LinkEntry link, bool flows)
    {
        Port? from = Resolve(state, nodes, link.From, $"{link.Path}.from");
        Port? to = Resolve(state, nodes, link.To, $"{link.Path}.to");
        if (from is null || to is null)
        {
            return;
        }

        Port? misplaced = new[] { from, to }.FirstOrDefault(p => p is FlowPort != flows);
        if (misplaced is not null)
        {
            state.Error(
                ConfigDiagnostics.CannotConnect,
                link.Path,
                flows
                    ? $"'{misplaced.QualifiedName}' carries a signal, but this link is under \"flows\"."
                    : $"'{misplaced.QualifiedName}' carries material, but this link is under \"signals\".",
                flows ? "Move the link to \"signals\", or name an inlet and an outlet." : "Move the link to \"flows\", or name an output and an input.");
            return;
        }

        if (!PortConnector.TryConnect(from, to, out string problem))
        {
            state.Error(
                ConfigDiagnostics.CannotConnect,
                link.Path,
                problem,
                flows
                    ? "Link an outlet to an inlet of the same payload kind; each end takes one link."
                    : "Link an output to an input of the same value type; an input takes one link.");
        }
    }

    private static void Bind(LoadState state, Dictionary<string, ISimNode> nodes, TagRequest tag)
    {
        Port? port = Resolve(state, nodes, tag.Port, $"{tag.Path}.port");
        if (port is null)
        {
            return;
        }

        try
        {
            TagBinding binding = TagBinding.ForPort(
                tag.Name, port, tag.Writable ? TagAccess.ReadWrite : TagAccess.ReadOnly, tag.Unit, tag.RangeLow, tag.RangeHigh, tag.Description);
            state.Bindings.Add((tag.Name, binding));
        }
        catch (ArgumentException ex)
        {
            int cut = ex.Message.IndexOf(" (Parameter '", StringComparison.Ordinal);
            state.Error(
                ConfigDiagnostics.TagCannotBind,
                tag.Path,
                cut < 0 ? ex.Message : ex.Message[..cut],
                "Bind a bool, double, int or long signal port, and ask for \"write\" only on an input.");
        }
    }

    private static Port? Resolve(LoadState state, Dictionary<string, ISimNode> nodes, string address, string path)
    {
        int dot = address.LastIndexOf('.');
        if (dot <= 0 || dot == address.Length - 1)
        {
            state.Error(
                ConfigDiagnostics.UnknownAddress,
                path,
                $"'{address}' is not a port address.",
                "Write it as <component>.<port>, such as CV001.Start; for a leaf inside a composite, CV001.Motor.Current.");
            return null;
        }

        string nodeId = address[..dot];
        string portName = address[(dot + 1)..];
        if (!nodes.TryGetValue(nodeId, out ISimNode? node))
        {
            state.Error(
                ConfigDiagnostics.UnknownAddress,
                path,
                $"'{nodeId}' is not a component in this plant (in the address '{address}').",
                Suggest.Fix(nodeId, state.Components.Select(c => c.Id), "components"));
            return null;
        }

        List<KeyValuePair<string, Port>> ports = node switch
        {
            CompositeComponent composite => composite.ExposedPorts.ToList(),
            ISimComponent leaf => leaf.Ports.Select(p => KeyValuePair.Create(p.Name, p)).ToList(),
            _ => [],
        };

        foreach (KeyValuePair<string, Port> candidate in ports)
        {
            if (string.Equals(candidate.Key, portName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        state.Error(
            ConfigDiagnostics.UnknownAddress,
            path,
            $"'{nodeId}' has no port named '{portName}'.",
            Suggest.Fix(portName, ports.Select(p => p.Key), "ports"));
        return null;
    }
}
