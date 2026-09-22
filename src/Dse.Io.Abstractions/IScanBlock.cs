namespace Dse.Io;

/// <summary>
/// A control block: a pure function of the plant it reads and the state it
/// keeps, scanned at its own period by the host. It never sees a directory, a
/// binding, a clock or a log.
/// </summary>
/// <remarks>
/// Three pin classes. <see cref="Inputs"/> are plant tags it reads;
/// <see cref="Writes"/> are plant tags it may command; <see cref="Outputs"/> and
/// <see cref="Commands"/> are tags it owns, published by the host as
/// <c>&lt;Id&gt;.&lt;Name&gt;</c>. Every list is fixed for the life of the
/// block: the host reads them once, at <c>Build()</c>.
/// </remarks>
public interface IScanBlock
{
    /// <summary>Unique across components and blocks; prefixes every owned tag.</summary>
    string Id { get; }

    /// <summary>How often the block scans. A positive whole number of time steps.</summary>
    TimeSpan ScanPeriod { get; }

    /// <summary>Plant tags this block reads, by full name.</summary>
    IReadOnlyList<TagRef> Inputs { get; }

    /// <summary>Plant tags this block may command, by full name. Each must be read-write.</summary>
    IReadOnlyList<TagRef> Writes { get; }

    /// <summary>Tags this block owns and publishes read-only, named relative to the block.</summary>
    IReadOnlyList<TagSpec> Outputs { get; }

    /// <summary>Tags this block owns and publishes read-write, named relative to the block.</summary>
    IReadOnlyList<TagSpec> Commands { get; }

    /// <summary>One scan. Must not throw, must not block, and must not keep either argument.</summary>
    /// <remarks>
    /// A scan that throws aborts the tick before the clock advances and the
    /// block is not rescheduled; a Simulation that has thrown out of Tick()
    /// must be rebuilt.
    /// </remarks>
    void Scan(in ScanInputs inputs, ref ScanOutputs outputs);
}
