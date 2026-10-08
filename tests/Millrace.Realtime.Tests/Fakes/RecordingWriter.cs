using Millrace.Io;

namespace Millrace.Realtime.Tests.Fakes;

/// <summary>An <see cref="ITagWriter"/> that remembers what it was asked to write.</summary>
public sealed class RecordingWriter : ITagWriter
{
    public RecordingWriter(ITagDirectory directory) => Directory = directory;

    public ITagDirectory Directory { get; }

    public List<(int Index, TagValue Value)> Writes { get; } = [];

    public void Write(int index, TagValue value) => Writes.Add((index, value));

    public void Write(string name, TagValue value) => Write(Directory.Find(name).Index, value);
}
