using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Control.Catalogue;

/// <summary>The <see cref="Alarm"/> in a plant file: <c>{ "type": "alarm", … }</c>.</summary>
public static class AlarmCatalogue
{
    private static readonly string[] Kinds = ["lo-lo", "lo", "hi", "hi-hi"];

    /// <summary>One limit: its kind, its value, its deadband and its on-delay.</summary>
    public static GroupDefinition LimitGroup { get; } = new(
        "AlarmLimit",
        Param.Enum("kind", "Which limit. Configured limits must ascend lo-lo < lo < hi < hi-hi, each at most once.", Kinds),
        Param.Double("value", "The limit, in the tag's engineering unit."),
        Param.Double("deadband", "How far back inside the limit the value must come before the alarm clears.", @default: 0.0, min: 0.0),
        ControlCatalogue.Seconds("onDelayS", "How long the value must stay across before the alarm raises.", 0.0));

    public static BlockDescriptor Descriptor { get; } = new(
        "alarm",
        "An ISA-18.2 analog alarm over one Double tag: one to four limits, each with a deadband and an on-delay, acknowledged by Ack.",
        OwnedTags,
        (id, period, p) => new Alarm(id, p.Tag("input"), p.Groups("limits").Select(Limit).ToList(), period))
    {
        Parameters =
        [
            Param.Tag("input", "The Double tag watched.", TagKind.Double),
            Param.GroupList("limits", "One to four limits, each kind at most once.", LimitGroup, minCount: 1),
        ],
    };

    /// <summary>A pair per configured limit, in limit order — the order the constructor sorts them into — then Ack.</summary>
    private static IReadOnlyList<(TagSpec Spec, TagAccess Access)> OwnedTags(string id, ParameterValues p)
    {
        var tags = new List<(TagSpec Spec, TagAccess Access)>();
        foreach (AlarmLimitKind kind in p.Groups("limits").Select(g => Kind(g.String("kind"))).Order())
        {
            string name = kind.ToString();
            tags.Add(ControlCatalogue.Output(id, $"{name}.Active", TagKind.Bool, string.Empty, $"The {name} limit is in alarm"));
            tags.Add(ControlCatalogue.Output(id, $"{name}.Acked", TagKind.Bool, string.Empty, $"Nothing is outstanding on the {name} limit"));
        }

        tags.Add(ControlCatalogue.Command(id, "Ack", "Acknowledges every outstanding limit on a rising edge"));
        return tags;
    }

    private static AlarmLimit Limit(ParameterValues g) =>
        new(Kind(g.String("kind")), g.Double("value"), g.Double("deadband"), TimeSpan.FromSeconds(g.Double("onDelayS")));

    private static AlarmLimitKind Kind(string kind) => kind switch
    {
        "lo-lo" => AlarmLimitKind.LoLo,
        "lo" => AlarmLimitKind.Lo,
        "hi" => AlarmLimitKind.Hi,
        _ => AlarmLimitKind.HiHi,
    };
}
