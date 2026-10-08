using Millrace.Io;

namespace Millrace.Core.Control;

/// <summary>
/// A block that passed validation, with everything the runtime needs that only
/// the builder knows: how many ticks its period is, and the owned tags it
/// publishes, in pin order.
/// </summary>
internal sealed class ScanBlockPlan
{
    internal ScanBlockPlan(IScanBlock block, long periodTicks, OwnedTag[] outputs, OwnedTag[] commands)
    {
        Block = block;
        PeriodTicks = periodTicks;
        Outputs = outputs;
        Commands = commands;
    }

    /// <summary>The block itself.</summary>
    internal IScanBlock Block { get; }

    /// <summary>The scan period in ticks; at least one.</summary>
    internal long PeriodTicks { get; }

    /// <summary>The owned outputs, in <see cref="IScanBlock.Outputs"/> order.</summary>
    internal OwnedTag[] Outputs { get; }

    /// <summary>The owned commands, in <see cref="IScanBlock.Commands"/> order.</summary>
    internal OwnedTag[] Commands { get; }
}
