using Dse.Io;

namespace Dse.Modbus;

/// <summary>
/// Where one tag lives on the wire: its area, its 0-based offset (the address
/// a request carries) and how many bits or registers it takes.
/// </summary>
/// <param name="Area">The data area.</param>
/// <param name="Offset">The 0-based wire offset of the tag's first bit or register.</param>
/// <param name="Tag">The tag.</param>
public sealed record RegisterEntry(ModbusArea Area, int Offset, TagDescriptor Tag)
{
    /// <summary>One bit, or two registers.</summary>
    public int Width => IsBit ? 1 : 2;

    /// <summary>The 1-based address a SCADA shows: <see cref="Offset"/> + 1.</summary>
    public int Number => Offset + 1;

    /// <summary>True for coils and discrete inputs.</summary>
    public bool IsBit => Area is ModbusArea.Coils or ModbusArea.DiscreteInputs;

    /// <summary>True for coils and holding registers, the areas a master may write.</summary>
    public bool IsWritable => Area is ModbusArea.Coils or ModbusArea.HoldingRegisters;

    /// <summary>How the value is encoded: <c>Bool</c>, <c>Float32</c> (big-endian) or <c>Int32</c> (big-endian, saturating).</summary>
    public string DataType => Tag.Kind switch
    {
        TagKind.Bool => "Bool",
        TagKind.Double => "Float32",
        _ => "Int32",
    };
}
