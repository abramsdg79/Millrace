namespace Millrace.Modbus;

/// <summary>The Modbus exception codes this server answers with (spec criterion 2).</summary>
public enum ModbusExceptionCode : byte
{
    /// <summary>01: the function code is not one of 1, 2, 3, 4, 5, 6, 15, 16.</summary>
    IllegalFunction = 0x01,

    /// <summary>02: an address past the end of the area, or a write that covers part of a two-register value.</summary>
    IllegalDataAddress = 0x02,

    /// <summary>03: a malformed request (quantity, byte count, coil value), a non-finite value, or a value the tag refuses.</summary>
    IllegalDataValue = 0x03,
}
