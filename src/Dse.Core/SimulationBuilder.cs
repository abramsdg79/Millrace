using System.Globalization;
using Dse.Core.Control;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Time;
using Dse.Core.Validation;
using Dse.Io;

namespace Dse.Core;

/// <summary>Collects nodes, flattens composites, validates, and produces a Simulation.</summary>
public sealed class SimulationBuilder
{
    private readonly List<ISimComponent> _components = [];
    private readonly List<CompositeComponent> _composites = [];
    private readonly List<(string Name, TagBinding Binding)> _explicitTags = [];
    private readonly List<IScanBlock> _blocks = [];
    private readonly SimulationOptions _options;
    private bool _built;

    public SimulationBuilder(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Adds a leaf component, or every leaf beneath a composite.</summary>
    public SimulationBuilder Add(ISimNode node)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case ISimComponent component:
                _components.Add(component);
                break;
            case CompositeComponent composite:
                _components.AddRange(composite.Leaves());
                _composites.Add(composite);
                break;
            default:
                throw new ArgumentException(
                    $"'{node.GetType().Name}' is neither an {nameof(ISimComponent)} nor a " +
                    $"{nameof(CompositeComponent)}.",
                    nameof(node));
        }

        return this;
    }

    /// <summary>
    /// Binds a tag under an explicit full name (R22). Adds a tag for a port
    /// nothing declared, or replaces the declared binding for that port.
    /// </summary>
    public SimulationBuilder Bind(string name, TagBinding binding)
    {
        ThrowIfBuilt();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(binding);
        _explicitTags.Add((name, binding));
        return this;
    }

    /// <summary>
    /// Attaches a control block (spec 5c §3). The block is checked at
    /// <see cref="Build"/>: its period against the time step (DSE013), its
    /// inputs and writes against the tag directory (DSE014), and its id and
    /// owned tag names against everything else in the plant (DSE015). Its
    /// outputs and commands become ordinary tags.
    /// </summary>
    public SimulationBuilder AddScanBlock(IScanBlock block)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(block);
        TagNameRules.Check(block.Id, nameof(block));
        _blocks.Add(block);
        return this;
    }

    /// <summary>Checks the plant without building it. Used by tooling and by Build.</summary>
    public ValidationResult Validate() => Validate(out _, out _);

    /// <summary>Validates and constructs the simulation. Throws if the plant is invalid.</summary>
    public Simulation Build()
    {
        ThrowIfBuilt();

        ValidationResult result = Validate(out List<TagBinding> tags, out List<ScanBlockPlan> blocks);
        if (!result.IsValid)
        {
            throw new SimulationValidationException(result);
        }

        GraphResolver.TryResolve(_components, out ISimComponent[] ordered, out _);
        FlowGraph flow = FlowGraph.Build(FlowNodes());

        foreach (TagBinding tag in tags)
        {
            tag.BindExternal();
        }

        FreezePorts();
        _built = true;

        var image = new TagImage(new TagDirectory(tags));
        return new Simulation(ordered, flow, _options, image, [.. blocks]);
    }

    private ValidationResult Validate(out List<TagBinding> tags, out List<ScanBlockPlan> blocks)
    {
        var errors = new List<ValidationError>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISimComponent component in _components)
        {
            if (!seen.Add(component.Id))
            {
                errors.Add(new ValidationError(
                    "DSE001",
                    $"Duplicate component id '{component.Id}'. Ids must be unique across the " +
                    $"whole plant; rename one of them or place it inside a composite.",
                    [component.Id]));
            }
        }

        tags = CollectTags(seen, errors);
        blocks = CollectBlocks(seen, tags, errors);

        var writablePorts = new HashSet<Port>(ReferenceEqualityComparer.Instance);
        foreach (TagBinding tag in tags)
        {
            if (tag.Access == TagAccess.ReadWrite)
            {
                writablePorts.Add(tag.Port);
            }
        }

        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                if (port.IsMissingRequiredConnection && !writablePorts.Contains(port))
                {
                    errors.Add(new ValidationError(
                        "DSE002",
                        $"Input '{port.QualifiedName}' is required but nothing drives it. " +
                        $"Connect an output to it, or declare the input optional with a default.",
                        [component.Id]));
                }
            }
        }

        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                if (port.SourcePort is { } source && !seen.Contains(source.OwnerId))
                {
                    errors.Add(new ValidationError(
                        "DSE004",
                        $"Input '{port.QualifiedName}' is driven by '{source.QualifiedName}', but " +
                        $"component '{source.OwnerId}' is not part of the plant. Add it to the " +
                        $"builder, or add the composite that contains it.",
                        [component.Id, source.OwnerId]));
                }
            }
        }

        if (!GraphResolver.TryResolve(_components, out _, out IReadOnlyList<string> cycle))
        {
            string path = string.Join(" -> ", cycle.Append(cycle.Count > 0 ? cycle[0] : string.Empty));
            errors.Add(new ValidationError(
                "DSE003",
                $"Algebraic loop: {path}. Insert a UnitDelay on one connection in the cycle to " +
                $"break it; one tick of lag is physically irrelevant and makes the solve order " +
                $"unambiguous.",
                cycle));
        }

        errors.AddRange(FlowGraph.Validate(FlowNodes(), seen, _options.TimeStep.TotalSeconds));

        return ValidationResult.From(errors);
    }

    /// <summary>
    /// R22: leaf declarations qualified by component id, then composite aliases
    /// inside-out, then explicit binds. Every port ends with at most one
    /// binding; every name must be unique; a declared writable binding on a
    /// driven input degrades to read-only and an explicit one is an error (R23).
    /// </summary>
    private List<TagBinding> CollectTags(HashSet<string> componentIds, List<ValidationError> errors)
    {
        var byPort = new Dictionary<Port, TagBinding>(ReferenceEqualityComparer.Instance);
        var explicitPorts = new HashSet<Port>(ReferenceEqualityComparer.Instance);
        var order = new List<Port>();

        foreach (ISimComponent component in _components)
        {
            if (component is not ITagProvider provider)
            {
                continue;
            }

            foreach (TagBinding binding in provider.DescribeTags())
            {
                if (!string.Equals(binding.Port.OwnerId, component.Id, StringComparison.Ordinal))
                {
                    errors.Add(new ValidationError(
                        "DSE011",
                        $"Component '{component.Id}' declares tag '{binding.Name}' on port " +
                        $"'{binding.Port.QualifiedName}', which belongs to '{binding.Port.OwnerId}'. " +
                        $"A component declares tags on its own ports only.",
                        [component.Id, binding.Port.OwnerId]));
                    continue;
                }

                TagBinding qualified = binding.WithName($"{component.Id}.{binding.Name}");
                if (byPort.TryGetValue(binding.Port, out TagBinding? first))
                {
                    errors.Add(new ValidationError(
                        "DSE009",
                        $"Port '{binding.Port.QualifiedName}' is bound to two tags ('{first.Name}' and " +
                        $"'{qualified.Name}') by '{component.Id}'. Bind each port once.",
                        [component.Id]));
                    continue;
                }

                byPort[binding.Port] = qualified;
                order.Add(binding.Port);
            }
        }

        foreach (CompositeComponent top in _composites)
        {
            foreach (CompositeComponent composite in top.CompositesInsideOut())
            {
                foreach ((string alias, Port port) in composite.ExposedSignalPorts())
                {
                    if (byPort.TryGetValue(port, out TagBinding? declared))
                    {
                        byPort[port] = declared.WithName($"{composite.Id}.{alias}");
                    }
                }
            }
        }

        foreach ((string name, TagBinding binding) in _explicitTags)
        {
            if (!componentIds.Contains(binding.Port.OwnerId))
            {
                errors.Add(new ValidationError(
                    "DSE011",
                    $"Tag '{name}' binds port '{binding.Port.QualifiedName}', but component " +
                    $"'{binding.Port.OwnerId}' is not part of the plant. Add it to the builder.",
                    [binding.Port.OwnerId]));
                continue;
            }

            if (!byPort.ContainsKey(binding.Port))
            {
                order.Add(binding.Port);
            }

            byPort[binding.Port] = binding.WithName(name);
            explicitPorts.Add(binding.Port);
        }

        var result = new List<TagBinding>(order.Count);
        var byName = new Dictionary<string, TagBinding>(StringComparer.Ordinal);
        foreach (Port port in order)
        {
            TagBinding binding = byPort[port];
            if (byName.TryGetValue(binding.Name, out TagBinding? other))
            {
                errors.Add(new ValidationError(
                    "DSE009",
                    $"Tag '{binding.Name}' is bound to both '{other.Port.QualifiedName}' and " +
                    $"'{binding.Port.QualifiedName}'. Rename one with Bind or a composite alias.",
                    [other.Port.OwnerId, binding.Port.OwnerId]));
                continue;
            }

            if (binding.Access == TagAccess.ReadWrite && port.SourcePort is { } source)
            {
                if (explicitPorts.Contains(port))
                {
                    errors.Add(new ValidationError(
                        "DSE010",
                        $"Tag '{binding.Name}' would drive input '{port.QualifiedName}', but " +
                        $"'{source.QualifiedName}' already drives it. Remove the connection, or bind a " +
                        $"read-only tag instead.",
                        [port.OwnerId, source.OwnerId]));
                    continue;
                }

                // R23: the plant wired a controller here; the declared tag observes the command.
                binding = binding.AsReadOnly();
            }

            byName[binding.Name] = binding;
            result.Add(binding);
        }

        return result;
    }

    /// <summary>
    /// R69: two passes. The first checks each block's period and identity and
    /// creates its owned tags; the second checks every block's inputs and
    /// writes against the plant's tags *and* every block's owned tags, so a
    /// block may read another block's output whichever order they were added.
    /// </summary>
    private List<ScanBlockPlan> CollectBlocks(
        HashSet<string> componentIds,
        List<TagBinding> tags,
        List<ValidationError> errors)
    {
        var plans = new List<ScanBlockPlan>(_blocks.Count);
        if (_blocks.Count == 0)
        {
            return plans;
        }

        var byName = new Dictionary<string, TagBinding>(StringComparer.Ordinal);
        foreach (TagBinding binding in tags)
        {
            byName[binding.Name] = binding;
        }

        var blockIds = new HashSet<string>(StringComparer.Ordinal);
        long stepTicks = _options.TimeStep.Ticks;
        double stepMs = _options.TimeStep.TotalMilliseconds;

        if (stepTicks <= 0L)
        {
            return plans;
        }

        foreach (IScanBlock block in _blocks)
        {
            if (block.ScanPeriod <= TimeSpan.Zero)
            {
                errors.Add(new ValidationError(
                    "DSE013",
                    string.Create(CultureInfo.InvariantCulture,
                        $"Block '{block.Id}' has a scan period of {block.ScanPeriod.TotalMilliseconds} ms. " +
                        $"A scan period must be positive and a whole number of {stepMs} ms steps."),
                    [block.Id]));
            }
            else if (block.ScanPeriod.Ticks % stepTicks != 0L)
            {
                errors.Add(new ValidationError(
                    "DSE013",
                    string.Create(CultureInfo.InvariantCulture,
                        $"Block '{block.Id}' scans every {block.ScanPeriod.TotalMilliseconds} ms, which is not a " +
                        $"whole number of {stepMs} ms steps. Use a period that is a multiple of the time step."),
                    [block.Id]));
            }

            if (componentIds.Contains(block.Id))
            {
                errors.Add(new ValidationError(
                    "DSE015",
                    $"Block id '{block.Id}' is already a component id. Ids must be unique across components and " +
                    $"blocks; rename one of them.",
                    [block.Id]));
            }
            else if (!blockIds.Add(block.Id))
            {
                errors.Add(new ValidationError(
                    "DSE015",
                    $"Duplicate block id '{block.Id}'. Ids must be unique across components and blocks; rename " +
                    $"one of them.",
                    [block.Id]));
            }

            var ownNames = new HashSet<string>(StringComparer.Ordinal);
            var outputs = new List<OwnedTag>(block.Outputs.Count);
            var commands = new List<OwnedTag>(block.Commands.Count);

            foreach (TagSpec spec in block.Outputs)
            {
                AddOwned(block, spec, command: false, ownNames, byName, tags, outputs, errors);
            }

            foreach (TagSpec spec in block.Commands)
            {
                AddOwned(block, spec, command: true, ownNames, byName, tags, commands, errors);
            }

            long periodTicks = block.ScanPeriod.Ticks > 0L ? block.ScanPeriod.Ticks / stepTicks : 0L;
            // Math.Max(1L, ...) exists only so a DSE013-failed block's plan is still
            // constructible here; Build() throws SimulationValidationException before
            // this period value is ever used to schedule a scan.
            plans.Add(new ScanBlockPlan(block, Math.Max(1L, periodTicks), [.. outputs], [.. commands]));
        }

        foreach (IScanBlock block in _blocks)
        {
            foreach (TagRef pin in block.Inputs)
            {
                CheckPin(block, pin, commanded: false, byName, errors);
            }

            foreach (TagRef pin in block.Writes)
            {
                CheckPin(block, pin, commanded: true, byName, errors);
            }
        }

        return plans;
    }

    /// <summary>Creates one owned tag, or reports DSE015 and creates nothing.</summary>
    private static void AddOwned(
        IScanBlock block,
        TagSpec spec,
        bool command,
        HashSet<string> ownNames,
        Dictionary<string, TagBinding> byName,
        List<TagBinding> tags,
        List<OwnedTag> owned,
        List<ValidationError> errors)
    {
        string name = $"{block.Id}.{spec.Name}";

        if (!ownNames.Add(name))
        {
            errors.Add(new ValidationError(
                "DSE015",
                $"Block '{block.Id}' declares tag '{name}' twice. Give each output and command its own name.",
                [block.Id]));
            return;
        }

        if (byName.ContainsKey(name))
        {
            errors.Add(new ValidationError(
                "DSE015",
                $"Block '{block.Id}' owns tag '{name}', which the plant already has. Rename the block or the " +
                $"pin; a block's tag is its id followed by the pin name.",
                [block.Id]));
            return;
        }

        OwnedTag tag = command
            ? OwnedTag.Command(block.Id, name, spec)
            : OwnedTag.Output(block.Id, name, spec);

        byName[name] = tag.Binding;
        tags.Add(tag.Binding);
        owned.Add(tag);
    }

    /// <summary>Checks one input or write pin against the tag it names (DSE014).</summary>
    private static void CheckPin(
        IScanBlock block,
        TagRef pin,
        bool commanded,
        Dictionary<string, TagBinding> byName,
        List<ValidationError> errors)
    {
        string verb = commanded ? "commands" : "reads";

        if (!byName.TryGetValue(pin.Name, out TagBinding? binding))
        {
            errors.Add(new ValidationError(
                "DSE014",
                $"Block '{block.Id}' {verb} tag '{pin.Name}', which the plant does not have. Check the name " +
                $"against 'dse tags', or bind the port it should {(commanded ? "command" : "read")}.",
                [block.Id]));
            return;
        }

        if (binding.Kind != pin.Kind)
        {
            errors.Add(new ValidationError(
                "DSE014",
                $"Block '{block.Id}' {verb} tag '{pin.Name}' as a {pin.Kind}, but the plant publishes a " +
                $"{binding.Kind}. Declare the pin with the kind the tag has.",
                [block.Id]));
            return;
        }

        if (commanded && binding.Access != TagAccess.ReadWrite)
        {
            errors.Add(new ValidationError(
                "DSE014",
                $"Block '{block.Id}' commands tag '{pin.Name}', which is read-only. Command a read-write tag, " +
                $"or bind that port as a writable tag.",
                [block.Id]));
        }
    }

    /// <summary>The flow nodes among the added leaves, in registration order.</summary>
    private List<IFlowNode> FlowNodes() => _components.OfType<IFlowNode>().ToList();

    private void FreezePorts()
    {
        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                port.Freeze();
            }
        }
    }

    private void ThrowIfBuilt()
    {
        if (_built)
        {
            throw new InvalidOperationException(
                "This builder has already produced a simulation; the plant is immutable after Build(). " +
                "Create a new builder for a different plant.");
        }
    }
}
