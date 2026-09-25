using Dse.Core.Catalogue;
using Dse.Core.Contexts;
using Dse.Core.Faults;
using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Core.Testing;
using Dse.Core.Tests.Fakes;
using Dse.Io;

namespace Dse.Core.Tests.Catalogue;

public class CatalogueConformanceTests
{
    /// <summary>One of everything conformance looks at.</summary>
    private sealed class Widget : FlowComponentBase, IFaultTarget, ITagProvider
    {
        private static readonly FaultDescriptor[] Faults =
        [
            new("jam", "Stops.", new FaultParameter("seconds", "s", 1.0, "How long.")),
        ];

        public Widget(string id, int channels)
            : base(id)
        {
            for (int i = 1; i <= channels; i++)
            {
                AddInput<bool>($"Channel{i}", defaultValue: true);
            }

            Enable = AddInput<bool>("Enable", required: true);
            Level = AddOutput<double>("Level");
            In = AddInlet("In", PayloadKind.Bulk);
        }

        public InputPort<bool> Enable { get; }

        public OutputPort<double> Level { get; }

        public FlowInlet In { get; }

        public override double MassHeld => 0.0;

        public IReadOnlyList<FaultDescriptor> SupportedFaults => Faults;

        public void ApplyFault(string faultId, FaultArguments arguments)
        {
        }

        public void ClearFault(string faultId)
        {
        }

        public IEnumerable<TagBinding> DescribeTags() =>
        [
            TagBinding.Write("Enable", Enable, "Enabled"),
            TagBinding.Read("Level", Level, "fraction", 0.0, 1.0, "Level"),
        ];

        public override void Initialize(in InitContext ctx) => ctx.RegisterTelemetry("Held", "kg");
    }

