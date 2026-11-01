namespace NxDmx.Core.Playback;

/// <summary>A named set of fixtures that can be selected in one go: <c>Group 1</c>.</summary>
public sealed class Group(int number, string label, List<int> fixtureIds)
{
    public int Number { get; } = number;

    public string Label { get; set; } = label;

    public List<int> FixtureIds { get; } = fixtureIds;
}
