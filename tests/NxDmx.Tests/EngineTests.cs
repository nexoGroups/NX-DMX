using NxDmx.Core;
using NxDmx.Core.Dmx;
using NxDmx.Core.Engine;

namespace NxDmx.Tests;

public class EngineTests
{
    private sealed class RecordingOutput : IDmxOutput
    {
        private readonly object _gate = new();
        private readonly List<(int Universe, byte[] Data)> _sent = [];

        public List<(int Universe, byte[] Data)> Sent
        {
            get
            {
                lock (_gate)
                    return [.. _sent];
            }
        }

        public void Send(int universe, ReadOnlySpan<byte> data)
        {
            lock (_gate)
                _sent.Add((universe, data.ToArray()));
        }
    }

    private sealed class FailingOutput : IDmxOutput
    {
        public void Send(int universe, ReadOnlySpan<byte> data) => throw new InvalidOperationException("network down");
    }

    private static LightingConsole PatchedConsole()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Type Dimmer At 1.1");
        console.Execute("Patch Fixture 2 Type Dimmer At 2.1");
        console.Execute("Fixture 2 Full");
        return console;
    }

    [Fact]
    public void Tick_sends_every_patched_universe()
    {
        var output = new RecordingOutput();
        using var engine = new DmxEngine(PatchedConsole()) { Output = output };

        engine.Tick();

        Assert.Equal([1, 2], output.Sent.Select(s => s.Universe));
        Assert.Equal(0, output.Sent[0].Data[0]);
        Assert.Equal(255, output.Sent[1].Data[0]);
        Assert.Equal(1, engine.FramesSent);
        Assert.Equal(2, engine.LastFrame!.Universes.Count);
    }

    [Fact]
    public void Tick_renders_even_without_output()
    {
        using var engine = new DmxEngine(PatchedConsole());

        engine.Tick();

        Assert.NotNull(engine.LastFrame);
        Assert.Equal(0, engine.FramesSent);
    }

    [Fact]
    public void An_output_error_is_kept_and_cleared_when_sending_works_again()
    {
        using var engine = new DmxEngine(PatchedConsole()) { Output = new FailingOutput() };

        engine.Tick();
        Assert.Equal("Universe 1: network down", engine.LastError);
        Assert.Equal(0, engine.FramesSent);

        engine.Output = new RecordingOutput();
        engine.Tick();
        Assert.Null(engine.LastError);
        Assert.Equal(1, engine.FramesSent);
    }

    [Fact]
    public async Task The_loop_keeps_sending_until_stopped()
    {
        var output = new RecordingOutput();
        using var engine = new DmxEngine(PatchedConsole(), rateHz: 100) { Output = output };

        engine.Start();
        await Task.Delay(400);
        engine.Stop();

        var frames = engine.FramesSent;
        Assert.InRange(frames, 4, 60);

        await Task.Delay(100);
        Assert.Equal(frames, engine.FramesSent);
    }

    [Fact]
    public void Commands_and_rendering_can_run_at_the_same_time()
    {
        var console = PatchedConsole();
        using var engine = new DmxEngine(console, rateHz: 200) { Output = new RecordingOutput() };
        engine.Start();

        for (var i = 0; i < 500; i++)
        {
            Assert.True(console.Execute($"Fixture 1 At {i % 100}").Success);
            Assert.True(console.Execute("Oops").Success);
        }

        engine.Stop();
        Assert.Null(engine.LastError);
    }

    [Fact]
    public void Changed_is_raised_after_each_command()
    {
        var console = PatchedConsole();
        var count = 0;
        console.Changed += () => count++;

        console.Execute("Fixture 1 At 10");
        console.Execute("Banana");

        Assert.Equal(2, count);
    }
}
