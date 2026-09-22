using Dse.Io;

namespace Dse.Core.Tests.Fakes;

/// <summary>
/// The stub control block the host tests use. Its pins are declared fluently,
/// so one class covers a block with no pins, a block that echoes its first
/// input, a block that commands a tag and a block that raises events.
/// </summary>
public sealed class EchoBlock : IScanBlock
{
    private readonly List<TagRef> _inputs = [];
    private readonly List<TagRef> _writes = [];
    private readonly List<TagSpec> _outputs = [];
    private readonly List<TagSpec> _commands = [];

    public EchoBlock(string id, TimeSpan scanPeriod)
    {
        Id = id;
        ScanPeriod = scanPeriod;
    }

    public string Id { get; }

    public TimeSpan ScanPeriod { get; }

    public IReadOnlyList<TagRef> Inputs => _inputs;

    public IReadOnlyList<TagRef> Writes => _writes;

    public IReadOnlyList<TagSpec> Outputs => _outputs;

    public IReadOnlyList<TagSpec> Commands => _commands;

    /// <summary>The tick of every scan, in order.</summary>
    public List<long> ScanTicks { get; } = [];

    /// <summary>The <c>Elapsed</c> every scan was given, in order.</summary>
    public List<double> ElapsedSeconds { get; } = [];

    /// <summary>What input 0 held on every scan, in order.</summary>
    public List<bool> Seen { get; } = [];

    /// <summary>What command 0 held on every scan, in order.</summary>
    public List<bool> SeenCommand { get; } = [];

    /// <summary>Stop publishing the echo, so the held-output rule can be observed.</summary>
    public bool Silent { get; set; }

    /// <summary>Command write 0 on the next scan, once.</summary>
    public bool WriteOnce { get; set; }

    /// <summary>Raise one event on the next scan, once.</summary>
    public bool RaiseOnce { get; set; }

    /// <summary>Raise one event on every scan.</summary>
    public bool RaiseEveryScan { get; set; }

    /// <summary>Set output 0 to a Double whatever kind it was declared with, to exercise the host's kind check.</summary>
    public bool WrongKind { get; set; }

    public EchoBlock Reads(string tag, TagKind kind = TagKind.Bool)
    {
        _inputs.Add(new TagRef(tag, kind));
        return this;
    }

    public EchoBlock MayWrite(string tag, TagKind kind = TagKind.Bool)
    {
        _writes.Add(new TagRef(tag, kind));
        return this;
    }

    public EchoBlock Publishes(string name, TagKind kind = TagKind.Bool, string unit = "", string description = "")
    {
        _outputs.Add(new TagSpec(name, kind, unit, description));
        return this;
    }

    public EchoBlock Accepts(string name, TagKind kind = TagKind.Bool, string unit = "", string description = "")
    {
        _commands.Add(new TagSpec(name, kind, unit, description));
        return this;
    }

    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        ScanTicks.Add(inputs.Tick);
        ElapsedSeconds.Add(inputs.Elapsed);

        bool value = inputs.InputCount > 0 && inputs.Input(0).AsBool;
        Seen.Add(value);
        SeenCommand.Add(inputs.CommandCount > 0 && inputs.Command(0).AsBool);

        if (!Silent && outputs.OutputCount > 0)
        {
            outputs.Set(0, WrongKind ? TagValue.Double(1.0) : TagValue.Bool(value));
        }

        if (WriteOnce && outputs.WriteCount > 0)
        {
            outputs.Write(0, TagValue.Bool(true));
            WriteOnce = false;
        }

        if (RaiseOnce)
        {
            outputs.Raise("ECHO", "The stub raised an event.");
            RaiseOnce = false;
        }

        if (RaiseEveryScan)
        {
            outputs.Raise("SCANNED", "The stub scanned.");
        }
    }
}
