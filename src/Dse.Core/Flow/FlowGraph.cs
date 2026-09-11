using Dse.Core.Graph;
using Dse.Core.Validation;

namespace Dse.Core.Flow;

/// <summary>
/// The material layer of a built plant: nodes in downstream-first order, each
/// with its outgoing links. Transport is one sweep over that order, so back
/// pressure needs no code of its own — a full consumer simply accepts less.
/// </summary>
internal sealed class FlowGraph
{
    private readonly IFlowNode[] _order;
    private readonly FlowLink[][] _outgoing;

    private FlowGraph(IFlowNode[] order, FlowLink[][] outgoing)
    {
        _order = order;
        _outgoing = outgoing;
    }

    public static FlowGraph Empty { get; } = new([], []);

    /// <summary>Nodes in transport order: the most downstream first.</summary>
    public IReadOnlyList<IFlowNode> Nodes => _order;

    /// <summary>
    /// Every reason the material graph cannot run. <paramref name="plantIds"/>
    /// is the id set of every component in the plant, flow node or not, so a
    /// link from a non-flow component is told apart from a link from nowhere.
    /// </summary>
    public static IReadOnlyList<ValidationError> Validate(
        IReadOnlyList<IFlowNode> nodes,
        IReadOnlySet<string> plantIds,
        double dt)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(plantIds);

        var errors = new List<ValidationError>();
        Dictionary<string, int> byId = IndexById(nodes);

        foreach (IFlowNode node in nodes)
        {
            foreach (Port port in node.Ports)
            {
                switch (port)
                {
                    case FlowInlet { Source: { } source } inlet when !byId.ContainsKey(source.OwnerId):
                        errors.Add(MissingProducer(node, inlet, source, plantIds));
                        break;
                    case FlowInlet { IsConnected: true } inlet when !ImplementsConsumer(node, inlet.Kind):
                        errors.Add(ContractGap(node, inlet, ConsumerContract(inlet.Kind)));
                        break;
                    case FlowOutlet { IsConnected: true } outlet when !ImplementsProducer(node, outlet.Kind):
                        errors.Add(ContractGap(node, outlet, ProducerContract(outlet.Kind)));
                        break;
                }
            }

            errors.AddRange(node.ValidateFlow(dt));
        }

        if (errors.Count == 0 && !TrySort(nodes, byId, out _, out List<string> cycle))
        {
            errors.Add(new ValidationError(
                "DSE005",
                $"Material recirculation loop: {string.Join(" -> ", cycle.Append(cycle[0]))}. " +
                $"Recirculation is not supported; break the loop with an explicit sink and source.",
                cycle));
        }

