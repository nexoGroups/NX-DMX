namespace NxDmx.Core.Fixtures;

public sealed class Patch
{
    private readonly SortedDictionary<int, Fixture> _fixtures = new();

    /// <summary>All fixtures in ascending fixture-number order.</summary>
    public IEnumerable<Fixture> Fixtures => _fixtures.Values;

    public int Count => _fixtures.Count;

    public Fixture? Find(int id) => _fixtures.GetValueOrDefault(id);

    /// <summary>Adds the fixture, replacing any fixture that already has the same number.</summary>
    public void Add(Fixture fixture) => _fixtures[fixture.Id] = fixture;

    public bool Remove(int id) => _fixtures.Remove(id);

    public void Clear() => _fixtures.Clear();
}
