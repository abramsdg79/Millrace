namespace Dse.Realtime;

/// <summary>
/// A background thread that pumps a <see cref="RealtimeHub"/> whenever frames
/// arrive (spec 10.1). Dispose stops it, pumps once more so nothing published
/// before the stop is stranded, and joins. Dispose the dispatcher before the hub.
/// </summary>
public sealed class DispatcherThread : IDisposable
{
    private readonly RealtimeHub _hub;
    private readonly Thread _thread;
    private readonly TimeSpan _idleWait;
    private volatile bool _stop;
    private volatile bool _running;
    private bool _started;
    private long _pumped;
    private Exception? _failure;

    /// <summary>Creates a dispatcher for <paramref name="hub"/>; call <see cref="Start"/> to run it.</summary>
    public DispatcherThread(RealtimeHub hub, string name = "dse-dispatcher", TimeSpan? idleWait = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _hub = hub;
        _idleWait = idleWait ?? TimeSpan.FromMilliseconds(100);
        if (_idleWait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleWait), _idleWait, "The idle wait must be positive.");
        }

        _thread = new Thread(Loop) { Name = name, IsBackground = true };
    }

    /// <summary>True while the loop is running.</summary>
    public bool IsRunning => _running;

    /// <summary>Frames pumped by this thread.</summary>
    public long PumpedFrames => Volatile.Read(ref _pumped);

    /// <summary>The exception that ended the loop early, or null.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>Starts the thread. Throws if already started.</summary>
    public void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("The dispatcher has already been started.");
        }

        _started = true;
        _thread.Start();
    }

    /// <summary>Stops the loop, drains the ring once more, and joins.</summary>
    public void Dispose()
    {
        _stop = true;
        if (_started && _thread.IsAlive)
        {
            _thread.Join();
        }
    }

    private void Loop()
    {
        _running = true;
        try
        {
            while (!_stop)
            {
                _hub.FramesAvailable.WaitOne(_idleWait);
                Interlocked.Add(ref _pumped, _hub.Pump());
            }

            Interlocked.Add(ref _pumped, _hub.Pump());
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _failure, ex);
        }
        finally
        {
            _running = false;
        }
    }
}
