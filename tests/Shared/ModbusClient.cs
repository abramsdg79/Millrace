using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Millrace.Tests.Shared;

/// <summary>
/// A minimal, blocking Modbus TCP master for tests: it frames a PDU in an
/// MBAP header, sends it, and reads one response. Nothing here knows the
/// server's code; it speaks the wire protocol only. Every read times out
/// after five seconds, so a server that never answers fails a test instead
/// of hanging it.
/// </summary>
internal sealed class ModbusClient : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private ushort _transaction;

    public ModbusClient(IPEndPoint endpoint)
    {
        _tcp = new TcpClient { NoDelay = true, ReceiveTimeout = 5000, SendTimeout = 5000 };
        _tcp.Connect(endpoint);
        _stream = _tcp.GetStream();
    }

    /// <summary>The unit id sent with every request.</summary>
    public byte Unit { get; set; } = 1;

    /// <summary>An MBAP header (transaction, protocol 0, length, unit) followed by the PDU.</summary>
    public static byte[] Frame(ushort transaction, byte unit, params byte[] pdu)
    {
        byte[] frame = new byte[7 + pdu.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, transaction);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(pdu.Length + 1));
        frame[6] = unit;
        pdu.CopyTo(frame, 7);
        return frame;
    }

    /// <summary>A PDU: the function code, then each value as a big-endian word.</summary>
    public static byte[] Pdu(byte function, params ushort[] words)
    {
        byte[] pdu = new byte[1 + (2 * words.Length)];
        pdu[0] = function;
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1 + (2 * i)), words[i]);
        }

        return pdu;
    }

    /// <summary>Writes raw bytes, framed or not.</summary>
    public void Send(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);

    /// <summary>Reads one response frame.</summary>
    public (ushort Transaction, byte Unit, byte[] Pdu) Receive()
    {
        byte[] header = new byte[7];
        _stream.ReadExactly(header);
        ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
        byte[] pdu = new byte[length - 1];
        _stream.ReadExactly(pdu);
        return (BinaryPrimitives.ReadUInt16BigEndian(header), header[6], pdu);
    }

    /// <summary>True when the server has closed the connection: a read returns end of stream.</summary>
    public bool IsClosedByServer()
    {
        try
        {
            return _stream.Read(new byte[1]) == 0;
        }
        catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    /// <summary>Sends a PDU under the next transaction id and returns the response PDU, checking the echo.</summary>
    public byte[] Request(byte[] pdu)
    {
        ushort transaction = ++_transaction;
        Send(Frame(transaction, Unit, pdu));
        (ushort echoed, byte unit, byte[] response) = Receive();
        if (echoed != transaction || unit != Unit)
        {
            throw new InvalidDataException($"Sent transaction {transaction} unit {Unit}; the response carries {echoed} unit {unit}.");
        }

        return response;
    }

    public bool[] ReadCoils(int start, int count) => Bits(Expect(Request(Pdu(0x01, (ushort)start, (ushort)count)), 0x01), count);

    public bool[] ReadDiscreteInputs(int start, int count) => Bits(Expect(Request(Pdu(0x02, (ushort)start, (ushort)count)), 0x02), count);

    public ushort[] ReadHoldingRegisters(int start, int count) => Words(Expect(Request(Pdu(0x03, (ushort)start, (ushort)count)), 0x03));

    public ushort[] ReadInputRegisters(int start, int count) => Words(Expect(Request(Pdu(0x04, (ushort)start, (ushort)count)), 0x04));

    /// <summary>FC5: 0xFF00 for on, 0x0000 for off.</summary>
    public void WriteCoil(int address, bool on) => Expect(Request(Pdu(0x05, (ushort)address, on ? (ushort)0xFF00 : (ushort)0x0000)), 0x05);

    /// <summary>FC16 with one big-endian single in two registers.</summary>
    public void WriteFloat(int address, float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        WriteRegisters(address, (ushort)(bits >> 16), (ushort)bits);
    }

    /// <summary>FC16.</summary>
    public void WriteRegisters(int address, params ushort[] words) => Expect(Request(WriteRegistersPdu(address, words)), 0x10);

    /// <summary>The FC16 request PDU: start, quantity, byte count, the words.</summary>
    public static byte[] WriteRegistersPdu(int address, params ushort[] words)
    {
        byte[] pdu = new byte[6 + (2 * words.Length)];
        pdu[0] = 0x10;
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(1), (ushort)address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(3), (ushort)words.Length);
        pdu[5] = (byte)(2 * words.Length);
        for (int i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(6 + (2 * i)), words[i]);
        }

        return pdu;
    }

    /// <summary>The single in registers <paramref name="at"/> and <paramref name="at"/> + 1, high word first.</summary>
    public static float Float(ushort[] words, int at) => BitConverter.UInt32BitsToSingle(((uint)words[at] << 16) | words[at + 1]);

    /// <summary>The Int32 in registers <paramref name="at"/> and <paramref name="at"/> + 1, high word first.</summary>
    public static int Int32(ushort[] words, int at) => unchecked((int)(((uint)words[at] << 16) | words[at + 1]));

    public void Dispose()
    {
        _stream.Dispose();
        _tcp.Dispose();
    }

    private static byte[] Expect(byte[] pdu, byte function) =>
        pdu[0] == function
            ? pdu
            : throw new InvalidDataException($"Function {function:X2} answered {pdu[0]:X2} with exception {(pdu.Length > 1 ? pdu[1] : 0):X2}.");

    private static bool[] Bits(byte[] pdu, int count)
    {
        bool[] bits = new bool[count];
        for (int i = 0; i < count; i++)
        {
            bits[i] = (pdu[2 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        return bits;
    }

    private static ushort[] Words(byte[] pdu)
    {
        ushort[] words = new ushort[pdu[1] / 2];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(2 + (2 * i)));
        }

        return words;
    }
}
