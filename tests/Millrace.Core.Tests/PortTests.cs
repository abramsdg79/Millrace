using Millrace.Core.Graph;
using Xunit;

namespace Millrace.Core.Tests;

public class PortTests
{
    [Fact]
    public void UnconnectedInputReturnsItsDefault()
    {
        var input = new InputPort<double>("Speed", "M1", defaultValue: 1.5, isRequired: false);

        Assert.Equal(1.5, input.Value);
    }

    [Fact]
    public void ConnectedInputTracksItsSource()
    {
        var output = new OutputPort<double>("Out", "A");
        var input = new InputPort<double>("In", "B", defaultValue: 0.0, isRequired: false);

        output.ConnectTo(input);
        output.Value = 42.0;

        Assert.Equal(42.0, input.Value);
    }

    [Fact]
    public void OneOutputMayDriveManyInputs()
    {
        var output = new OutputPort<bool>("Out", "A");
        var first = new InputPort<bool>("In", "B", defaultValue: false, isRequired: false);
        var second = new InputPort<bool>("In", "C", defaultValue: false, isRequired: false);

        output.ConnectTo(first);
        output.ConnectTo(second);
        output.Value = true;

        Assert.True(first.Value);
        Assert.True(second.Value);
    }

    [Fact]
    public void ConnectingASecondSourceToAnInputThrows()
    {
        var first = new OutputPort<double>("Out", "A");
        var second = new OutputPort<double>("Out", "B");
        var input = new InputPort<double>("In", "C", defaultValue: 0.0, isRequired: false);

        first.ConnectTo(input);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(() => second.ConnectTo(input));
        Assert.Contains("C.In", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiredUnconnectedInputIsFlagged()
    {
        var required = new InputPort<double>("In", "A", defaultValue: 0.0, isRequired: true);
        var optional = new InputPort<double>("In", "B", defaultValue: 0.0, isRequired: false);
        var output = new OutputPort<double>("Out", "C");

        Assert.True(required.IsMissingRequiredConnection);
        Assert.False(optional.IsMissingRequiredConnection);
        Assert.False(output.IsMissingRequiredConnection);

        output.ConnectTo(required);
        Assert.False(required.IsMissingRequiredConnection);
    }

    [Fact]
    public void SourcePortExposesTheUpstreamPortForTheResolver()
    {
        var output = new OutputPort<double>("Out", "A");
        var input = new InputPort<double>("In", "B", defaultValue: 0.0, isRequired: false);

        Assert.Null(input.SourcePort);
        output.ConnectTo(input);
        Assert.Same(output, input.SourcePort);
    }
}
