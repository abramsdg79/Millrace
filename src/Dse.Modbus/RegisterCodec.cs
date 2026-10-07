using Dse.Io;

namespace Dse.Modbus;

/// <summary>
/// The two-register encodings (spec criterion 1): a Double as an IEEE 754
/// single, an Int64 as a two's-complement Int32 that saturates at
/// <see cref="int.MinValue"/> and <see cref="int.MaxValue"/>. Both are
/// big-endian: the high word is the first register, the high byte of each
/// register is sent first (FUXA's <c>Float32</c> and <c>Int32</c>, a
/// "ABCD" order).
/// </summary>
public static class RegisterCodec
{
    /// <summary>A double narrowed to a single; a value beyond the single's range becomes an infinity, NaN stays NaN.</summary>
    public static (ushort High, ushort Low) Float32(double value)
    {
        uint bits = BitConverter.SingleToUInt32Bits((float)value);
        return ((ushort)(bits >> 16), (ushort)bits);
    }

    /// <summary>A long clamped to the Int32 range.</summary>
    public static (ushort High, ushort Low) Int32(long value)
    {
        uint bits = unchecked((uint)(int)Math.Clamp(value, int.MinValue, int.MaxValue));
        return ((ushort)(bits >> 16), (ushort)bits);
    }

    /// <summary>The single two registers carry, widened to a double.</summary>
    public static double ToFloat32(ushort high, ushort low) =>
        BitConverter.UInt32BitsToSingle(((uint)high << 16) | low);

    /// <summary>The Int32 two registers carry, widened to a long.</summary>
    public static long ToInt32(ushort high, ushort low) =>
        unchecked((int)(((uint)high << 16) | low));

    /// <summary>
    /// The two registers of a value of <paramref name="kind"/>. A value of
    /// another kind — an image not yet primed holds <c>default</c>, a Bool —
    /// encodes as zero rather than throwing.
    /// </summary>
    public static (ushort High, ushort Low) Encode(TagKind kind, TagValue value)
    {
        if (value.Kind != kind)
        {
            return (0, 0);
        }

        return kind switch
        {
            TagKind.Double => Float32(value.AsDouble),
            TagKind.Int64 => Int32(value.AsInt64),
            _ => (0, 0),
        };
    }
}
