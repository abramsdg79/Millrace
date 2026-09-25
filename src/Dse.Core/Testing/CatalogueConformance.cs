using System.Globalization;
using System.Text.Json;
using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Randomness;
using Dse.Core.Telemetry;
using Dse.Io;

namespace Dse.Core.Testing;

/// <summary>
/// Builds one instance of every catalogue entry and reports every difference
/// between a descriptor and what it built. No test-framework dependency: a
/// module's tests assert that <see cref="ConformanceReport.Mismatches"/> is empty.
/// </summary>
public static class CatalogueConformance
{
    /// <summary>The id every probe is built with.</summary>
    public const string ProbeId = "probe";

    /// <summary>The scan period every block probe is built with.</summary>
    public static TimeSpan ProbePeriod { get; } = TimeSpan.FromMilliseconds(100);

    public static ConformanceReport Check(ComponentCatalogue catalogue, ConformanceFixtures fixtures)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(fixtures);

        var mismatches = new List<string>();
        var built = new List<Type>();
        Type[] wanted = catalogue.Components
            .SelectMany(c => Flatten(c.Parameters))
            .Concat(catalogue.Objects.SelectMany(o => Flatten(o.Parameters)))
            .Where(p => p.Kind == ParameterKind.Reference)
            .Select(p => p.Capability!)
            .Distinct()
            .ToArray();

        foreach (ObjectDescriptor descriptor in catalogue.Objects)
        {
            string label = $"{descriptor.Slot} '{descriptor.Type}'";
            ParameterValues? values = BindFixture(
                descriptor.Parameters, fixtures.ParametersFor(descriptor.Slot, descriptor.Type), fixtures.NewContext(catalogue), label, mismatches);
            if (values is not null && TryBuild(() => descriptor.Factory(values), label, mismatches, out object? made))
            {
                built.Add(made.GetType());
            }
        }

        foreach (ComponentDescriptor descriptor in catalogue.Components)
        {
            ParameterValues? values = BindFixture(
                descriptor.Parameters, fixtures.ParametersFor(descriptor.Type), fixtures.NewContext(catalogue), descriptor.Type, mismatches);
            if (values is null || !TryBuild(() => descriptor.Factory(ProbeId, values), descriptor.Type, mismatches, out object? made))
            {
                continue;
            }

            var node = (ISimNode)made;
            built.Add(node.GetType());
            Compare(descriptor, values, node, wanted, mismatches);
        }

        foreach (BlockDescriptor descriptor in catalogue.Blocks)
        {
            IReadOnlyList<string> jsons = fixtures.BlockParametersFor(descriptor.Type);
            for (int i = 0; i < jsons.Count; i++)
            {
                string label = jsons.Count == 1
                    ? descriptor.Type
                    : string.Create(CultureInfo.InvariantCulture, $"{descriptor.Type} (fixture {i + 1})");
                ParameterValues? values = BindFixture(descriptor.Parameters, jsons[i], fixtures.NewContext(catalogue), label, mismatches);
                if (values is null || !TryBuild(() => descriptor.Factory(ProbeId, ProbePeriod, values), label, mismatches, out object? made))
                {
                    continue;
                }

                var block = (IScanBlock)made;
                built.Add(block.GetType());
                CompareBlock(descriptor, label, values, block, mismatches);
            }
        }

