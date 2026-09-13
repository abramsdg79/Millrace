using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Xunit;

namespace Dse.Core.Tests;

public class ExternalInputTests
{
    [Fact]
    public void UnconnectedInputReadsItsDefaultUntilDriven()
    {
        var port = new InputPort<double>("Setpoint", "T", defaultValue: 4.0, isRequired: false);

        Assert.False(port.IsExternallyDriven);
        port.DriveExternally();

        Assert.True(port.IsExternallyDriven);
        Assert.Equal(4.0, port.Value);

        port.SetExternal(9.5);

        Assert.Equal(9.5, port.Value);
        Assert.Equal(9.5, port.ExternalValue);
    }

    [Fact]
    public void ExternallyDrivenRequiredInputIsNotMissing()
    {
        var port = new InputPort<bool>("Enable", "T", defaultValue: false, isRequired: true);
        Assert.True(port.IsMissingRequiredConnection);

        port.DriveExternally();

        Assert.False(port.IsMissingRequiredConnection);
    }

    [Fact]
    public void LatchedExternalInputReadsOneCaptureLate()
    {
        var port = new InputPort<double>("Demand", "T", defaultValue: 0.0, isRequired: false, isLatched: true);
        port.DriveExternally();

        port.SetExternal(3.0);
        Assert.Equal(0.0, port.Value);

        port.Capture();
        Assert.Equal(3.0, port.Value);
    }

    [Fact]
    public void DrivingAConnectedInputThrows()
    {
        var source = new OutputPort<double>("Out", "S");
        var port = new InputPort<double>("In", "T", defaultValue: 0.0, isRequired: false);
        source.ConnectTo(port);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(port.DriveExternally);
        Assert.Contains("S.Out", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectingADrivenInputThrows()
    {
        var source = new OutputPort<double>("Out", "S");
        var port = new InputPort<double>("In", "T", defaultValue: 0.0, isRequired: false);
        port.DriveExternally();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => source.ConnectTo(port));
        Assert.Contains("externally", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectedInputStillReadsItsSourceNotTheExternalSlot()
    {
        var source = new ConstantSource("S", 2.0);
        var recorder = new Recorder("R");
        source.Out.ConnectTo(recorder.In);

        Assert.False(recorder.In.IsExternallyDriven);
    }
}
