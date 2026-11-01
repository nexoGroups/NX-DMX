using NxDmx.Core.Dmx;

namespace NxDmx.Core.Engine;

/// <summary>Renders the console state at a fixed rate and sends every universe each tick, as Art-Net nodes expect.</summary>
public sealed class DmxEngine : IDisposable
{
    private readonly LightingConsole _console;
    private readonly TimeSpan _period;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IDmxOutput? _output;
    private DmxFrame? _lastFrame;
    private string? _lastError;
    private long _framesSent;

    /// <param name="rateHz">Art-Net allows up to 44 frames per second per universe.</param>
    public DmxEngine(LightingConsole console, double rateHz = 40)
    {
        _console = console;
        _period = TimeSpan.FromSeconds(1 / rateHz);
    }

    /// <summary>Where frames go. May be changed (or set to null to stop sending) while running.</summary>
    public IDmxOutput? Output
    {
        get => Volatile.Read(ref _output);
        set => Volatile.Write(ref _output, value);
    }

    public DmxFrame? LastFrame => Volatile.Read(ref _lastFrame);

    public string? LastError => Volatile.Read(ref _lastError);

    public long FramesSent => Interlocked.Read(ref _framesSent);

    public void Start()
    {
        if (_loop is not null)
            return;

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _loop?.Wait();
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    /// <summary>Renders and sends one frame. The timer calls this; tests call it directly.</summary>
    public void Tick()
    {
        var frame = _console.Render();
        Volatile.Write(ref _lastFrame, frame);

        var output = Output;
        if (output is null)
            return;

        try
        {
            foreach (var (universe, data) in frame.Universes)
                output.Send(universe, data);

            Interlocked.Increment(ref _framesSent);
            Volatile.Write(ref _lastError, null);
        }
        catch (Exception ex)
        {
            // A cable pulled or an adapter switched off must not stop the engine; keep trying every tick.
            Volatile.Write(ref _lastError, ex.Message);
        }
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(_period);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                Tick();
        }
        catch (OperationCanceledException)
        {
        }
    }
}
