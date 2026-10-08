using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Millrace.Io;
using Millrace.Realtime;

namespace Millrace.Modbus;

/// <summary>
/// A Modbus TCP server over a plant (plan 8 spec criterion 2): MBAP framing,
/// function codes 1, 2, 3, 4, 5, 6, 15 and 16, any unit id accepted and
/// echoed, any number of clients at once. Reads come from
/// the snapshot delegate — the tag image's published array — and writes
/// go through the command bus, so they land at phase 1 of the next
/// tick like any other external write. Each connection answers its requests
/// in order; a request split across reads, or several in one read, is framed
/// by the MBAP length. A frame whose protocol id is not 0 or whose length is
/// outside 2–254 closes its connection.
/// </summary>
public sealed class ModbusServer : IAsyncDisposable
{
    private const int HeaderLength = 7;
    private const int MaxLength = 254;

    private readonly ModbusProtocol _protocol;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private readonly List<Task> _connections = [];
    private TcpListener? _listener;
    private Task? _accepting;
    private int _open;
    private long _requests;
    private int _disposed;

    /// <summary>Creates a server; <see cref="Start"/> opens it.</summary>
    /// <param name="map">The register map.</param>
    /// <param name="snapshot">Returns the current published image, indexed by tag; called once per read request.</param>
    /// <param name="commands">Where writes go.</param>
    public ModbusServer(RegisterMap map, Func<ReadOnlyMemory<TagValue>> snapshot, CommandBus commands)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(commands);
        Map = map;
        _protocol = new ModbusProtocol(map, snapshot, commands);
    }

    /// <summary>The register map served.</summary>
    public RegisterMap Map { get; }

    /// <summary>The bound endpoint once started — with the port the system chose when started on port 0.</summary>
    public IPEndPoint? LocalEndPoint { get; private set; }

    /// <summary>Connections currently open.</summary>
    public int OpenConnections => Volatile.Read(ref _open);

    /// <summary>Requests answered so far, exceptions included: counted before the response is sent, so a client that has its answer sees it counted.</summary>
    public long Requests => Interlocked.Read(ref _requests);

    /// <summary>
    /// Binds and starts accepting. Throws <see cref="SocketException"/> when
    /// the endpoint cannot be bound (a port in use), and
    /// <see cref="InvalidOperationException"/> when already started.
    /// </summary>
    public IPEndPoint Start(IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_listener is not null)
        {
            throw new InvalidOperationException("The server has already been started.");
        }

        var listener = new TcpListener(endpoint);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            listener.Dispose();
            throw;
        }

        _listener = listener;
        LocalEndPoint = (IPEndPoint)listener.LocalEndpoint;
        _accepting = AcceptAsync(listener, _stop.Token);
        return LocalEndPoint;
    }

    /// <summary>Stops accepting, closes every connection and waits for them to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        if (_accepting is not null)
        {
            await _accepting.ConfigureAwait(false);
        }

        Task[] connections;
        lock (_gate)
        {
            connections = [.. _connections];
        }

        await Task.WhenAll(connections).ConfigureAwait(false);
        _stop.Dispose();
    }

    private async Task AcceptAsync(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_gate)
            {
                _connections.RemoveAll(t => t.IsCompleted);
                _connections.Add(ServeAsync(client, stop));
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        await Task.Yield();
        Interlocked.Increment(ref _open);
        try
        {
            using (client)
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                byte[] header = new byte[HeaderLength];
                byte[] pdu = new byte[MaxLength - 1];
                while (true)
                {
                    await stream.ReadExactlyAsync(header, stop).ConfigureAwait(false);
                    ushort protocol = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                    ushort length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                    if (protocol != 0 || length is < 2 or > MaxLength)
                    {
                        return;
                    }

                    await stream.ReadExactlyAsync(pdu.AsMemory(0, length - 1), stop).ConfigureAwait(false);
                    byte[] response = _protocol.Handle(pdu.AsSpan(0, length - 1));

                    byte[] frame = new byte[HeaderLength + response.Length];
                    header.AsSpan(0, 2).CopyTo(frame);
                    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), (ushort)(response.Length + 1));
                    frame[6] = header[6];
                    response.CopyTo(frame, HeaderLength);
                    Interlocked.Increment(ref _requests);
                    await stream.WriteAsync(frame, stop).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away, or the server is stopping: either way this connection is done.
        }
        catch (Exception)
        {
            // Anything else a request provokes ends only its own connection.
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }
}