        return errors;
    }

    /// <summary>Builds the transport order. Call <see cref="Validate"/> first; an invalid set throws.</summary>
    public static FlowGraph Build(IReadOnlyList<IFlowNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0)
        {
            return Empty;
        }

        Dictionary<string, int> byId = IndexById(nodes);
        if (!TrySort(nodes, byId, out int[] topological, out _))
        {
            throw new InvalidOperationException(
                "The material graph contains a loop. Validate the plant before building it.");
        }

        // Downstream first: reverse the topological order.
        var order = new IFlowNode[nodes.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = nodes[topological[order.Length - 1 - i]];
        }

        var outgoing = new FlowLink[order.Length][];
        for (int i = 0; i < order.Length; i++)
        {
            var links = new List<FlowLink>();
            foreach (Port port in order[i].Ports)
            {
                if (port is FlowOutlet { Target: { } target } outlet)
                {
                    links.Add(new FlowLink(order[i], outlet, nodes[byId[target.OwnerId]], target));
                }
            }

            outgoing[i] = links.ToArray();
        }

        return new FlowGraph(order, outgoing);
    }

    /// <summary>
    /// Phase 3. For each node, most downstream first: discharge through its
    /// outgoing links into consumers that have already advanced and made room,
    /// then advance its own contents.
    /// </summary>
    public void Step(double dt)
    {
        for (int i = 0; i < _order.Length; i++)
        {
            foreach (FlowLink link in _outgoing[i])
            {
                Transfer(in link);
            }

            _order[i].Advance(dt);
        }
    }

    private static void Transfer(in FlowLink link)
    {
        if (link.Outlet.Kind == PayloadKind.Bulk)
        {
            TransferBulk(in link);
        }
        else
        {
            TransferItems(in link);
        }
    }

    private static void TransferBulk(in FlowLink link)
    {
        var producer = (IBulkProducer)link.Producer;
        var consumer = (IBulkConsumer)link.Consumer;

        double mass = Math.Min(producer.OfferMass(link.Outlet), consumer.AcceptMass(link.Inlet));
        if (mass <= 0.0)
        {
            return;
        }

        BulkLot lot = producer.Withdraw(link.Outlet, mass);
        if (lot.IsEmpty)
        {
            return;
        }

        consumer.Deposit(link.Inlet, in lot);
    }

    private static void TransferItems(in FlowLink link)
    {
        var producer = (IItemProducer)link.Producer;
        var consumer = (IItemConsumer)link.Consumer;

        while (producer.TryPeekItem(link.Outlet, out ItemInstance? item)
            && consumer.CanAcceptItem(link.Inlet, item))
        {
            ItemInstance withdrawn = producer.WithdrawItem(link.Outlet);
            if (!ReferenceEquals(withdrawn, item))
            {
                throw new InvalidOperationException(
                    $"'{link.Producer.Id}' withdrew {withdrawn} after showing {item}. " +
                    $"{nameof(IItemProducer.WithdrawItem)} must remove exactly the item last " +
                    $"returned by {nameof(IItemProducer.TryPeekItem)} for that outlet.");
            }

            consumer.DepositItem(link.Inlet, withdrawn);
        }
    }

    private static Dictionary<string, int> IndexById(IReadOnlyList<IFlowNode> nodes)
    {
        var byId = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            byId[nodes[i].Id] = i;
        }

        return byId;
    }

    private static bool TrySort(
        IReadOnlyList<IFlowNode> nodes,
        Dictionary<string, int> byId,
        out int[] order,
        out List<string> cycle)
    {
        var dependents = new List<int>[nodes.Count];
        for (int i = 0; i < dependents.Length; i++)
        {
            dependents[i] = [];
        }

        for (int consumer = 0; consumer < nodes.Count; consumer++)
        {
            foreach (Port port in nodes[consumer].Ports)
            {
                if (port is FlowInlet { Source: { } source }
                    && byId.TryGetValue(source.OwnerId, out int producer)
                    && producer != consumer)
                {
                    dependents[producer].Add(consumer);
                }
            }
        }

        if (TopologicalSorter.TrySort(dependents, out order, out List<int> loop))
        {
            cycle = [];
            return true;
        }

        cycle = loop.Select(i => nodes[i].Id).ToList();
        return false;
    }

    private static bool ImplementsConsumer(IFlowNode node, PayloadKind kind) =>
        kind == PayloadKind.Bulk ? node is IBulkConsumer : node is IItemConsumer;

    private static bool ImplementsProducer(IFlowNode node, PayloadKind kind) =>
        kind == PayloadKind.Bulk ? node is IBulkProducer : node is IItemProducer;

    private static string ConsumerContract(PayloadKind kind) =>
        kind == PayloadKind.Bulk ? nameof(IBulkConsumer) : nameof(IItemConsumer);

    private static string ProducerContract(PayloadKind kind) =>
        kind == PayloadKind.Bulk ? nameof(IBulkProducer) : nameof(IItemProducer);

    private static ValidationError ContractGap(IFlowNode node, FlowPort port, string contract) =>
        new(
            "DSE008",
            $"Port '{port.QualifiedName}' is connected, but '{node.Id}' does not implement " +
            $"{contract}. Implement it, or leave the port unconnected.",
            [node.Id]);

    private static ValidationError MissingProducer(
        IFlowNode node,
        FlowInlet inlet,
        FlowOutlet source,
        IReadOnlySet<string> plantIds) =>
        plantIds.Contains(source.OwnerId)
            ? new ValidationError(
                "DSE008",
                $"Inlet '{inlet.QualifiedName}' is fed by '{source.QualifiedName}', but " +
                $"'{source.OwnerId}' is not an {nameof(IFlowNode)}. Derive it from " +
                $"{nameof(FlowComponentBase)} or implement {nameof(IFlowNode)}.",
                [node.Id, source.OwnerId])
            : new ValidationError(
                "DSE007",
                $"Inlet '{inlet.QualifiedName}' is fed by '{source.QualifiedName}', but " +
                $"component '{source.OwnerId}' is not part of the plant. Add it to the " +
                $"builder, or add the composite that contains it.",
                [node.Id, source.OwnerId]);
}
