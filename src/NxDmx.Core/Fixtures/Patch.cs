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

    /// <summary>Fixtures that share DMX channels with another fixture, as the numbers of both.</summary>
    public HashSet<int> FindConflicts()
    {
        var conflicts = new HashSet<int>();
        foreach (var group in _fixtures.Values.GroupBy(f => f.Universe))
        {
            var sorted = group.OrderBy(f => f.Address).ThenBy(f => f.Id).ToList();
            var reach = 0;
            Fixture? reaching = null;
            foreach (var fixture in sorted)
            {
                if (reaching is not null && fixture.Address <= reach)
                {
                    conflicts.Add(fixture.Id);
                    conflicts.Add(reaching.Id);
                }

                if (fixture.EndAddress > reach)
                {
                    reach = fixture.EndAddress;
                    reaching = fixture;
                }
            }
        }

        return conflicts;
    }

    /// <summary>The fixture, other than those in <paramref name="ignore"/>, that occupies any channel of the given range.</summary>
    public Fixture? FindOccupant(int universe, int address, int length, ISet<int>? ignore = null) =>
        _fixtures.Values.FirstOrDefault(f =>
            f.Universe == universe && f.Address <= address + length - 1 && address <= f.EndAddress
            && (ignore is null || !ignore.Contains(f.Id)));

    /// <summary>
    /// First address in the universe, at or after <paramref name="from"/>, where <paramref name="length"/> channels
    /// are free. Null when the universe has no such gap.
    /// </summary>
    public int? FindFreeAddress(int universe, int length, int from = 1)
    {
        var address = Math.Max(1, from);
        foreach (var fixture in _fixtures.Values.Where(f => f.Universe == universe).OrderBy(f => f.Address))
        {
            if (fixture.EndAddress < address)
                continue;
            if (fixture.Address >= address + length)
                break;

            address = fixture.EndAddress + 1;
        }

        return address + length - 1 <= 512 ? address : null;
    }

    /// <summary>The lowest fixture number, at or after <paramref name="from"/>, that is not used yet.</summary>
    public int NextFreeId(int from = 1)
    {
        var id = Math.Max(1, from);
        while (_fixtures.ContainsKey(id))
            id++;

        return id;
    }
}
