using System.Net;
using Dse.Io;
using Dse.Realtime;
using Dse.Tests.Shared;

namespace Dse.Modbus.Tests.Fakes;

/// <summary>
/// A server over <see cref="Plant"/> on a free loopback port: the image it
/// reads is <see cref="Image"/>, replaced whole as the simulation's front
/// buffer is, and every accepted write lands in <see cref="Writer"/>.
/// </summary>
internal sealed class Rig : IAsyncDisposable
{
    private TagValue[] _image = Plant.Image();

    private Rig()
    {
        Directory = Plant.Directory();
        Map = RegisterMap.Build(Directory);
        Writer = new RecordingWriter(Directory);
        Commands = new CommandBus(Writer);
        Server = new ModbusServer(Map, () => Volatile.Read(ref _image), Commands);
        EndPoint = Server.Start(new IPEndPoint(IPAddress.Loopback, 0));
    }

    public ArrayDirectory Directory { get; }

    public RegisterMap Map { get; }

    public RecordingWriter Writer { get; }

    public CommandBus Commands { get; }

    public ModbusServer Server { get; }

    public IPEndPoint EndPoint { get; }

    /// <summary>The image the server reads; setting it publishes a new one.</summary>
    public TagValue[] Image
    {
        get => Volatile.Read(ref _image);
        set => Volatile.Write(ref _image, value);
    }

    public static Rig Start() => new();

    public ModbusClient Connect() => new(EndPoint);

    public ValueTask DisposeAsync() => Server.DisposeAsync();
}
