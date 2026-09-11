namespace Dse.Core.Randomness;

/// <summary>
/// SplitMix64. Small, fast, and — unlike <see cref="System.Random"/> — a fixed
/// algorithm whose output will not change with a runtime upgrade.
/// </summary>
public sealed class DeterministicRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;
    private double _spareGaussian;
    private bool _hasSpareGaussian;

    public DeterministicRandom(ulong seed) => _state = seed;

    public ulong NextUInt64()
    {
        unchecked
        {
            _state += GoldenGamma;
        }

        return Hash64.Mix(_state);
    }

    /// <summary>Uniform in [0, 1). Uses the top 53 bits, the full mantissa of a double.</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Standard normal, via the polar Box-Muller method.</summary>
    public double NextGaussian()
    {
        if (_hasSpareGaussian)
        {
            _hasSpareGaussian = false;
            return _spareGaussian;
        }

        double u, v, s;
        do
        {
            u = (2.0 * NextDouble()) - 1.0;
            v = (2.0 * NextDouble()) - 1.0;
            s = (u * u) + (v * v);
        }
        while (s >= 1.0 || s == 0.0);

        double scale = Math.Sqrt(-2.0 * Math.Log(s) / s);
        _spareGaussian = v * scale;
        _hasSpareGaussian = true;
        return u * scale;
    }
}
