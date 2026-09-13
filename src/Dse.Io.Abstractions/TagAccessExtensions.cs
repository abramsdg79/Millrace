namespace Dse.Io;

/// <summary>The typed front door over <see cref="ITagReader"/> and <see cref="ITagWriter"/> (spec 9.2).</summary>
public static class TagAccessExtensions
{
    /// <summary>Reads a bool tag by name.</summary>
    public static bool ReadBool(this ITagReader reader, string name) => reader.Read(name).AsBool;

    /// <summary>Reads a double tag by name.</summary>
    public static double ReadDouble(this ITagReader reader, string name) => reader.Read(name).AsDouble;

    /// <summary>Reads a long tag by name.</summary>
    public static long ReadInt64(this ITagReader reader, string name) => reader.Read(name).AsInt64;

    /// <summary>Reads a bool tag through a handle.</summary>
    public static bool ReadBool(this ITagReader reader, TagHandle<bool> handle) => reader.Read(handle.Index).AsBool;

    /// <summary>Reads a double tag through a handle.</summary>
    public static double ReadDouble(this ITagReader reader, TagHandle<double> handle) => reader.Read(handle.Index).AsDouble;

    /// <summary>Reads a long tag through a handle.</summary>
    public static long ReadInt64(this ITagReader reader, TagHandle<long> handle) => reader.Read(handle.Index).AsInt64;

    /// <summary>Reads any tag's value with quality through a handle.</summary>
    public static TagValue Read<T>(this ITagReader reader, TagHandle<T> handle)
        where T : unmanaged => reader.Read(handle.Index);

    /// <summary>Queues a bool by name.</summary>
    public static void WriteBool(this ITagWriter writer, string name, bool value) => writer.Write(name, TagValue.Bool(value));

    /// <summary>Queues a double by name.</summary>
    public static void WriteDouble(this ITagWriter writer, string name, double value) => writer.Write(name, TagValue.Double(value));

    /// <summary>Queues a long by name.</summary>
    public static void WriteInt64(this ITagWriter writer, string name, long value) => writer.Write(name, TagValue.Int64(value));

    /// <summary>Queues a bool through a handle.</summary>
    public static void WriteBool(this ITagWriter writer, TagHandle<bool> handle, bool value) => writer.Write(handle.Index, TagValue.Bool(value));

    /// <summary>Queues a double through a handle.</summary>
    public static void WriteDouble(this ITagWriter writer, TagHandle<double> handle, double value) => writer.Write(handle.Index, TagValue.Double(value));

    /// <summary>Queues a long through a handle.</summary>
    public static void WriteInt64(this ITagWriter writer, TagHandle<long> handle, long value) => writer.Write(handle.Index, TagValue.Int64(value));

    /// <summary>Queues a bool by index.</summary>
    public static void WriteBool(this ITagWriter writer, int index, bool value) => writer.Write(index, TagValue.Bool(value));

    /// <summary>Queues a double by index.</summary>
    public static void WriteDouble(this ITagWriter writer, int index, double value) => writer.Write(index, TagValue.Double(value));

    /// <summary>Queues a long by index.</summary>
    public static void WriteInt64(this ITagWriter writer, int index, long value) => writer.Write(index, TagValue.Int64(value));
}
