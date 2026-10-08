using System.Text;

namespace Millrace.Core.Randomness;

/// <summary>
/// Stable 64-bit hashing. Unlike <see cref="string.GetHashCode()"/> these values
/// are identical in every process and every run, which is what makes
/// per-component seed derivation reproducible.
/// </summary>
public static class Hash64
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>FNV-1a over the UTF-8 bytes of <paramref name="value"/>.</summary>
    public static ulong OfString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ulong hash = FnvOffsetBasis;
        foreach (byte b in Encoding.UTF8.GetBytes(value))
        {
            unchecked
            {
                hash ^= b;
                hash *= FnvPrime;
            }
        }

        return hash;
    }

    /// <summary>The SplitMix64 finaliser. Avalanches a counter into a usable value.</summary>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Derives a component's stream seed from the master seed and its id.</summary>
    public static ulong Combine(ulong seed, string id) => Mix(seed ^ OfString(id));
}
