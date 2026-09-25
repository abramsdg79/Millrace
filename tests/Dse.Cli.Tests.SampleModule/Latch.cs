using Dse.Core.Catalogue;
using Dse.Io;

namespace Dse.Cli.Tests.SampleModule;

/// <summary>
/// A set/reset latch, reset dominant: the worked example of a block type a
/// plugin registers, in docs/authoring-a-component.md. <c>Q</c> goes true while
/// <c>set</c> is true, false while <c>reset</c> is true, and holds in between.
/// </summary>
public sealed class Latch : IScanBlock
{
    public static BlockDescriptor Descriptor { get; } = new(
        "latch",
        "A set/reset latch, reset dominant: Q follows set until reset clears it.",
        (id, p) => [(new TagSpec($"{id}.Q", TagKind.Bool, string.Empty, "Latched"), TagAccess.ReadOnly)],
        (id, period, p) => new Latch(id, p.Tag("set"), p.Tag("reset"), period))
    {
        Parameters =
        [
            Param.Tag("set", "Sets the latch while true.", TagKind.Bool),
            Param.Tag("reset", "Clears the latch while true; wins over set.", TagKind.Bool),
        ],
    };

    private bool _q;

    public Latch(string id, string set, string reset, TimeSpan scanPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = [new TagRef(set, TagKind.Bool), new TagRef(reset, TagKind.Bool)];
    }

    public string Id { get; }

    public TimeSpan ScanPeriod { get; }

    public IReadOnlyList<TagRef> Inputs { get; }

    public IReadOnlyList<TagRef> Writes { get; } = [];

    public IReadOnlyList<TagSpec> Outputs { get; } = [new TagSpec("Q", TagKind.Bool, string.Empty, "Latched")];

    public IReadOnlyList<TagSpec> Commands { get; } = [];

    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        if (inputs.Input(1).AsBool)
        {
            _q = false;
        }
        else if (inputs.Input(0).AsBool)
        {
            _q = true;
        }

        outputs.Set(0, TagValue.Bool(_q));
    }
}
