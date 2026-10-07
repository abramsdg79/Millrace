using System.Buffers.Binary;
using Dse.Io;
using Dse.Realtime;

namespace Dse.Modbus;

/// <summary>
/// One request PDU in, one response PDU out (Modbus Application Protocol
/// v1.1b3 §6). Reads take one snapshot of the published image per request, so
/// every value in a response belongs to the same tick. Writes are validated
/// whole — every value of a multi-value request — before any is handed to the
/// command bus, so a refused request writes nothing. Checks run in the
/// specification's order: function (01), quantity and framing (03), address
/// (02), value (03). Thread-safe: every connection shares one instance.
/// </summary>
internal sealed class ModbusProtocol
{
    private const int MaxReadBits = 2000;
    private const int MaxReadRegisters = 125;
    private const int MaxWriteBits = 1968;
    private const int MaxWriteRegisters = 123;

    private readonly RegisterMap _map;
    private readonly Func<ReadOnlyMemory<TagValue>> _snapshot;
    private readonly CommandBus _commands;
    private readonly Lock _writeGate = new();

    public ModbusProtocol(RegisterMap map, Func<ReadOnlyMemory<TagValue>> snapshot, CommandBus commands)
    {
        _map = map;
        _snapshot = snapshot;
        _commands = commands;
    }

    /// <summary>Answers one request PDU (function code first). Never throws on a malformed request.</summary>
    public byte[] Handle(ReadOnlySpan<byte> pdu)
    {
        byte function = pdu[0];
        return function switch
        {
            0x01 => ReadBits(pdu, ModbusArea.Coils),
            0x02 => ReadBits(pdu, ModbusArea.DiscreteInputs),
            0x03 => ReadRegisters(pdu, ModbusArea.HoldingRegisters),
            0x04 => ReadRegisters(pdu, ModbusArea.InputRegisters),
            0x05 => WriteSingleCoil(pdu),
            0x06 => WriteSingleRegister(pdu),
            0x0F => WriteMultipleCoils(pdu),
            0x10 => WriteMultipleRegisters(pdu),
            _ => Exception(function, ModbusExceptionCode.IllegalFunction),
        };
    }

    private static byte[] Exception(byte function, ModbusExceptionCode code) => [(byte)(function | 0x80), (byte)code];

    private static ushort Word(ReadOnlySpan<byte> pdu, int at) => BinaryPrimitives.ReadUInt16BigEndian(pdu[at..]);

    private bool InArea(ModbusArea area, int start, int quantity) => start + quantity <= _map.Size(area);

