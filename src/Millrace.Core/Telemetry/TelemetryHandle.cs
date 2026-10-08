namespace Millrace.Core.Telemetry;

/// <summary>
/// A write handle for one telemetry channel. Resolved once during Initialize so
/// that writing during a tick costs an array store and no dictionary lookup.
/// </summary>
public readonly struct TelemetryHandle
{
    private readonly TelemetryRegistry? _registry;
    private readonly int _index;

    internal TelemetryHandle(TelemetryRegistry registry, int index)
    {
        _registry = registry;
        _index = index;
    }

    public void Write(double value) => _registry?.WriteAt(_index, value);
}
