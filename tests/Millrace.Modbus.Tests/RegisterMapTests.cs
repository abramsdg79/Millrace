using Millrace.Io;
using Millrace.Modbus.Tests.Fakes;

namespace Millrace.Modbus.Tests;

public class RegisterMapTests
{
    [Theory]
    [InlineData("A.Run", ModbusArea.Coils, 0, "Bool")]
    [InlineData("B.Stop", ModbusArea.Coils, 1, "Bool")]
    [InlineData("A.Permit", ModbusArea.DiscreteInputs, 0, "Bool")]
    [InlineData("A.Running", ModbusArea.DiscreteInputs, 1, "Bool")]
    [InlineData("A.Count", ModbusArea.InputRegisters, 0, "Int32")]
    [InlineData("A.Speed", ModbusArea.InputRegisters, 2, "Float32")]
    [InlineData("A.Batch", ModbusArea.HoldingRegisters, 0, "Int32")]
    [InlineData("A.Setpoint", ModbusArea.HoldingRegisters, 2, "Float32")]
    [InlineData("B.Bias", ModbusArea.HoldingRegisters, 4, "Float32")]
    public void EveryTagGetsTheAreaItsKindAndAccessGiveInDirectoryOrderFromZero(string tag, ModbusArea area, int offset, string type)
    {
        RegisterEntry entry = RegisterMap.Build(Plant.Directory()).Find(tag);

        Assert.Equal((area, offset, offset + 1, type), (entry.Area, entry.Offset, entry.Number, entry.DataType));
    }

    [Fact]
    public void EachAreaIsPackedWithNoGapsAndTwoRegistersPerValue()
    {
        RegisterMap map = RegisterMap.Build(Plant.Directory());

        Assert.Equal((2, 2, 4, 6), (map.Size(ModbusArea.Coils), map.Size(ModbusArea.DiscreteInputs), map.Size(ModbusArea.InputRegisters), map.Size(ModbusArea.HoldingRegisters)));
        Assert.Same(map.Find("A.Setpoint"), map.At(ModbusArea.HoldingRegisters, 2));
        Assert.Same(map.Find("A.Setpoint"), map.At(ModbusArea.HoldingRegisters, 3));
        Assert.Null(map.At(ModbusArea.HoldingRegisters, 6));
        Assert.Null(map.At(ModbusArea.Coils, -1));
        Assert.Equal(Plant.Directory().Tags.Select(t => t.Name), map.Entries.Select(e => e.Tag.Name));
    }

    [Fact]
    public void TheSameDirectoryAlwaysGivesTheSameMap()
    {
        RegisterMap first = RegisterMap.Build(Plant.Directory());
        RegisterMap second = RegisterMap.Build(Plant.Directory());

        Assert.Equal(
            first.Entries.Select(e => (e.Tag.Name, e.Area, e.Offset)),
            second.Entries.Select(e => (e.Tag.Name, e.Area, e.Offset)));
    }

    [Fact]
    public void AClaimedTagMapsToAReadOnlyArea()
    {
        RegisterEntry permit = RegisterMap.Build(Plant.Directory()).Find("A.Permit");

        Assert.Equal("INT_A", permit.Tag.ClaimedBy);
        Assert.Equal(ModbusArea.DiscreteInputs, permit.Area);
        Assert.False(permit.IsWritable);
    }

    [Fact]
    public void APlantTooLargeForOneAreaIsRefusedByName()
    {
        TagDescriptor[] tags = Enumerable.Range(0, 32_769)
            .Select(i => new TagDescriptor(i, $"T{i:D5}", TagKind.Double, TagAccess.ReadWrite, "", double.NaN, double.NaN, ""))
            .ToArray();

        ArgumentException refused = Assert.Throws<ArgumentException>(() => RegisterMap.Build(new ArrayDirectory(tags)));
        Assert.StartsWith("The plant's holding registers need more than 65536 addresses", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0, 0x3F80, 0x0000)]
    [InlineData(-2.5, 0xC020, 0x0000)]
    [InlineData(1.95, 0x3FF9, 0x999A)]
    [InlineData(0.0, 0x0000, 0x0000)]
    [InlineData(1e39, 0x7F80, 0x0000)]
    [InlineData(-1e39, 0xFF80, 0x0000)]
    public void ADoubleIsABigEndianSingleHighWordFirst(double value, int high, int low)
    {
        Assert.Equal(((ushort)high, (ushort)low), RegisterCodec.Float32(value));
        Assert.Equal((float)value, (float)RegisterCodec.ToFloat32((ushort)high, (ushort)low));
    }

    [Theory]
    [InlineData(5L, 0x0000, 0x0005)]
    [InlineData(-1L, 0xFFFF, 0xFFFF)]
    [InlineData(70_000L, 0x0001, 0x1170)]
    [InlineData(2_147_483_647L, 0x7FFF, 0xFFFF)]
    [InlineData(2_147_483_648L, 0x7FFF, 0xFFFF)]
    [InlineData(long.MaxValue, 0x7FFF, 0xFFFF)]
    [InlineData(-2_147_483_648L, 0x8000, 0x0000)]
    [InlineData(-3_000_000_000L, 0x8000, 0x0000)]
    [InlineData(long.MinValue, 0x8000, 0x0000)]
    public void AnInt64IsABigEndianInt32ThatSaturates(long value, int high, int low)
    {
        Assert.Equal(((ushort)high, (ushort)low), RegisterCodec.Int32(value));
    }

    [Fact]
    public void AValueOfAnotherKindEncodesAsZeroRatherThanThrowing()
    {
        Assert.Equal(((ushort)0, (ushort)0), RegisterCodec.Encode(TagKind.Double, default));
        Assert.Equal(((ushort)0, (ushort)0), RegisterCodec.Encode(TagKind.Int64, TagValue.Double(3.0)));
        Assert.Equal(((ushort)0x4040, (ushort)0), RegisterCodec.Encode(TagKind.Double, TagValue.Double(3.0)));
    }
}
