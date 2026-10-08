using System.Globalization;
using Millrace.Core.Catalogue;
using Millrace.Core.Control;
using Millrace.Core.Flow;
using Millrace.Core.Graph;
using Millrace.Core.Io;
using Millrace.Core.Time;
using Millrace.Core.Validation;
using Millrace.Io;

namespace Millrace.Core;

/// <summary>Collects nodes, flattens composites, validates, and produces a Simulation.</summary>
public sealed class SimulationBuilder
{
    private readonly List<ISimComponent> _components = [];
    private readonly List<CompositeComponent> _composites = [];
    private readonly List<(string Name, TagBinding Binding)> _explicitTags = [];
    private readonly List<IScanBlock> _blocks = [];
    private readonly List<string[]> _claims = [];
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
    /// <see cref="Build"/>: its period against the time step (MR013), its
    /// inputs and writes against the tag directory (MR014), its id and
    /// owned tag names against everything else in the plant (MR015), and its
    /// claims (MR016). Its outputs and commands become ordinary tags.
    /// </summary>
    /// <param name="block">The block.</param>
    /// <param name="claims">
    /// Tags this block alone may write, by full name: each a read-write tag in
    /// the block's <see cref="IScanBlock.Writes"/>. A claimed tag is published
    /// read-only, naming the block; an external write to it is refused, and so
    /// is any other block that commands it (MR016).
    /// </param>
    public SimulationBuilder AddScanBlock(IScanBlock block, IReadOnlyList<string>? claims = null)
    {
        ThrowIfBuilt();
        ArgumentNullException.ThrowIfNull(block);
        TagNameRules.Check(block.Id, nameof(block));
        string[] claimed = claims is null ? [] : [.. claims];
        if (claimed.Any(c => c is null))
        {
            throw new ArgumentException($"Block '{block.Id}' has a null claim. Name each claimed tag.", nameof(claims));
        }

        _blocks.Add(block);
        _claims.Add(claimed);
        return this;
    }

    /// <summary>Checks the plant without building it. Used by tooling and by Build.</summary>
    public ValidationResult Validate() => Validate(out _, out _, out _);

    /// <summary>
    /// The tags the plant added so far would publish, exactly as <see cref="Build"/>
    /// would put them in the directory — component tags, composite exposures and
    /// explicit binds, sorted by name and indexed, with the access Build gives them
    /// (a writable tag on an input a link drives is read-only, R23) — without
    /// building anything. A block's owned tags are not included, and neither is a
    /// tag <see cref="Validate()"/> would reject (MR009–MR011). The plant loader
    /// resolves a plant file's controllers against this list.
    /// </summary>
    public IReadOnlyList<TagDescriptor> PlantTags()
    {
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISimComponent component in _components)
        {
            componentIds.Add(component.Id);
        }

