namespace NxDmx.App.Services;

/// <summary>
/// What a long job tells the progress window: how far it really is, what it is doing, and whether it can still be
/// cancelled. The job writes, the window reads a few times a second. Nothing here moves by itself: the bar only goes
/// forward when the work reports that it did.
/// </summary>
public sealed class BusyJob(bool cancellable)
{
    private readonly CancellationTokenSource _cancel = new();
    private double _fraction;
    private string _status = "";
    private int _canCancel = cancellable ? 1 : 0;

    public CancellationToken Token => _cancel.Token;

    /// <summary>How much of the job is done, 0 to 1.</summary>
    public double Fraction => Volatile.Read(ref _fraction);

    public string Status => Volatile.Read(ref _status);

    public bool CanCancel => Volatile.Read(ref _canCancel) == 1;

    public bool IsCancelRequested => _cancel.IsCancellationRequested;

    public void Cancel()
    {
        if (CanCancel)
            _cancel.Cancel();
    }

    /// <summary>From here on the job changes the show and cannot be cancelled any more.</summary>
    public void PointOfNoReturn() => Interlocked.Exchange(ref _canCancel, 0);

    /// <summary>
    /// The progress of one step that covers <paramref name="from"/> to <paramref name="to"/> of the whole job. While the
    /// job can still be cancelled, a report is also where the cancel takes effect: it throws, and the work stops.
    /// </summary>
    public IProgress<double> Step(double from, double to, string status)
    {
        Volatile.Write(ref _status, status);
        return new StepProgress(this, from, to);
    }

    private sealed class StepProgress(BusyJob job, double from, double to) : IProgress<double>
    {
        public void Report(double value)
        {
            if (job.CanCancel)
                job.Token.ThrowIfCancellationRequested();

            var fraction = from + Math.Clamp(value, 0, 1) * (to - from);
            double seen;
            do
            {
                seen = job.Fraction;
            }
            while (fraction > seen && Interlocked.CompareExchange(ref job._fraction, fraction, seen) != seen);
        }
    }
}
