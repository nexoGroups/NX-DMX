namespace NxDmx.Core.Fixtures;

public sealed class Fixture
{
    public Fixture(int id, FixtureType type, int universe, int address, string? label = null)
    {
        Id = id;
        Type = type;
        Universe = universe;
        Address = address;
        Label = label ?? $"{type.Name} {id}";
    }

    /// <summary>The number the operator types: <c>Fixture 5</c>.</summary>
    public int Id { get; }

    public string Label { get; set; }

    public FixtureType Type { get; set; }

    /// <summary>1-based, as shown to the operator.</summary>
    public int Universe { get; set; }

    /// <summary>First DMX channel, 1..512.</summary>
    public int Address { get; set; }

    /// <summary>Last DMX channel the fixture occupies.</summary>
    public int EndAddress => Address + Type.Footprint - 1;

    public bool Overlaps(Fixture other) =>
        other != this && other.Universe == Universe && other.Address <= EndAddress && Address <= other.EndAddress;
}
