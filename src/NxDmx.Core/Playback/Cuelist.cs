using NxDmx.Core.Programming;

namespace NxDmx.Core.Playback;

/// <summary>
/// An ordered list of cues with a current position. Playback tracks: the output is every cue up to the
/// current one applied in order, so a cue only needs to state what changes.
/// </summary>
public sealed class Cuelist(int number)
{
    private readonly SortedDictionary<decimal, Cue> _cues = new();

    public int Number { get; } = number;

    public string Label { get; set; } = $"Cuelist {number}";

    public IEnumerable<Cue> Cues => _cues.Values;

    /// <summary>Number of the cue most recently gone to, or null while the cuelist is off.</summary>
    public decimal? CurrentNumber { get; private set; }

    public bool IsActive => CurrentNumber is not null;

    public Cue? Find(decimal number) => _cues.GetValueOrDefault(number);

    public void Set(Cue cue) => _cues[cue.Number] = cue;

    public bool Remove(decimal number) => _cues.Remove(number);

    /// <summary>Steps to the next cue; returns null when there is none.</summary>
    public Cue? Go()
    {
        var next = _cues.Values.FirstOrDefault(c => CurrentNumber is null || c.Number > CurrentNumber);
        if (next is not null)
            CurrentNumber = next.Number;
        return next;
    }

    /// <summary>Steps to the previous cue; returns null when off or already on the first cue.</summary>
    public Cue? Back()
    {
        if (CurrentNumber is null)
            return null;

        var previous = _cues.Values.LastOrDefault(c => c.Number < CurrentNumber);
        if (previous is not null)
            CurrentNumber = previous.Number;
        return previous;
    }

    public bool GoTo(decimal number)
    {
        if (!_cues.ContainsKey(number))
            return false;

        CurrentNumber = number;
        return true;
    }

    public void Release() => CurrentNumber = null;

    internal void RestorePosition(decimal? number) => CurrentNumber = number;

    /// <summary>All values in effect at the current cue.</summary>
    public Dictionary<ValueKey, double> Tracked()
    {
        var result = new Dictionary<ValueKey, double>();
        if (CurrentNumber is null)
            return result;

        foreach (var cue in _cues.Values.TakeWhile(c => c.Number <= CurrentNumber))
        {
            foreach (var (key, value) in cue.Values)
                result[key] = value;
        }

        return result;
    }
}
