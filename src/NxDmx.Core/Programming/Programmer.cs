using NxDmx.Core.Fixtures;

namespace NxDmx.Core.Programming;

/// <summary>
/// The operator's live edit buffer: which fixtures are selected and which values were set on them.
/// Programmer values override every playback until they are cleared. Values are normalised 0..1.
/// </summary>
public sealed class Programmer
{
    private readonly List<int> _selection = [];
    private readonly HashSet<int> _selected = [];
    private readonly Dictionary<ValueKey, double> _values = new();

    /// <summary>Selected fixture numbers in the order they were selected.</summary>
    public IReadOnlyList<int> Selection => _selection;

    public IReadOnlyDictionary<ValueKey, double> Values => _values;

    public bool IsEmpty => _values.Count == 0;

    public void Select(IEnumerable<int> ids)
    {
        ClearSelection();
        AddToSelection(ids);
    }

    public void AddToSelection(IEnumerable<int> ids)
    {
        foreach (var id in ids)
        {
            if (_selected.Add(id))
                _selection.Add(id);
        }
    }

    public void RemoveFromSelection(IEnumerable<int> ids)
    {
        var remove = ids.ToHashSet();
        _selection.RemoveAll(remove.Contains);
        _selected.ExceptWith(remove);
    }

    public void ClearSelection()
    {
        _selection.Clear();
        _selected.Clear();
    }

    public void Set(int fixtureId, FixtureAttribute attribute, double value) =>
        _values[new ValueKey(fixtureId, attribute)] = Math.Clamp(value, 0, 1);

    public void ClearValues() => _values.Clear();

    public void RemoveFixture(int fixtureId)
    {
        RemoveFromSelection([fixtureId]);
        foreach (var key in _values.Keys.Where(k => k.FixtureId == fixtureId).ToList())
            _values.Remove(key);
    }
}
