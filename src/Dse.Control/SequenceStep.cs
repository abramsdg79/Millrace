namespace Dse.Control;

/// <summary>
/// One step of a linear sequence: what it commands on entry, what ends it, and
/// how long it may take before the sequence faults.
/// </summary>
public sealed class SequenceStep
{
    /// <summary>Creates a step.</summary>
    /// <param name="name">A short phrase, used in the step's event message. It must not end in a full stop: the block adds one.</param>
    /// <param name="entryWrites">What to command on the scan that enters the step; may be empty.</param>
    /// <param name="transition">What ends the step.</param>
    /// <param name="timeout">How long the step may run before the sequence faults, or null for no limit.</param>
    public SequenceStep(
        string name,
        IReadOnlyList<BlockWrite> entryWrites,
        StepTransition transition,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(entryWrites);
        ArgumentNullException.ThrowIfNull(transition);

        if (name.EndsWith('.'))
        {
            throw new ArgumentException(
                $"Step name '{name}' must not end in a full stop; the block adds one.", nameof(name));
        }

        if (timeout is { } limit && limit <= TimeSpan.Zero)
        {
            throw new ArgumentException("A step timeout must be positive, or absent.", nameof(timeout));
        }

        for (int i = 0; i < entryWrites.Count; i++)
        {
            for (int j = 0; j < i; j++)
            {
                if (string.Equals(entryWrites[i].Tag, entryWrites[j].Tag, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Tag '{entryWrites[i].Tag}' is commanded twice by step '{name}'. Command each tag once.",
                        nameof(entryWrites));
                }
            }
        }

        Name = name;
        EntryWrites = [.. entryWrites];
        Transition = transition;
        Timeout = timeout;
    }

    /// <summary>A short phrase, used in the step's event message.</summary>
    public string Name { get; }

    /// <summary>What the step commands on entry.</summary>
    public IReadOnlyList<BlockWrite> EntryWrites { get; }

    /// <summary>What ends the step.</summary>
    public StepTransition Transition { get; }

    /// <summary>How long the step may run before the sequence faults, or null.</summary>
    public TimeSpan? Timeout { get; }
}