        return new ConformanceReport(mismatches, built.Distinct().ToList());
    }

    private static IEnumerable<ParameterDescriptor> Flatten(IEnumerable<ParameterDescriptor> parameters) =>
        parameters.SelectMany(p => Flatten(p.Children).Prepend(p));

    private static ParameterValues? BindFixture(
        IReadOnlyList<ParameterDescriptor> schema, string json, BindingContext context, string label, List<string> mismatches)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var issues = new List<BindingIssue>();
        ParameterValues? values = ParameterBinder.Bind(schema, document.RootElement, "$", context, construct: true, issues);
        foreach (BindingIssue issue in issues)
        {
            mismatches.Add($"{label}: the fixture does not bind — {issue.Path}: {issue.Message} {issue.Fix}");
        }

        return values;
    }

    private static bool TryBuild(Func<object> build, string label, List<string> mismatches, out object made)
    {
        try
        {
            made = build();
            return true;
        }
#pragma warning disable CA1031 // Any exception from a factory is a finding to report, not a crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            mismatches.Add($"{label}: the factory threw {ex.GetType().Name}: {ex.Message}");
            made = new object();
            return false;
        }
    }

    private static void Compare(ComponentDescriptor descriptor, ParameterValues values, ISimNode node, Type[] wanted, List<string> mismatches)
    {
        IReadOnlyList<ISimComponent> leaves = node switch
        {
            CompositeComponent composite => composite.LeafComponents,
            ISimComponent leaf => [leaf],
            _ => [],
        };
        List<(string Name, Port Port)> ports = node switch
        {
            CompositeComponent composite => composite.ExposedPorts.Select(e => (e.Key, e.Value)).ToList(),
            ISimComponent leaf => leaf.Ports.Select(p => (p.Name, p)).ToList(),
            _ => [],
        };

        Diff(
            descriptor.Type, "signal port",
            descriptor.Ports.SelectMany(p => Names(p.Name, p.Repeat, values)
                .Select(n => $"'{n}' ({p.Direction} {p.ValueType}{(p.Required ? " required" : string.Empty)})")),
            ports.Where(p => p.Port is not FlowPort)
                .Select(p => $"'{p.Name}' ({(p.Port.IsInput ? PortDirection.In : PortDirection.Out)} {PortSpec.ValueTypeName(p.Port.ValueType!)}{(p.Port.IsRequiredInput ? " required" : string.Empty)})"),
            mismatches);

        Diff(
            descriptor.Type, "flow port",
            descriptor.FlowPorts.SelectMany(p => Names(p.Name, p.Repeat, values).Select(n => $"'{n}' ({p.Direction} {p.Payload})")),
            ports.Where(p => p.Port is FlowPort)
                .Select(p => $"'{p.Name}' ({(p.Port is FlowInlet ? PortDirection.In : PortDirection.Out)} {((FlowPort)p.Port).Kind})"),
            mismatches);

        Diff(
            descriptor.Type, "fault",
            descriptor.Faults.Select(Signature),
            node is IFaultTarget target ? target.SupportedFaults.Select(Signature) : [],
            mismatches);

        Diff(
            descriptor.Type, "tag",
            descriptor.Tags.SelectMany(t => Names(t.Name, t.Repeat, values).Select(n => $"'{n}' ({t.Kind} {t.Access})")),
            ActualTags(node, leaves).Select(t => $"'{t.Name}' ({t.Kind} {t.Access})"),
            mismatches);

        CompareTelemetry(descriptor, leaves, mismatches);

        foreach (Type capability in descriptor.Provides)
        {
            if (!Capabilities.TryGet(node, capability, out _))
            {
                mismatches.Add($"{descriptor.Type}: provides {capability.Name} in the descriptor, but the instance cannot supply it.");
            }
        }

        foreach (Type capability in wanted)
        {
            if (Capabilities.TryGet(node, capability, out _) && !descriptor.Provides.Contains(capability))
            {
                mismatches.Add(
                    $"{descriptor.Type}: the instance can supply {capability.Name}, which a reference parameter in this catalogue needs, " +
                    $"but the descriptor does not list it under Provides.");
            }
        }
    }

    private static void CompareBlock(
        BlockDescriptor descriptor, string label, ParameterValues values, IScanBlock block, List<string> mismatches)
    {
        if (!string.Equals(block.Id, ProbeId, StringComparison.Ordinal))
        {
            mismatches.Add($"{label}: the instance's id is '{block.Id}', not the id its factory was given ('{ProbeId}').");
        }

        if (block.ScanPeriod != ProbePeriod)
        {
            mismatches.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{label}: the instance scans every {block.ScanPeriod.TotalMilliseconds} ms, not at the period its factory was given ({ProbePeriod.TotalMilliseconds} ms)."));
        }

        IReadOnlyList<(TagSpec Spec, TagAccess Access)> declared;
        try
        {
            declared = descriptor.OwnedTags(ProbeId, values);
        }
#pragma warning disable CA1031 // Reported, not rethrown: a broken OwnedTags is a finding like any other.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            mismatches.Add($"{label}: OwnedTags threw {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!TryReadPins(nameof(IScanBlock.Outputs), () => block.Outputs, label, mismatches, out IReadOnlyList<TagSpec> outputs)
            || !TryReadPins(nameof(IScanBlock.Commands), () => block.Commands, label, mismatches, out IReadOnlyList<TagSpec> commands))
        {
            return;
        }

        var actual = outputs.Select(s => (Spec: s, Access: TagAccess.ReadOnly))
            .Concat(commands.Select(s => (Spec: s, Access: TagAccess.ReadWrite)))
            .Select(t => (Name: $"{block.Id}.{t.Spec.Name}", t.Spec.Kind, t.Access, t.Spec.Unit, t.Spec.Description))
            .ToList();

        Diff(
            label, "owned tag",
            declared.Select(t => $"'{t.Spec.Name}' ({t.Spec.Kind} {t.Access})"),
            actual.Select(t => $"'{t.Name}' ({t.Kind} {t.Access})"),
            mismatches);

        // R102: a tag that matches by name, kind and access must also match by unit and description.
        foreach ((TagSpec spec, TagAccess access) in declared)
        {
            foreach (var match in actual.Where(a =>
                         string.Equals(a.Name, spec.Name, StringComparison.Ordinal) && a.Kind == spec.Kind && a.Access == access))
            {
                if (!string.Equals(match.Unit, spec.Unit, StringComparison.Ordinal))
                {
                    mismatches.Add($"{label}: owned tag '{spec.Name}' has unit '{spec.Unit}' in the descriptor but '{match.Unit}' on the instance.");
                }

                if (!string.Equals(match.Description, spec.Description, StringComparison.Ordinal))
                {
                    mismatches.Add($"{label}: owned tag '{spec.Name}' is described '{spec.Description}' in the descriptor but '{match.Description}' on the instance.");
                }
            }
        }
    }

    /// <summary>A pin getter that throws, returns null or holds a null is a finding to report, not a crash.</summary>
    private static bool TryReadPins(
        string pin, Func<IReadOnlyList<TagSpec>> read, string label, List<string> mismatches, out IReadOnlyList<TagSpec> pins)
    {
        try
        {
            pins = read();
        }
#pragma warning disable CA1031 // Reported, not rethrown: a broken pin getter is a finding like any other.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            mismatches.Add($"{label}: the instance's {pin} failed with {ex.GetType().Name}: {ex.Message}");
            pins = [];
            return false;
        }

        if (pins is null || pins.Any(p => p is null))
        {
            mismatches.Add($"{label}: the instance's {pin} is null or holds a null.");
            pins = [];
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> Names(string pattern, PortRepeat? repeat, ParameterValues values)
    {
        if (repeat is null)
        {
            return [pattern];
        }

        return repeat.IsByName
            ? repeat.Expand(pattern, 0, values.Groups(repeat.Parameter).Select(g => g.String(repeat.NameChild)).ToList())
            : repeat.Expand(pattern, values.Int(repeat.Parameter), []);
    }

    private static string Signature(FaultDescriptor fault) =>
        $"'{fault.Id}({string.Join(", ", fault.Parameters.Select(p => p.Name))})'";

    /// <summary>The tags a plant would see, named as <c>SimulationBuilder</c> names them, relative to the probe.</summary>
    private static List<(string Name, TagKind Kind, TagAccess Access)> ActualTags(ISimNode node, IReadOnlyList<ISimComponent> leaves)
    {
        var aliases = new Dictionary<Port, string>(ReferenceEqualityComparer.Instance);
        if (node is CompositeComponent composite)
        {
            foreach (KeyValuePair<string, Port> exposed in composite.ExposedPorts)
            {
                aliases[exposed.Value] = exposed.Key;
            }
        }

        var tags = new List<(string, TagKind, TagAccess)>();
        foreach (ISimComponent leaf in leaves)
        {
            if (leaf is not ITagProvider provider)
            {
                continue;
            }

            string prefix = leaf.Id.Length > node.Id.Length ? leaf.Id[(node.Id.Length + 1)..] + "." : string.Empty;
            foreach (TagBinding binding in provider.DescribeTags())
            {
                string name = aliases.TryGetValue(binding.Port, out string? alias) ? alias : prefix + binding.Name;
                TagAccess access = binding.Access == TagAccess.ReadWrite && binding.Port.SourcePort is not null
                    ? TagAccess.ReadOnly
                    : binding.Access;
                tags.Add((name, binding.Kind, access));
            }
        }

        return tags;
    }

    private static void CompareTelemetry(ComponentDescriptor descriptor, IReadOnlyList<ISimComponent> leaves, List<string> mismatches)
    {
        var registry = new TelemetryRegistry();
        var items = new ItemIdSequence();
        foreach (ISimComponent leaf in leaves)
        {
            try
            {
                leaf.Initialize(new InitContext(new DeterministicRandom(1UL), registry, items, leaf.Id, DateTimeOffset.UnixEpoch, 0.01));
            }
#pragma warning disable CA1031 // Reported, not rethrown: one broken Initialize must not hide the other findings.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                mismatches.Add($"{descriptor.Type}: Initialize threw {ex.GetType().Name} on '{leaf.Id}': {ex.Message}");
            }
        }

        var actual = registry.Channels.ToDictionary(c => c.Key[(ProbeId.Length + 1)..], c => c.Unit, StringComparer.Ordinal);
        Diff(
            descriptor.Type, "telemetry",
            descriptor.Telemetry.Select(t => $"'{t.Name}'"),
            actual.Keys.Select(k => $"'{k}'"),
            mismatches);

        foreach (TelemetryKey key in descriptor.Telemetry)
        {
            if (key.Unit.Length > 0 && actual.TryGetValue(key.Name, out string? unit) && !string.Equals(unit, key.Unit, StringComparison.Ordinal))
            {
                mismatches.Add($"{descriptor.Type}: telemetry '{key.Name}' is '{unit}' on the instance but '{key.Unit}' in the descriptor.");
            }
        }
    }

    private static void Diff(string type, string what, IEnumerable<string> declared, IEnumerable<string> actual, List<string> mismatches)
    {
        List<string> declaredList = declared.ToList();
        List<string> actualList = actual.ToList();
        foreach (string item in declaredList.Except(actualList, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            mismatches.Add($"{type}: {what} {item} is in the descriptor but not on the instance.");
        }

        foreach (string item in actualList.Except(declaredList, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            mismatches.Add($"{type}: {what} {item} is on the instance but not in the descriptor.");
        }
    }
}
