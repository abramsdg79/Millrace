using Dse.Core.Graph;
using Dse.Core.Time;
using Dse.Core.Validation;

namespace Dse.Core;

/// <summary>Collects nodes, flattens composites, validates, and produces a Simulation.</summary>
public sealed class SimulationBuilder
{
    private readonly List<ISimComponent> _components = [];
    private readonly SimulationOptions _options;

    public SimulationBuilder(SimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Adds a leaf component, or every leaf beneath a composite.</summary>
    public SimulationBuilder Add(ISimNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case ISimComponent component:
                _components.Add(component);
                break;
            case CompositeComponent composite:
                _components.AddRange(composite.Leaves());
                break;
            default:
                throw new ArgumentException(
                    $"'{node.GetType().Name}' is neither an {nameof(ISimComponent)} nor a " +
                    $"{nameof(CompositeComponent)}.",
                    nameof(node));
        }

        return this;
    }

    /// <summary>Checks the plant without building it. Used by tooling and by Build.</summary>
    public ValidationResult Validate()
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

        foreach (ISimComponent component in _components)
        {
            foreach (Port port in component.Ports)
            {
                if (port.IsMissingRequiredConnection)
                {
                    errors.Add(new ValidationError(
                        "DSE002",
                        $"Input '{port.QualifiedName}' is required but nothing drives it. " +
                        $"Connect an output to it, or declare the input optional with a default.",
                        [component.Id]));
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

        return ValidationResult.From(errors);
    }

    /// <summary>Validates and constructs the simulation. Throws if the plant is invalid.</summary>
    public Simulation Build()
    {
        ValidationResult result = Validate();
        if (!result.IsValid)
        {
            throw new SimulationValidationException(result);
        }

        GraphResolver.TryResolve(_components, out ISimComponent[] ordered, out _);
        return new Simulation(ordered, _options);
    }
}
