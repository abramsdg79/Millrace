using Millrace.Core.Graph;

namespace Millrace.Core.Tests.Fakes;

/// <summary>Two gains in series, exposed as a single composite.</summary>
public sealed class TwoStage : CompositeComponent
{
    public TwoStage(string id, double firstFactor, double secondFactor)
        : base(id)
    {
        Gain first = AddChild(new Gain("First", firstFactor));
        Gain second = AddChild(new Gain("Second", secondFactor));

        first.Out.ConnectTo(second.In);

        Expose("In", first.In);
        Expose("Out", second.Out);
    }
}
