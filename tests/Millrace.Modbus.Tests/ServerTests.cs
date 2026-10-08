using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Millrace.Io;
using Millrace.Modbus.Tests.Fakes;
using Millrace.Realtime;
using Millrace.Tests.Shared;

namespace Millrace.Modbus.Tests;

/// <summary>MBAP framing, unit ids, several clients at once, and shutdown.</summary>
public class ServerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static void WaitUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Patience, $"Timed out waiting for {what}.");
            Thread.Sleep(5);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(247)]
    [InlineData(255)]
    public async Task AnyUnitIdIsAcceptedAndEchoed(byte unit)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        client.Unit = unit;

        Assert.Equal(new[] { true, true }, client.ReadCoils(0, 2));
    }

    [Fact]
    public async Task ARequestSplitAcrossSeveralReadsIsAnsweredOnceWhole()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        byte[] frame = ModbusClient.Frame(0x1234, 9, ModbusClient.Pdu(0x04, 2, 2));

        client.Send(frame.AsSpan(0, 3));
        Thread.Sleep(50);
        client.Send(frame.AsSpan(3, 5));
        Thread.Sleep(50);
        client.Send(frame.AsSpan(8));
        (ushort transaction, byte unit, byte[] pdu) = client.Receive();

        Assert.Equal((0x1234, 9), (transaction, unit));
        Assert.Equal(new byte[] { 0x04, 0x04, 0x3F, 0xF9, 0x99, 0x9A }, pdu);
    }

    [Fact]
    public async Task TwoRequestsInOneReadAreAnsweredInOrder()
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();
        byte[] first = ModbusClient.Frame(7, 1, ModbusClient.Pdu(0x01, 0, 2));
        byte[] second = ModbusClient.Frame(8, 1, ModbusClient.Pdu(0x05, 1, 0x0000));

        client.Send([.. first, .. second]);

        (ushort t1, _, byte[] r1) = client.Receive();
        (ushort t2, _, byte[] r2) = client.Receive();

        Assert.Equal((7, 8), (t1, t2));
        Assert.Equal(new byte[] { 0x01, 0x01, 0b11 }, r1);
        Assert.Equal(ModbusClient.Pdu(0x05, 1, 0x0000), r2);
        Assert.Equal(new[] { ("B.Stop", TagValue.Bool(false)) }, rig.Writer.Writes);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x01, 0x00, 0x06, 0x01, 0x01, 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01 })]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x01 })]
    public async Task AFrameWithAForeignProtocolIdOrAnImpossibleLengthClosesTheConnection(byte[] frame)
    {
        await using Rig rig = Rig.Start();
        using ModbusClient client = rig.Connect();

        client.Send(frame);

        Assert.True(client.IsClosedByServer());
        using ModbusClient next = rig.Connect();
        Assert.Equal(new[] { true, true }, next.ReadCoils(0, 2));
    }

    [Fact]
    public async Task AFrameWithTheLargestAllowedLengthIsAnsweredNotClosed()
    {
        await using Rig rig = Rig.Start();

        // Length 254: the unit id plus a 253-byte PDU, here a read of one coil padded with zeros.
        byte[] pdu = new byte[253];
        ModbusClient.Pdu(0x01, 0, 1).CopyTo(pdu, 0);

        byte[] response = await Task.Factory.StartNew(() =>
        {
            using ModbusClient client = rig.Connect();
            client.Send(ModbusClient.Frame(9, 1, pdu));
            (ushort transaction, _, byte[] answer) = client.Receive();
            Assert.Equal(9, transaction);
            return answer;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(Patience);

        Assert.Equal(0x01, response[0] & 0x7F);
    }

    [Fact]
    public async Task SeveralClientsAtOnceEachGetTheirOwnAnswers()
    {
        await using Rig rig = Rig.Start();
        const int clients = 8;
        const int rounds = 100;

        Task[] work = Enumerable.Range(0, clients).Select(c => Task.Factory.StartNew(() =>
        {
            using ModbusClient client = rig.Connect();
            client.Unit = (byte)(c + 1);
            for (int r = 0; r < rounds; r++)
            {
                Assert.Equal(1.95f, ModbusClient.Float(client.ReadInputRegisters(2, 2), 0));
                client.WriteCoil(c % 2, r % 2 == 0);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

        await Task.WhenAll(work).WaitAsync(Patience);
        Assert.Equal(clients * rounds, rig.Writer.Writes.Length);
        Assert.Equal(clients * rounds, rig.Commands.Accepted);
        Assert.Equal(2L * clients * rounds, rig.Server.Requests);
    }

    [Fact]
    public async Task OneRequestsValuesComeFromOneImageWhileTheImageIsReplaced()
    {
        await using Rig rig = Rig.Start();
        rig.Image = Consistent(0);                            // every image the client can see is consistent, the first too
        using var stop = new CancellationTokenSource();
        Task publisher = Task.Factory.StartNew(() =>
        {
            for (long n = 1; !stop.IsCancellationRequested; n++)
            {
                rig.Image = Consistent(n);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        using ModbusClient client = rig.Connect();
        for (int i = 0; i < 500; i++)
        {
            ushort[] words = client.ReadHoldingRegisters(0, 6);
            int batch = ModbusClient.Int32(words, 0);
            Assert.Equal(batch % 2, (int)ModbusClient.Float(words, 2));
            Assert.Equal(batch % 2, (int)ModbusClient.Float(words, 4));
        }

        await stop.CancelAsync();
        await publisher;

        static TagValue[] Consistent(long n)
        {
            TagValue[] image = Plant.Image();
            image[0] = TagValue.Int64(n);                     // A.Batch
            image[5] = TagValue.Double(n % 2);                // A.Setpoint
            image[7] = TagValue.Double(n % 2);                // B.Bias
            return image;
        }
    }

    [Fact]
    public async Task DisposingTheServerClosesEveryConnectionAndStopsListening()
    {
        Rig rig = Rig.Start();
        using ModbusClient first = rig.Connect();
        using ModbusClient second = rig.Connect();
        Assert.Equal(new[] { true, true }, first.ReadCoils(0, 2));
        Assert.Equal(new[] { true, true }, second.ReadCoils(0, 2));
        WaitUntil(() => rig.Server.OpenConnections == 2, "two open connections");

        await rig.DisposeAsync().AsTask().WaitAsync(Patience);

        Assert.Equal(0, rig.Server.OpenConnections);
        Assert.True(first.IsClosedByServer());
        Assert.True(second.IsClosedByServer());
        using var probe = new TcpClient();
        Assert.Throws<SocketException>(() => probe.Connect(rig.EndPoint));
    }

    [Fact]
    public async Task APortInUseFailsToStartWithASocketException()
    {
        await using Rig rig = Rig.Start();
        var map = RegisterMap.Build(Plant.Directory());
        await using var second = new ModbusServer(map, () => Plant.Image(), new CommandBus(new RecordingWriter(Plant.Directory())));

        Assert.Throws<SocketException>(() => second.Start(rig.EndPoint));
        Assert.Throws<SocketException>(() => second.Start(new IPEndPoint(IPAddress.Loopback, rig.EndPoint.Port)));
    }
}