    private byte[] ReadBits(ReadOnlySpan<byte> pdu, ModbusArea area)
    {
        byte function = pdu[0];
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        if (quantity is < 1 or > MaxReadBits)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(area, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        ReadOnlySpan<TagValue> image = _snapshot().Span;
        int bytes = (quantity + 7) / 8;
        byte[] response = new byte[2 + bytes];
        response[0] = function;
        response[1] = (byte)bytes;
        for (int i = 0; i < quantity; i++)
        {
            TagValue value = image[_map.At(area, start + i)!.Tag.Index];
            if (value.Kind == TagKind.Bool && value.AsBool)
            {
                response[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private byte[] ReadRegisters(ReadOnlySpan<byte> pdu, ModbusArea area)
    {
        byte function = pdu[0];
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        if (quantity is < 1 or > MaxReadRegisters)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(area, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        ReadOnlySpan<TagValue> image = _snapshot().Span;
        byte[] response = new byte[2 + (2 * quantity)];
        response[0] = function;
        response[1] = (byte)(2 * quantity);
        for (int i = 0; i < quantity; i++)
        {
            RegisterEntry entry = _map.At(area, start + i)!;
            (ushort high, ushort low) = RegisterCodec.Encode(entry.Tag.Kind, image[entry.Tag.Index]);
            ushort word = start + i == entry.Offset ? high : low;
            BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2 + (2 * i)), word);
        }

        return response;
    }

    private byte[] WriteSingleCoil(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x05;
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int address = Word(pdu, 1);
        ushort value = Word(pdu, 3);
        if (value is not (0xFF00 or 0x0000))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(ModbusArea.Coils, address, 1))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        TagDescriptor tag = _map.At(ModbusArea.Coils, address)!.Tag;
        lock (_writeGate)
        {
            if (_commands.WriteBool(tag.Name, value == 0xFF00) != CommandOutcome.Accepted)
            {
                return Exception(function, ModbusExceptionCode.IllegalDataValue);
            }
        }

        return pdu.ToArray();
    }

    /// <summary>
    /// Every holding register belongs to a two-register value, so FC6 — one
    /// register — can only ever write half of one: within the area it answers
    /// 02, as any partial write does (R197).
    /// </summary>
    private byte[] WriteSingleRegister(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x06;
        if (pdu.Length != 5)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        return Exception(function, ModbusExceptionCode.IllegalDataAddress);
    }

    private byte[] WriteMultipleCoils(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x0F;
        if (pdu.Length < 6)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        int bytes = pdu[5];
        if (quantity is < 1 or > MaxWriteBits || bytes != (quantity + 7) / 8 || pdu.Length != 6 + bytes)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(ModbusArea.Coils, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        bool refused = false;
        lock (_writeGate)
        {
            for (int i = 0; i < quantity; i++)
            {
                bool on = (pdu[6 + (i / 8)] & (1 << (i % 8))) != 0;
                refused |= _commands.WriteBool(_map.At(ModbusArea.Coils, start + i)!.Tag.Name, on) != CommandOutcome.Accepted;
            }
        }

        return refused ? Exception(function, ModbusExceptionCode.IllegalDataValue) : pdu[..5].ToArray();
    }

    private byte[] WriteMultipleRegisters(ReadOnlySpan<byte> pdu)
    {
        const byte function = 0x10;
        if (pdu.Length < 6)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        int start = Word(pdu, 1);
        int quantity = Word(pdu, 3);
        int bytes = pdu[5];
        if (quantity is < 1 or > MaxWriteRegisters || bytes != 2 * quantity || pdu.Length != 6 + bytes)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataValue);
        }

        if (!InArea(ModbusArea.HoldingRegisters, start, quantity))
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        RegisterEntry first = _map.At(ModbusArea.HoldingRegisters, start)!;
        RegisterEntry last = _map.At(ModbusArea.HoldingRegisters, start + quantity - 1)!;
        if (first.Offset != start || last.Offset + last.Width != start + quantity)
        {
            return Exception(function, ModbusExceptionCode.IllegalDataAddress);
        }

        var writes = new (TagDescriptor Tag, TagValue Value)[quantity / 2];
        for (int i = 0; i < writes.Length; i++)
        {
            TagDescriptor tag = _map.At(ModbusArea.HoldingRegisters, start + (2 * i))!.Tag;
            ushort high = Word(pdu, 6 + (4 * i));
            ushort low = Word(pdu, 8 + (4 * i));
            if (tag.Kind == TagKind.Double)
            {
                double value = RegisterCodec.ToFloat32(high, low);
                if (!double.IsFinite(value) || (tag.HasRange && (value < tag.RangeLow || value > tag.RangeHigh)))
                {
                    return Exception(function, ModbusExceptionCode.IllegalDataValue);
                }

                writes[i] = (tag, TagValue.Double(value));
            }
            else
            {
                writes[i] = (tag, TagValue.Int64(RegisterCodec.ToInt32(high, low)));
            }
        }

        bool refused = false;
        lock (_writeGate)
        {
            foreach ((TagDescriptor tag, TagValue value) in writes)
            {
                refused |= _commands.Write(tag.Name, value) != CommandOutcome.Accepted;
            }
        }

        // Pre-validation above makes a refusal here unreachable for this directory's
        // tags; should the bus refuse one anyway, the master is told rather than
        // shown an echo for a write that did not happen.
        return refused ? Exception(function, ModbusExceptionCode.IllegalDataValue) : pdu[..5].ToArray();
    }
}
