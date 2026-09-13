using System.Runtime.CompilerServices;

namespace Dse.Core.Io;

/// <summary>Reads an enum's underlying integer without boxing, whatever its underlying type.</summary>
internal static class EnumBits<TEnum>
    where TEnum : unmanaged, Enum
{
    private static readonly TypeCode Code = Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum)));

    public static long ToInt64(TEnum value) => Code switch
    {
        TypeCode.SByte => Unsafe.As<TEnum, sbyte>(ref value),
        TypeCode.Byte => Unsafe.As<TEnum, byte>(ref value),
        TypeCode.Int16 => Unsafe.As<TEnum, short>(ref value),
        TypeCode.UInt16 => Unsafe.As<TEnum, ushort>(ref value),
        TypeCode.Int32 => Unsafe.As<TEnum, int>(ref value),
        TypeCode.UInt32 => Unsafe.As<TEnum, uint>(ref value),
        TypeCode.Int64 => Unsafe.As<TEnum, long>(ref value),
        _ => (long)Unsafe.As<TEnum, ulong>(ref value),
    };
}
