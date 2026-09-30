using Dse.Core.Graph;
using Dse.Core.Io;
using Dse.Io;
using Xunit;

namespace Dse.Core.Tests;

/// <summary>
/// Spec 6d §2, the runtime half: the directory publishes a claimed tag
/// read-only and names its claimant, and the image refuses every write to it
/// but the claimant's. Built straight from bindings, as <see cref="TagImageTests"/> is.
/// </summary>
public class ClaimedTagTests
{
    private sealed class Ports
    {
        public OutputPort<bool> Running { get; } = new("Running", "CV001");

        public InputPort<bool> Start { get; } = new("Start", "CV001", defaultValue: false, isRequired: false);

        public InputPort<bool> Permit { get; } = new("Permit", "CV001", defaultValue: true, isRequired: false);

        public InputPort<double> Rate { get; } = new("Rate", "Feed", defaultValue: 5.0, isRequired: false);

        /// <summary>CV001.Permit is claimed by INT01; nothing else is.</summary>
        public TagDirectory Directory(IReadOnlyDictionary<string, string>? claimants = null) => new(
            [
                TagBinding.Read("CV001.Running", Running, "Contactor closed"),
                TagBinding.Write("CV001.Start", Start, "Start command"),
                TagBinding.Write("CV001.Permit", Permit, "Run permit"),
                TagBinding.Write("Feed.Rate", Rate, "kg/s", 0.0, 20.0, "Feed rate"),
            ],
            claimants ?? new Dictionary<string, string>(StringComparer.Ordinal) { ["CV001.Permit"] = "INT01" });

        public TagImage Image()
        {
            TagDirectory directory = Directory();
            foreach (TagBinding binding in directory.Bindings)
            {
                binding.BindExternal();
            }

            var image = new TagImage(directory);
            image.Prime();
            return image;
        }
    }

    [Fact]
    public void AClaimedTagIsPublishedReadOnlyNamingItsClaimantAndEveryOtherTagNamesNone()
    {
        TagDirectory directory = new Ports().Directory();

        TagDescriptor permit = directory.Find("CV001.Permit");
        Assert.Equal((TagAccess.ReadOnly, "INT01"), (permit.Access, permit.ClaimedBy));
        Assert.Equal((TagKind.Bool, "Run permit"), (permit.Kind, permit.Description));

        Assert.Equal(
            new[] { ("CV001.Running", TagAccess.ReadOnly), ("CV001.Start", TagAccess.ReadWrite), ("Feed.Rate", TagAccess.ReadWrite) },
            directory.Tags.Where(t => t.ClaimedBy.Length == 0).Select(t => (t.Name, t.Access)));
    }

    [Fact]
    public void ADirectoryWithoutClaimsPublishesEveryTagAsItsBindingSays()
    {
        TagDirectory directory = new Ports().Directory(new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.All(directory.Tags, t => Assert.Equal(string.Empty, t.ClaimedBy));
        Assert.Equal(TagAccess.ReadWrite, directory.Find("CV001.Permit").Access);
    }

    [Fact]
    public void ToTextAppendsTheClaimantAfterTheDescription()
    {
        string[] lines = new Ports().Directory().ToText().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("CV001.Permit  Bool  ReadOnly  Run permit  claimed by INT01", lines[0]);
        Assert.Equal("CV001.Start  Bool  ReadWrite  Start command", lines[2]);
        Assert.Single(lines, l => l.Contains("claimed by", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExternalWriteToAClaimedTagIsRefusedByNameAndByIndexAndNothingIsQueued()
    {
        TagImage image = new Ports().Image();
        int index = image.Directory.Find("CV001.Permit").Index;

        InvalidOperationException byName = Assert.Throws<InvalidOperationException>(
            () => image.Write("CV001.Permit", TagValue.Bool(true)));
        InvalidOperationException byIndex = Assert.Throws<InvalidOperationException>(
            () => image.Write(index, TagValue.Bool(true)));
        InvalidOperationException check = Assert.Throws<InvalidOperationException>(
            () => image.CheckWritable("CV001.Permit", TagValue.Bool(true)));

        Assert.Equal("Tag 'CV001.Permit' is claimed by INT01; only that block writes it.", byName.Message);
        Assert.Equal(byName.Message, byIndex.Message);
        Assert.Equal(byName.Message, check.Message);
        Assert.Equal(0, image.PendingWrites);
    }

    [Fact]
    public void ABlockWriteFromAnotherOriginIsRefusedWithTheSameMessage()
    {
        TagImage image = new Ports().Image();
        int index = image.Directory.Find("CV001.Permit").Index;

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => image.Write(index, TagValue.Bool(true), "SEQ01"));

        Assert.Equal("Tag 'CV001.Permit' is claimed by INT01; only that block writes it.", error.Message);
        Assert.Equal(0, image.PendingWrites);
    }

    [Fact]
    public void TheClaimantsWriteIsQueued()
    {
        TagImage image = new Ports().Image();
        int index = image.Directory.Find("CV001.Permit").Index;

        image.Write(index, TagValue.Bool(false), "INT01");

        Assert.Equal(1, image.PendingWrites);
    }

    [Fact]
    public void TheKindCheckRunsBeforeTheClaimCheck()
    {
        TagImage image = new Ports().Image();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => image.Write("CV001.Permit", TagValue.Double(1.0)));

        Assert.Equal("Tag 'CV001.Permit' is a Bool tag; cannot write a Double.", error.Message);
    }

    [Fact]
    public void UnclaimedTagsAcceptWritesExactlyAsBefore()
    {
        TagImage image = new Ports().Image();

        image.Write("CV001.Start", TagValue.Bool(true));
        image.Write(image.Directory.Find("Feed.Rate").Index, TagValue.Double(12.0), "INT01");

        Assert.Equal(2, image.PendingWrites);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => image.Write("CV001.Running", TagValue.Bool(true)));
        Assert.Equal("Tag 'CV001.Running' is read-only.", error.Message);
    }
}
