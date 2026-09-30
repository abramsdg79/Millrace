using System.Collections.Concurrent;
using Dse.Core.Contexts;
using Dse.Io;

namespace Dse.Core.Io;

/// <summary>
/// The I/O image (spec 9.3): a double-buffered snapshot any thread may read,
/// and a lock-free write queue drained at phase 1. The simulation thread is the
/// only writer of the model; the published array is never mutated after the
/// swap, so readers take no lock and can never see a half-updated plant.
/// </summary>
public sealed class TagImage : ITagReader, ITagWriter
{
    private readonly TagBinding[] _bindings;
    private readonly ConcurrentQueue<PendingWrite> _writes = new();
    private TagValue[] _front;
    private long _snapshotTick = -1;
    private bool _published;
    private IActionRecorder? _recorder;

    internal TagImage(TagDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        Directory = directory;
        _bindings = directory.Bindings;
        _front = new TagValue[_bindings.Length];
    }

    /// <summary>The directory this image indexes.</summary>
    public TagDirectory Directory { get; }

    ITagDirectory ITagReader.Directory => Directory;

    ITagDirectory ITagWriter.Directory => Directory;

    /// <inheritdoc/>
    public long SnapshotTick => Volatile.Read(ref _snapshotTick);

    /// <summary>Writes queued and not yet applied.</summary>
    public int PendingWrites => _writes.Count;

    /// <inheritdoc/>
    public TagValue Read(int index)
    {
        TagValue[] front = Volatile.Read(ref _front);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, front.Length);
        return front[index];
    }

    /// <inheritdoc/>
    public TagValue Read(string name) => Read(Directory.Find(name).Index);

    /// <summary>
    /// The whole published image as one immutable view: every value in it
    /// belongs to the same tick. Use this, not repeated <see cref="Read(int)"/>
    /// calls, when correlated tags must be read together.
    /// </summary>
    public ReadOnlyMemory<TagValue> Snapshot() => Volatile.Read(ref _front);

    /// <inheritdoc/>
    public TagHandle<T> Handle<T>(string name)
        where T : unmanaged
    {
        TagKind wanted = TagValue.KindOf<T>();
        TagDescriptor tag = Directory.Find(name);
        if (tag.Kind != wanted)
        {
            throw new InvalidOperationException(
                $"Tag '{name}' is a {tag.Kind} tag; a TagHandle<{typeof(T).Name}> needs {wanted}.");
        }

        return new TagHandle<T>(tag.Index, tag.Name);
    }

    /// <inheritdoc/>
    public void Write(int index, TagValue value)
    {
        Check(index, value, origin: null);
        _writes.Enqueue(new PendingWrite(index, value, null));
    }

    /// <summary>
    /// Queues a write a control block issued. It lands exactly as an external
    /// write does — at phase 1 of the next tick, in enqueue order, so the last
    /// writer still wins — but it is logged <c>Set to … by &lt;origin&gt;.</c>
    /// and never reaches the action recorder.
    /// </summary>
    internal void Write(int index, TagValue value, string origin)
    {
        Check(index, value, origin);
        _writes.Enqueue(new PendingWrite(index, value, origin));
    }

    /// <inheritdoc/>
    public void Write(string name, TagValue value) => Write(Directory.Find(name).Index, value);

    /// <summary>
    /// Resolves a name and runs exactly the checks <see cref="Write(int, TagValue)"/>
    /// runs, without queueing anything. This is what lets
    /// <c>Simulation.WriteAt</c> fail at the call site rather than mid-run.
    /// </summary>
    internal int CheckWritable(string name, TagValue value)
    {
        int index = Directory.Find(name).Index;
        Check(index, value, origin: null);
        return index;
    }

    /// <summary>
    /// The one place a write lands: applies the value to a binding and logs it
    /// as <c>WRITE</c>. An external write (no origin) is reported to the action
    /// recorder; a block's write is attributed in the log and is not, because
    /// replaying the external actions re-runs the block, which issues it again.
    /// Phase 1 only, called both from the queued-write drain and from a
    /// scheduled <c>WriteEvent</c>.
    /// </summary>
    internal void ApplyNow(int index, TagValue value, string? origin, in TickContext ctx)
    {
        TagBinding binding = _bindings[index];
        binding.Apply(value);
        if (origin is null)
        {
            ctx.Log(binding.Name, "WRITE", $"Set to {value}.");
            _recorder?.Wrote(ctx.Tick, binding.Name, value);
        }
        else
        {
            ctx.Log(binding.Name, "WRITE", $"Set to {value} by {origin}.");
        }
    }

    /// <summary>
    /// Refuses a write that cannot land: an index out of range, a read-only
    /// binding, a kind mismatch — and a claimed tag written by anything but its
    /// claimant. <paramref name="origin"/> is the writing block's id, or null
    /// for an external write.
    /// </summary>
    private void Check(int index, TagValue value, string? origin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _bindings.Length);

        TagBinding binding = _bindings[index];
        if (binding.Access != TagAccess.ReadWrite)
        {
            throw new InvalidOperationException($"Tag '{binding.Name}' is read-only.");
        }

        if (binding.Kind != value.Kind)
        {
            throw new InvalidOperationException(
                $"Tag '{binding.Name}' is a {binding.Kind} tag; cannot write a {value.Kind}.");
        }

        string? claimant = Directory.ClaimantOf(index);
        if (claimant is not null && !string.Equals(origin, claimant, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Tag '{binding.Name}' is claimed by {claimant}; only that block writes it.");
        }
    }

    /// <summary>The recorder the simulation attached, or none. Set once, through <c>Simulation.AttachActionRecorder</c>.</summary>
    internal void SetActionRecorder(IActionRecorder recorder) => _recorder = recorder;

    /// <summary>Captures the initial values before the first tick. Called from <c>Simulation.Initialize</c>.</summary>
    internal void Prime()
    {
        var values = new TagValue[_bindings.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = _bindings[i].Capture();
        }

        Volatile.Write(ref _front, values);
    }

    /// <summary>
    /// Phase 4: captures every binding into a fresh array, diffs it against the
    /// previous snapshot, and publishes it. The first publish is wholly dirty.
    /// Returns the published array and mask so phase 5 can wrap them in a frame
    /// without copying.
    /// </summary>
    internal (TagValue[] Values, DirtyMask Dirty) Publish(long tick)
    {
        TagValue[] previous = _front;
        var values = new TagValue[_bindings.Length];
        var words = new ulong[DirtyMask.WordsFor(values.Length)];
        int count = 0;

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = _bindings[i].Capture();
            if (!_published || values[i] != previous[i])
            {
                words[i >> 6] |= 1UL << (i & 63);
                count++;
            }
        }

        Volatile.Write(ref _front, values);
        Volatile.Write(ref _snapshotTick, tick);
        _published = true;
        return (values, DirtyMask.FromBits(words, values.Length, count));
    }

    /// <summary>
    /// Phase 1: applies every write queued before entry, in enqueue order, and
    /// logs each as <c>WRITE</c> from the tag name (R24).
    /// </summary>
    internal int ApplyPendingWrites(in TickContext ctx)
    {
        int budget = _writes.Count;
        int applied = 0;
        while (applied < budget && _writes.TryDequeue(out PendingWrite write))
        {
            ApplyNow(write.Index, write.Value, write.Origin, in ctx);
            applied++;
        }

        return applied;
    }

    private readonly record struct PendingWrite(int Index, TagValue Value, string? Origin);
}
