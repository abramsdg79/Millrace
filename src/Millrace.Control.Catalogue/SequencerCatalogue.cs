using Millrace.Core.Catalogue;
using Millrace.Io;

namespace Millrace.Control.Catalogue;

/// <summary>The <see cref="Sequencer"/> in a plant file: <c>{ "type": "sequencer", … }</c>.</summary>
public static class SequencerCatalogue
{
    /// <summary>One step: its name, its entry writes, its transition and its optional timeout.</summary>
    public static GroupDefinition StepGroup { get; } = new(
        "SequenceStep",
        Param.String("name", "A short phrase for the step's event message, without a full stop."),
        Param.GroupList("writes", "What to command on the scan that enters the step.", ControlCatalogue.WriteGroup),
        Param.Object("transition", "What ends the step: when a tag compares as asked, or after a delay.", ControlCatalogue.TransitionSlot),
        Param.Double(
            "timeoutS",
            "How long the step may run before the sequence faults and sends the abort writes. Omit for no limit.",
            "s",
            min: 0.0,
            max: ControlCatalogue.MaxSeconds,
            exclusiveMin: true,
            optional: true));

    public static BlockDescriptor Descriptor { get; } = new(
        "sequencer",
        "A linear sequence of steps, each with entry writes, a transition and an optional timeout, run by Start, Hold, Resume, Abort and Reset.",
        (id, p) =>
        [
            ControlCatalogue.Output(id, "Step", TagKind.Int64, string.Empty, "The step running, or 0 when idle"),
            ControlCatalogue.Output(id, "Running", TagKind.Bool, string.Empty, "A step is running"),
            ControlCatalogue.Output(id, "Held", TagKind.Bool, string.Empty, "The step clock is frozen"),
            ControlCatalogue.Output(id, "Complete", TagKind.Bool, string.Empty, "The last step finished"),
            ControlCatalogue.Output(id, "Faulted", TagKind.Bool, string.Empty, "A step timed out"),
            ControlCatalogue.Output(id, "StepTime", TagKind.Double, "s", "Time in the current step"),
            ControlCatalogue.Command(id, "Start", "Enters step 1 from idle on a rising edge"),
            ControlCatalogue.Command(id, "Hold", "Freezes the step clock on a rising edge"),
            ControlCatalogue.Command(id, "Resume", "Continues a held step on a rising edge"),
            ControlCatalogue.Command(id, "Abort", "Returns to idle on a rising edge"),
            ControlCatalogue.Command(id, "Reset", "Returns to idle from faulted or complete on a rising edge"),
        ],
        (id, period, p) => new Sequencer(id, p.Groups("steps").Select(Step).ToList(), period, ControlCatalogue.Writes(p, "abort")))
    {
        Parameters =
        [
            Param.GroupList("steps", "The steps, in order. There is no branching and no parallel step.", StepGroup, minCount: 1),
            Param.GroupList("abort", "What to command on an Abort or a step timeout.", ControlCatalogue.WriteGroup),
        ],
    };

    private static SequenceStep Step(ParameterValues g) => new(
        g.String("name"),
        ControlCatalogue.Writes(g, "writes"),
        g.Object<StepTransition>("transition"),
        g.Has("timeoutS") ? TimeSpan.FromSeconds(g.Double("timeoutS")) : null);
}
