namespace Dse.Core.Telemetry;

public sealed record TelemetryChannel(string Key, string Unit);

/// <summary>
/// The god view: true internal values published by components, unaffected by
/// sensor noise, drift or failure. Tests assert on these; controllers must not.
/// </summary>
public sealed class TelemetryRegistry
{
    private readonly List<TelemetryChannel> _channels = [];
    private readonly Dictionary<string, int> _indexByKey = new(StringComparer.Ordinal);
    private double[] _values = new double[16];

    public IReadOnlyList<TelemetryChannel> Channels => _channels;

    public TelemetryHandle Register(string key, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(unit);

        if (_indexByKey.ContainsKey(key))
        {
            throw new InvalidOperationException(
                $"Telemetry key '{key}' is already registered. Keys must be unique.");
        }

        int index = _channels.Count;
        _channels.Add(new TelemetryChannel(key, unit));
        _indexByKey[key] = index;

        if (index >= _values.Length)
        {
            Array.Resize(ref _values, _values.Length * 2);
        }

        return new TelemetryHandle(this, index);
    }

    public double Read(string key)
    {
        if (!_indexByKey.TryGetValue(key, out int index))
        {
            throw new KeyNotFoundException($"No telemetry channel named '{key}'.");
        }

        return _values[index];
    }

    public bool TryRead(string key, out double value)
    {
        if (_indexByKey.TryGetValue(key, out int index))
        {
            value = _values[index];
            return true;
        }

        value = 0.0;
        return false;
    }

    internal void WriteAt(int index, double value) => _values[index] = value;
}
