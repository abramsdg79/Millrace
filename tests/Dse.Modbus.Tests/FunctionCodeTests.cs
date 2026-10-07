using Dse.Io;
using Dse.Modbus.Tests.Fakes;
using Dse.Tests.Shared;

namespace Dse.Modbus.Tests;

/// <summary>Each function code against <see cref="Plant"/>, over a real loopback connection.</summary>
public class FunctionCodeTests
{
    [Fact]
    public async Task ReadCoilsReturnsTheReadWriteBoolsPackedLowBitFirst()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new[] { true, true }, client.ReadCoils(0, 2));
        Assert.Equal(new byte[] { 0x01, 0x01, 0b11 }, client.Request(ModbusClient.Pdu(0x01, 0, 2)));
    }

    [Fact]
    public async Task ReadDiscreteInputsReturnsTheReadOnlyBoolsClaimedOnesIncluded()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new[] { true, false }, client.ReadDiscreteInputs(0, 2));
    }

    [Fact]
    public async Task ReadHoldingRegistersReturnsTheReadWriteNumbersTwoRegistersEach()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        ushort[] words = client.ReadHoldingRegisters(0, 6);

        Assert.Equal(-7, ModbusClient.Int32(words, 0));
        Assert.Equal(1.5f, ModbusClient.Float(words, 2));
        Assert.Equal(-0.25f, ModbusClient.Float(words, 4));
    }

    [Fact]
    public async Task ReadInputRegistersReturnsTheReadOnlyNumbersAndMayStartMidValue()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        ushort[] words = client.ReadInputRegisters(0, 4);
        ushort[] tail = client.ReadInputRegisters(3, 1);

        Assert.Equal(123_456, ModbusClient.Int32(words, 0));
        Assert.Equal(1.95f, ModbusClient.Float(words, 2));
        Assert.Equal(new ushort[] { 0x999A }, tail);
    }

    [Fact]
    public async Task EveryReadSeesTheImagePublishedWhenItArrives()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        Assert.Equal(1.95f, ModbusClient.Float(client.ReadInputRegisters(2, 2), 0));

        TagValue[] next = Plant.Image();
        next[6] = TagValue.Double(0.5);
        next[4] = TagValue.Bool(true);
        rig.Image = next;

        Assert.Equal(0.5f, ModbusClient.Float(client.ReadInputRegisters(2, 2), 0));
        Assert.Equal(new[] { true, true }, client.ReadDiscreteInputs(0, 2));
    }

    [Fact]
    public async Task AnImageNotYetPrimedReadsAsZeroesRatherThanFailing()
    {
        await using Rig rig = Rig.Start();
        rig.Image = new TagValue[9];
        using ModbusClient client = rig.Connect();

        Assert.Equal(new ushort[6], client.ReadHoldingRegisters(0, 6));
        Assert.Equal(new[] { false, false }, client.ReadCoils(0, 2));
    }

    [Fact]
    public async Task WriteSingleCoilQueuesTheBoolAndEchoesTheRequest()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        byte[] response = client.Request(ModbusClient.Pdu(0x05, 1, 0xFF00));
        client.WriteCoil(0, false);

        Assert.Equal(ModbusClient.Pdu(0x05, 1, 0xFF00), response);
        Assert.Equal(new[] { ("B.Stop", TagValue.Bool(true)), ("A.Run", TagValue.Bool(false)) }, rig.Writer.Writes);
    }

    [Fact]
    public async Task WriteMultipleCoilsQueuesEachBoolInAddressOrder()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        byte[] response = client.Request([0x0F, 0x00, 0x00, 0x00, 0x02, 0x01, 0b10]);

        Assert.Equal(new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x02 }, response);
        Assert.Equal(new[] { ("A.Run", TagValue.Bool(false)), ("B.Stop", TagValue.Bool(true)) }, rig.Writer.Writes);
    }

    [Fact]
    public async Task WriteMultipleRegistersQueuesWholeValuesDecodedByKind()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        client.WriteRegisters(0, 0xFFFF, 0xFFF9, 0x3FC0, 0x0000);
        client.WriteFloat(4, 1e30f);

        Assert.Equal(
            new[] { ("A.Batch", TagValue.Int64(-7L)), ("A.Setpoint", TagValue.Double(1.5)), ("B.Bias", TagValue.Double(1e30f)) },
            rig.Writer.Writes);
    }

    [Theory]
    [InlineData(0x07)]
    [InlineData(0x08)]
    [InlineData(0x17)]
    [InlineData(0x2B)]
    [InlineData(0x00)]
    public async Task AnUnsupportedFunctionIsException01(byte function)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x01 }, client.Request(ModbusClient.Pdu(function, 0, 1)));
    }

    [Theory]
    [InlineData(0x01, 2, 1)]
    [InlineData(0x01, 0, 3)]
    [InlineData(0x02, 1, 2)]
    [InlineData(0x03, 6, 1)]
    [InlineData(0x03, 0, 7)]
    [InlineData(0x04, 3, 2)]
    [InlineData(0x04, 65535, 1)]
    public async Task AReadPastTheEndOfItsAreaIsException02(byte function, int start, int count)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x02 }, client.Request(ModbusClient.Pdu(function, (ushort)start, (ushort)count)));
    }

    [Theory]
    [InlineData(0x01, 0)]
    [InlineData(0x01, 2001)]
    [InlineData(0x02, 0)]
    [InlineData(0x03, 0)]
    [InlineData(0x03, 126)]
    [InlineData(0x04, 126)]
    public async Task AReadOfAnIllegalQuantityIsException03(byte function, int count)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x03 }, client.Request(ModbusClient.Pdu(function, 0, (ushort)count)));
    }

    [Theory]
    [InlineData(0, 0x0001)]
    [InlineData(0, 0x00FF)]
    [InlineData(5, 0x1234)]
    public async Task ACoilValueOtherThanFF00Or0000IsException03(int address, int value)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { 0x85, 0x03 }, client.Request(ModbusClient.Pdu(0x05, (ushort)address, (ushort)value)));
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(0x05, 2)]
    [InlineData(0x06, 0)]
    [InlineData(0x06, 3)]
    [InlineData(0x06, 6)]
    public async Task AWriteToAnAddressNoReadWriteTagOccupiesOrToHalfAValueIsException02(byte function, int address)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        ushort value = function == 0x05 ? (ushort)0xFF00 : (ushort)0x0001;
        Assert.Equal(new byte[] { (byte)(function | 0x80), 0x02 }, client.Request(ModbusClient.Pdu(function, (ushort)address, value)));
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(1, new ushort[] { 0x3FC0, 0x0000 })]
    [InlineData(0, new ushort[] { 0x0000 })]
    [InlineData(0, new ushort[] { 0x0000, 0x0001, 0x3FC0 })]
    [InlineData(4, new ushort[] { 0x0000, 0x0000, 0x0000, 0x0000 })]
    public async Task AWriteOfPartOfATwoRegisterValueIsException02AndWritesNothing(int start, ushort[] words)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { 0x90, 0x02 }, client.Request(ModbusClient.WriteRegistersPdu(start, words)));
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(2.5f)]
    [InlineData(-0.5f)]
    public async Task ANonFiniteOrOutOfRangeValueIsException03AndNothingOfTheRequestIsWritten(float setpoint)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        uint bits = BitConverter.SingleToUInt32Bits(setpoint);

        byte[] response = client.Request(ModbusClient.WriteRegistersPdu(0, 0x0000, 0x0003, (ushort)(bits >> 16), (ushort)bits));

        Assert.Equal(new byte[] { 0x90, 0x03 }, response);
        Assert.Empty(rig.Writer.Writes);
    }

    [Theory]
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x02, 0x03, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x02, 0x04, 0, 0, 0 })]
    [InlineData(new byte[] { 0x10, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x02, 0x02, 0x03, 0x00 })]
    [InlineData(new byte[] { 0x0F, 0x00, 0x00, 0x00, 0x09, 0x01, 0xFF })]
    [InlineData(new byte[] { 0x03, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x05, 0x00, 0x00, 0xFF, 0x00, 0x00 })]
    public async Task AMalformedRequestIsException03(byte[] pdu)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        Assert.Equal(new byte[] { (byte)(pdu[0] | 0x80), 0x03 }, client.Request(pdu));
        Assert.Empty(rig.Writer.Writes);
    }
}