        return new TagDirectory(CollectTags(componentIds, [])).Tags;
    }

    /// <summary>Validates and constructs the simulation. Throws if the plant is invalid.</summary>
    public Simulation Build()
    {
        ThrowIfBuilt();

        ValidationResult result = Validate(
            out List<TagBinding> tags, out List<ScanBlockPlan> blocks, out Dictionary<string, string> claimants);
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

        var image = new TagImage(new TagDirectory(tags, claimants));
        return new Simulation(ordered, flow, _options, image, [.. blocks]);
    }

    private ValidationResult Validate(
        out List<TagBinding> tags, out List<ScanBlockPlan> blocks, out Dictionary<string, string> claimants)
    {
        var errors = new List<ValidationError>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ISimComponent component in _components)
        {
            if (!seen.Add(component.Id))
            {
                errors.Add(new ValidationError(
                    "MR001",
                    $"Duplicate component id '{component.Id}'. Ids must be unique across the " +
                    $"whole plant; rename one of them or place it inside a composite.",
                    [component.Id]));
            }
        }

        tags = CollectTags(seen, errors);
        blocks = CollectBlocks(seen, tags, errors);
        claimants = CheckClaims(tags, errors);

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
                        "MR002",
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
                        "MR004",
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
                "MR003",
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
                        "MR011",
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
                        "MR009",
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
                    "MR011",
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
                    "MR009",
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
                        "MR010",
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
                    "MR013",
                    string.Create(CultureInfo.InvariantCulture,
                        $"Block '{block.Id}' has a scan period of {block.ScanPeriod.TotalMilliseconds} ms. " +
                        $"A scan period must be positive and a whole number of {stepMs} ms steps."),
                    [block.Id]));
            }
            else if (block.ScanPeriod.Ticks % stepTicks != 0L)
            {
                errors.Add(new ValidationError(
                    "MR013",
                    string.Create(CultureInfo.InvariantCulture,
                        $"Block '{block.Id}' scans every {block.ScanPeriod.TotalMilliseconds} ms, which is not a " +
                        $"whole number of {stepMs} ms steps. Use a period that is a multiple of the time step."),
                    [block.Id]));
            }

            if (componentIds.Contains(block.Id))
            {
                errors.Add(new ValidationError(
                    "MR015",
                    $"Block id '{block.Id}' is already a component id. Ids must be unique across components and " +
                    $"blocks; rename one of them.",
                    [block.Id]));
            }
            else if (!blockIds.Add(block.Id))
            {
                errors.Add(new ValidationError(
                    "MR015",
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
            // Math.Max(1L, ...) exists only so an MR013-failed block's plan is still
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

    /// <summary>Creates one owned tag, or reports MR015 and creates nothing.</summary>
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
                "MR015",
                $"Block '{block.Id}' declares tag '{name}' twice. Give each output and command its own name.",
                [block.Id]));
            return;
        }

        if (byName.ContainsKey(name))
        {
            errors.Add(new ValidationError(
                "MR015",
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

    /// <summary>Checks one input or write pin against the tag it names (MR014).</summary>
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
                "MR014",
                $"Block '{block.Id}' {verb} tag '{pin.Name}', which the plant does not have. Check the name " +
                $"against 'millrace tags', or bind the port it should {(commanded ? "command" : "read")}.",
                [block.Id]));
            return;
        }

        if (binding.Kind != pin.Kind)
        {
            errors.Add(new ValidationError(
                "MR014",
                $"Block '{block.Id}' {verb} tag '{pin.Name}' as a {pin.Kind}, but the plant publishes a " +
                $"{binding.Kind}. Declare the pin with the kind the tag has.",
                [block.Id]));
            return;
        }

        if (commanded && binding.Access != TagAccess.ReadWrite)
        {
            errors.Add(new ValidationError(
                "MR014",
                $"Block '{block.Id}' commands tag '{pin.Name}', which is read-only. Command a read-write tag, " +
                $"or bind that port as a writable tag.",
                [block.Id]));
        }
    }

    /// <summary>
    /// MR016 (spec 6d): each claim names a read-write tag its block commands
    /// and no other block has claimed; then no other block commands a claimed
    /// tag. Runs after every block's owned tags have joined
    /// <paramref name="tags"/>, so a block may claim another block's command.
    /// Every error carries the claim's tag and its position in its block's list.
    /// Returns the claims that passed, tag name to block id.
    /// </summary>
    private Dictionary<string, string> CheckClaims(List<TagBinding> tags, List<ValidationError> errors)
    {
        var claimants = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_blocks.Count == 0)
        {
            return claimants;
        }

        var byName = new Dictionary<string, TagBinding>(StringComparer.Ordinal);
        foreach (TagBinding binding in tags)
        {
            byName.TryAdd(binding.Name, binding);
        }

        // Where each accepted claim sits in its claimant's list, for the errors that point at it.
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int b = 0; b < _blocks.Count; b++)
        {
            IScanBlock block = _blocks[b];
            var own = new HashSet<string>(StringComparer.Ordinal);
            for (int j = 0; j < _claims[b].Length; j++)
            {
                string claim = _claims[b][j];
                ValidationError? error = null;
                if (!own.Add(claim))
                {
                    // A repeat is reported once, as a repeat, whatever else is wrong with the tag.
                    error = Claim(block.Id, claim, j, "which it already claims. Claim each tag once.");
                }
                else if (!byName.TryGetValue(claim, out TagBinding? binding))
                {
                    error = Claim(block.Id, claim, j, $"which the plant does not have. {NearestFix(block, claim, byName)}");
                }
                else if (binding.Access != TagAccess.ReadWrite)
                {
                    error = Claim(
                        block.Id, claim, j, "which is read-only. Claim a read-write tag the block commands, not a measured value, " +
                        "a block's output or an input a signal link drives.");
                }
                else if (!block.Writes.Any(w => string.Equals(w.Name, claim, StringComparison.Ordinal)))
                {
                    error = Claim(
                        block.Id, claim, j, "which it does not command. Add the tag to the block's writes, or remove the claim.");
                }
                else if (claimants.TryGetValue(claim, out string? first))
                {
                    error = Claim(
                        block.Id, claim, j, $"which '{first}' already claims. A tag has one claimant; remove one of the claims.") with
                    {
                        ComponentIds = [block.Id, first],
                    };
                }
                else
                {
                    claimants[claim] = block.Id;
                    positions[claim] = j;
                }

                if (error is not null)
                {
                    errors.Add(error);
                }
            }
        }

        for (int b = 0; b < _blocks.Count; b++)
        {
            IScanBlock block = _blocks[b];

            // A block that claimed the tag itself was reported above if the claim failed; once is enough.
            var reported = new HashSet<string>(_claims[b], StringComparer.Ordinal);
            foreach (TagRef pin in block.Writes)
            {
                if (claimants.TryGetValue(pin.Name, out string? claimant)
                    && !string.Equals(claimant, block.Id, StringComparison.Ordinal)
                    && reported.Add(pin.Name))
                {
                    errors.Add(new ValidationError(
                        "MR016",
                        $"Block '{block.Id}' commands tag '{pin.Name}', which '{claimant}' claims. Only the claiming block " +
                        $"writes a claimed tag; remove the write from '{block.Id}', or this claim.",
                        [block.Id, claimant])
                    {
                        Tag = pin.Name,
                        ClaimIndex = positions[pin.Name],
                    });
                }
            }
        }

        return claimants;

        // The nearest name among the block's own writes the plant has, then among every tag (spec 6e criterion 7);
        // Suggest.Closest sorts its candidates, so the order handed to it never matters.
        static string NearestFix(IScanBlock block, string claim, Dictionary<string, TagBinding> byName)
        {
            string? closest = Suggest.Closest(claim, block.Writes.Select(w => w.Name).Where(byName.ContainsKey))
                ?? Suggest.Closest(claim, byName.Keys);
            return closest is null
                ? "Check the name against 'millrace tags'; a block claims a tag it commands."
                : $"Check the name against 'millrace tags' — '{closest}' is closest; a block claims a tag it commands.";
        }

        static ValidationError Claim(string blockId, string claim, int index, string rest) =>
            new("MR016", $"Block '{blockId}' claims tag '{claim}', {rest}", [blockId]) { Tag = claim, ClaimIndex = index };
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
