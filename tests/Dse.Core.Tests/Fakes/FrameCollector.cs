using Dse.Io;

namespace Dse.Core.Tests.Fakes;

/// <summary>Keeps every frame it is handed.</summary>
public sealed class FrameCollector : ITickFrameSink
{
    public List<TickFrame> Frames { get; } = [];

    public void Publish(TickFrame frame) => Frames.Add(frame);
}