    private static ComponentDescriptor Honest() =>
        new("widget", ComponentCategory.Flow, "A test widget.", (id, p) => new Widget(id, p.Int("channels")))
        {
            Parameters = [Param.Int("channels", "How many channels.", @default: 2, min: 1)],
            Ports =
            [
                PortSpec.In<bool>("Channel{n}", repeat: new PortRepeat("channels")),
                PortSpec.In<bool>("Enable", required: true),
                PortSpec.Out<double>("Level", "fraction"),
            ],
            FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk)],
            Faults = [new FaultDescriptor("jam", "Stops.", new FaultParameter("seconds", "s", 1.0, "How long."))],
            Tags =
            [
                new TagEntry("Enable", TagKind.Bool, TagAccess.ReadWrite),
                new TagEntry("Level", TagKind.Double, TagAccess.ReadOnly, "fraction"),
            ],
            Telemetry = [new TelemetryKey("Held", "kg")],
        };

    /// <summary>A leaf with nothing but a bool output, used to drive a sibling's writable tag from inside a composite.</summary>
    private sealed class Flag : ComponentBase
    {
        public Flag(string id)
            : base(id)
        {
            Set = AddOutput<bool>("Set");
        }

        public OutputPort<bool> Set { get; }

        public override void Evaluate(in TickContext ctx) => Set.Value = true;
    }

    /// <summary>
    /// The one composite conformance's tests exercise: an alias exposed under a
    /// name that differs from the leaf port, a leaf tag left unexposed, and a
    /// writable leaf tag wired from a sibling's output inside the composite.
    /// </summary>
    private sealed class Station : CompositeComponent
    {
        public Station(string id)
            : base(id)
        {
            Flag source = AddChild(new Flag("Source"));
            A = AddChild(new Widget("A", 1));
            B = AddChild(new Widget("B", 1));

            source.Set.ConnectTo(B.Enable);

            Expose("Output", A.Level);
            Expose("Intake", A.In);
        }

        public Widget A { get; }

        public Widget B { get; }
    }

    private static ComponentDescriptor HonestStation() =>
        new("station", ComponentCategory.Flow, "A test composite.", (id, p) => new Station(id))
        {
            Ports = [PortSpec.Out<double>("Output", "fraction")],
            FlowPorts = [PortSpec.Inlet("Intake", PayloadKind.Bulk)],
            Tags =
            [
                new TagEntry("A.Enable", TagKind.Bool, TagAccess.ReadWrite),
                new TagEntry("Output", TagKind.Double, TagAccess.ReadOnly, "fraction"),
                new TagEntry("B.Enable", TagKind.Bool, TagAccess.ReadOnly),
                new TagEntry("B.Level", TagKind.Double, TagAccess.ReadOnly, "fraction"),
            ],
            Telemetry = [new TelemetryKey("A.Held", "kg"), new TelemetryKey("B.Held", "kg")],
        };

    private static ConformanceReport Check(ComponentDescriptor descriptor) =>
        CatalogueConformance.Check(new CatalogueBuilder().Add(descriptor).Build(), new ConformanceFixtures());

    [Fact]
    public void AnHonestDescriptorHasNoMismatches()
    {
        ConformanceReport report = Check(Honest());

        Assert.Empty(report.Mismatches);
        Assert.Contains(typeof(Widget), report.BuiltTypes);
    }

    [Fact]
    public void AnHonestCompositeDescriptorHasNoMismatches()
    {
        ConformanceReport report = Check(HonestStation());

        Assert.Empty(report.Mismatches);
        Assert.Contains(typeof(Station), report.BuiltTypes);
    }

    [Fact]
    public void ACompositeReportsAliasNotLeafNames()
    {
        ComponentDescriptor honest = HonestStation();
        var wrong = new ComponentDescriptor(honest.Type, honest.Category, honest.Description, honest.Factory)
        {
            Ports = [PortSpec.Out<double>("Level", "fraction")],
            FlowPorts = honest.FlowPorts,
            Tags =
            [
                new TagEntry("A.Enable", TagKind.Bool, TagAccess.ReadWrite),
                new TagEntry("Output", TagKind.Double, TagAccess.ReadOnly, "fraction"),
                new TagEntry("B.Enable", TagKind.Bool, TagAccess.ReadWrite),
                new TagEntry("B.Level", TagKind.Double, TagAccess.ReadOnly, "fraction"),
            ],
            Telemetry = honest.Telemetry,
        };

        List<string> mismatches = Check(wrong).Mismatches.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            [
                "station: signal port 'Level' (Out double) is in the descriptor but not on the instance.",
                "station: signal port 'Output' (Out double) is on the instance but not in the descriptor.",
                "station: tag 'B.Enable' (Bool ReadOnly) is on the instance but not in the descriptor.",
                "station: tag 'B.Enable' (Bool ReadWrite) is in the descriptor but not on the instance.",
            ],
            mismatches);
    }

    [Fact]
    public void AFixtureChangesTheProbeAndTheExpansion()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder().Add(Honest()).Build();

        ConformanceReport report = CatalogueConformance.Check(
            catalogue, new ConformanceFixtures().Parameters("widget", """{ "channels": 4 }"""));

        Assert.Empty(report.Mismatches);
    }

    [Fact]
    public void ReportsAPortTheDescriptorForgot()
    {
        ComponentDescriptor honest = Honest();
        var forgetful = new ComponentDescriptor(honest.Type, honest.Category, honest.Description, honest.Factory)
        {
            Parameters = honest.Parameters,
            Ports = honest.Ports.Where(p => p.Name != "Level").ToList(),
            FlowPorts = honest.FlowPorts,
            Faults = honest.Faults,
            Tags = honest.Tags,
            Telemetry = honest.Telemetry,
        };

        string mismatch = Assert.Single(Check(forgetful).Mismatches);

        Assert.Equal("widget: signal port 'Level' (Out double) is on the instance but not in the descriptor.", mismatch);
    }

    [Fact]
    public void ReportsAPortTheDescriptorInvented()
    {
        ComponentDescriptor honest = Honest();
        var inventive = new ComponentDescriptor(honest.Type, honest.Category, honest.Description, honest.Factory)
        {
            Parameters = honest.Parameters,
            Ports = [.. honest.Ports, PortSpec.Out<bool>("Ghost")],
            FlowPorts = honest.FlowPorts,
            Faults = honest.Faults,
            Tags = honest.Tags,
            Telemetry = honest.Telemetry,
        };

        string mismatch = Assert.Single(Check(inventive).Mismatches);

        Assert.Equal("widget: signal port 'Ghost' (Out bool) is in the descriptor but not on the instance.", mismatch);
    }

    [Fact]
    public void ReportsEveryOtherAspect()
    {
        ComponentDescriptor honest = Honest();
        var wrong = new ComponentDescriptor(honest.Type, honest.Category, honest.Description, honest.Factory)
        {
            Parameters = honest.Parameters,
            Ports = honest.Ports,
            FlowPorts = [PortSpec.Inlet("In", PayloadKind.Discrete)],
            Faults = [new FaultDescriptor("jam", "Stops.")],
            Tags = [new TagEntry("Enable", TagKind.Bool, TagAccess.ReadOnly), new TagEntry("Level", TagKind.Double, TagAccess.ReadOnly)],
            Telemetry = [new TelemetryKey("Held", "t")],
            Provides = [typeof(IMaterialObservable)],
        };

        IReadOnlyList<string> mismatches = Check(wrong).Mismatches;

        Assert.Contains(mismatches, m => m.Contains("flow port 'In' (In Discrete) is in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("flow port 'In' (In Bulk) is on the instance", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("fault 'jam()' is in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("fault 'jam(seconds)' is on the instance", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("tag 'Enable' (Bool ReadOnly) is in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("telemetry 'Held' is 'kg' on the instance but 't' in the descriptor", StringComparison.Ordinal));
        Assert.Contains(mismatches, m => m.Contains("provides IMaterialObservable in the descriptor, but the instance cannot supply it", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsAFixtureThatDoesNotBind()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder().Add(Honest()).Build();

        ConformanceReport report = CatalogueConformance.Check(
            catalogue, new ConformanceFixtures().Parameters("widget", """{ "channels": 0 }"""));

        string mismatch = Assert.Single(report.Mismatches);
        Assert.StartsWith("widget: the fixture does not bind — $.channels:", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAFactoryThatThrows()
    {
        var broken = new ComponentDescriptor("broken", ComponentCategory.Signal, "Asks for the wrong name.", (id, p) => new UnitDelay<bool>(id, p.Bool("nope")));

        string mismatch = Assert.Single(Check(broken).Mismatches);

        Assert.StartsWith("broken: the factory threw KeyNotFoundException", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAnUnlistedCapabilityThatSomeReferenceNeeds()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ComponentDescriptor("belt", ComponentCategory.Flow, "A belt.", (id, p) => new BulkBelt(id, 10.0, 0.5, 2.0, 100.0))
            {
                Ports =
                [
                    PortSpec.In<double>("Speed"), PortSpec.In<double>("AmbientTemperature"),
                    PortSpec.Out<double>("Load"), PortSpec.Out<double>("PeakLinearDensity"),
                ],
                FlowPorts = [PortSpec.Inlet("In", PayloadKind.Bulk), PortSpec.Outlet("Out", PayloadKind.Bulk)],
                Telemetry = [new TelemetryKey("Load", "kg")],
            })
            .Add(new ComponentDescriptor("watcher", ComponentCategory.Signal, "Watches a belt.", (id, p) => new UnitDelay<bool>(id))
            {
                Parameters = [Param.Reference<IMaterialObservable>("belt", "What to watch.")],
                Ports = [PortSpec.In<bool>("In"), PortSpec.Out<bool>("Out")],
            })
            .Build();
        ConformanceFixtures fixtures = new ConformanceFixtures()
            .Node(new BulkBelt("STANDIN", 10.0, 0.5, 2.0, 100.0))
            .Parameters("watcher", """{ "belt": "STANDIN" }""");

        string mismatch = Assert.Single(CatalogueConformance.Check(catalogue, fixtures).Mismatches);

        Assert.Equal("belt: the instance can supply IMaterialObservable, which a reference parameter in this catalogue needs, but the descriptor does not list it under Provides.", mismatch);
    }

    [Fact]
    public void BuildsEveryObjectDescriptor()
    {
        ComponentCatalogue catalogue = new CatalogueBuilder()
            .Add(new ObjectDescriptor(ObjectSlots.Hold, "for-seconds", "Waits.", p => Tuple.Create(p.Double("seconds")))
            {
                Parameters = [Param.Double("seconds", "How long.")],
            })
            .Build();

        ConformanceReport missing = CatalogueConformance.Check(catalogue, new ConformanceFixtures());
        ConformanceReport given = CatalogueConformance.Check(
            catalogue, new ConformanceFixtures().ObjectParameters(ObjectSlots.Hold, "for-seconds", """{ "seconds": 5 }"""));

        Assert.StartsWith("hold 'for-seconds': the fixture does not bind — $.seconds:", Assert.Single(missing.Mismatches), StringComparison.Ordinal);
        Assert.Empty(given.Mismatches);
        Assert.Contains(typeof(Tuple<double>), given.BuiltTypes);
    }

    private static BlockDescriptor EchoDescriptor(Func<string, ParameterValues, IReadOnlyList<(TagSpec Spec, TagAccess Access)>> owned) =>
        new("echo",
            "Echoes a tag.",
            owned,
            (id, period, p) => new EchoBlock(id, period).Reads(p.Tag("input")).Publishes("Q").Accepts("Cmd"))
        {
            Parameters =
            [
                Param.Tag("input", "The tag echoed.", TagKind.Bool),
                Param.Bool("extra", "Declare one more tag than the block has.", @default: false),
            ],
        };

    private static IReadOnlyList<(TagSpec Spec, TagAccess Access)> HonestTags(string id, ParameterValues p)
    {
        var tags = new List<(TagSpec Spec, TagAccess Access)>
        {
            (new TagSpec($"{id}.Q", TagKind.Bool), TagAccess.ReadOnly),
            (new TagSpec($"{id}.Cmd", TagKind.Bool), TagAccess.ReadWrite),
        };
        if (p.Bool("extra"))
        {
            tags.Add((new TagSpec($"{id}.Extra", TagKind.Bool), TagAccess.ReadOnly));
        }

        return tags;
    }

    private static ConformanceReport CheckBlock(BlockDescriptor descriptor, params string[] fixtures)
    {
        var f = new ConformanceFixtures();
        foreach (string json in fixtures)
        {
            f.BlockParameters("echo", json);
        }

        return CatalogueConformance.Check(new CatalogueBuilder().AddBlock(descriptor).Build(), f);
    }

    [Fact]
    public void AnHonestBlockDescriptorHasNoMismatches()
    {
        ConformanceReport report = CheckBlock(EchoDescriptor(HonestTags), """{ "input": "X.In" }""");

        Assert.Empty(report.Mismatches);
        Assert.Contains(typeof(EchoBlock), report.BuiltTypes);
    }

    [Fact]
    public void ReportsAnOwnedTagTheInstanceLacks()
    {
        ConformanceReport report = CheckBlock(EchoDescriptor(HonestTags), """{ "input": "X.In", "extra": true }""");

        Assert.Equal(
            "echo: owned tag 'probe.Extra' (Bool ReadOnly) is in the descriptor but not on the instance.",
            Assert.Single(report.Mismatches));
    }

    [Fact]
    public void ReportsAnOwnedTagTheDescriptorForgot()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor((id, p) => [(new TagSpec($"{id}.Q", TagKind.Bool), TagAccess.ReadOnly)]),
            """{ "input": "X.In" }""");

        Assert.Equal(
            "echo: owned tag 'probe.Cmd' (Bool ReadWrite) is on the instance but not in the descriptor.",
            Assert.Single(report.Mismatches));
    }

    [Fact]
    public void ReportsAKindOrAccessMismatch()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor((id, p) =>
            [
                (new TagSpec($"{id}.Q", TagKind.Double), TagAccess.ReadOnly),
                (new TagSpec($"{id}.Cmd", TagKind.Bool), TagAccess.ReadOnly),
            ]),
            """{ "input": "X.In" }""");

        Assert.Equal(
            new[]
            {
                "echo: owned tag 'probe.Cmd' (Bool ReadOnly) is in the descriptor but not on the instance.",
                "echo: owned tag 'probe.Q' (Double ReadOnly) is in the descriptor but not on the instance.",
                "echo: owned tag 'probe.Cmd' (Bool ReadWrite) is on the instance but not in the descriptor.",
                "echo: owned tag 'probe.Q' (Bool ReadOnly) is on the instance but not in the descriptor.",
            },
            report.Mismatches);
    }

    [Fact]
    public void ReportsAUnitOrDescriptionMismatch()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor((id, p) =>
            [
                (new TagSpec($"{id}.Q", TagKind.Bool, "s"), TagAccess.ReadOnly),
                (new TagSpec($"{id}.Cmd", TagKind.Bool, "", "A command"), TagAccess.ReadWrite),
            ]),
            """{ "input": "X.In" }""");

        Assert.Equal(
            new[]
            {
                "echo: owned tag 'probe.Q' has unit 's' in the descriptor but '' on the instance.",
                "echo: owned tag 'probe.Cmd' is described 'A command' in the descriptor but '' on the instance.",
            },
            report.Mismatches);
    }

    [Fact]
    public void ReportsAParameterTheFactoryReadsButTheDescriptorDoesNotDeclare()
    {
        var descriptor = new BlockDescriptor(
            "echo",
            "Echoes a tag.",
            HonestTags,
            (id, period, p) => new EchoBlock(id, TimeSpan.FromSeconds(p.Double("gain"))).Publishes("Q").Accepts("Cmd"))
        {
            Parameters = [Param.Bool("extra", "Declare one more tag than the block has.", @default: false)],
        };

        ConformanceReport report = CheckBlock(descriptor, "{}");

        string mismatch = Assert.Single(report.Mismatches);
        Assert.StartsWith("echo: the factory threw KeyNotFoundException:", mismatch, StringComparison.Ordinal);
        Assert.Contains("'gain', which its descriptor does not declare", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksEveryFixtureOfABlockTypeAndLabelsEach()
    {
        ConformanceReport report = CheckBlock(
            EchoDescriptor(HonestTags),
            """{ "input": "X.In" }""",
            """{ "input": "X.In", "extra": true }""");

        Assert.Equal(
            "echo (fixture 2): owned tag 'probe.Extra' (Bool ReadOnly) is in the descriptor but not on the instance.",
            Assert.Single(report.Mismatches));
    }

    [Fact]
    public void ReportsAPinGetterThatThrowsInsteadOfThrowing()
    {
        var throwing = new BlockDescriptor("echo", "Echoes a tag.", (id, p) => [], (id, period, p) => new ThrowingOutputsBlock(id, period));

        ConformanceReport report = CheckBlock(throwing, "{}");

        Assert.Equal(
            "echo: the instance's Outputs failed with InvalidOperationException: The stub has no outputs.",
            Assert.Single(report.Mismatches));
    }

    /// <summary>A block whose Outputs getter throws.</summary>
    private sealed class ThrowingOutputsBlock(string id, TimeSpan scanPeriod) : IScanBlock
    {
        public string Id { get; } = id;

        public TimeSpan ScanPeriod { get; } = scanPeriod;

        public IReadOnlyList<TagRef> Inputs => [];

        public IReadOnlyList<TagRef> Writes => [];

        public IReadOnlyList<TagSpec> Outputs => throw new InvalidOperationException("The stub has no outputs.");

        public IReadOnlyList<TagSpec> Commands => [];

        public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
        {
        }
    }
}
