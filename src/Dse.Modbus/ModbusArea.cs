namespace Dse.Modbus;

/// <summary>The four Modbus data areas, in the order a SCADA numbers them (0xxxxx, 1xxxxx, 3xxxxx, 4xxxxx).</summary>
public enum ModbusArea
{
    /// <summary>Read-write bits: a read-write Bool tag. Read with FC1, written with FC5 and FC15.</summary>
    Coils,

    /// <summary>Read-only bits: a read-only Bool tag. Read with FC2.</summary>
    DiscreteInputs,

    /// <summary>Read-only 16-bit registers: a read-only Double or Int64 tag, two registers each. Read with FC4.</summary>
    InputRegisters,

    /// <summary>Read-write 16-bit registers: a read-write Double or Int64 tag, two registers each. Read with FC3, written with FC16.</summary>
    HoldingRegisters,
}
