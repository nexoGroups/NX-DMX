namespace NxDmx.Core;

/// <summary>
/// Reports how much of a piece of work is done, from 0 to 1, as the work is counted. It reports whole percents at most
/// (a progress bar does not need more) and never a figure the work has not reached.
/// </summary>
public sealed class ProgressMeter(IProgress<double>? progress, double total)
{
    private double _done;
    private int _lastPercent = -1;

    /// <summary>Counts <paramref name="amount"/> units of work as done.</summary>
    public void Add(double amount)
    {
        _done += amount;
        if (progress is null || total <= 0)
            return;

        var percent = (int)Math.Min(100, _done / total * 100);
        if (percent != _lastPercent)
        {
            _lastPercent = percent;
            progress.Report(percent / 100.0);
        }
    }
}

/// <summary>Maps the 0 to 1 of one step of a longer job to its share of the whole: a step covering 0.2 to 0.6 reports 0.2 to 0.6.</summary>
public sealed class ScaledProgress(IProgress<double> inner, double from, double to) : IProgress<double>
{
    public void Report(double value) => inner.Report(from + Math.Clamp(value, 0, 1) * (to - from));
}

/// <summary>
/// Reads from another stream and reports the part read so far. A reader that works through the stream as it goes
/// (the JSON parser does) shows real progress this way.
/// </summary>
public sealed class ProgressStream(Stream inner, IProgress<double>? progress) : Stream
{
    private int _lastPercent = -1;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Report(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Report(inner.Read(buffer));

    private int Report(int read)
    {
        if (progress is not null && inner.Length > 0)
        {
            var percent = (int)(inner.Position * 100 / inner.Length);
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                progress.Report(percent / 100.0);
            }
        }

        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();

        base.Dispose(disposing);
    }
}
