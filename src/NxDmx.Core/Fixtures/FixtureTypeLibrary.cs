using static NxDmx.Core.Fixtures.FixtureAttribute;

namespace NxDmx.Core.Fixtures;

/// <summary>Built-in fixture types. A loadable library (GDTF or own format) comes later.</summary>
public static class FixtureTypeLibrary
{
    public static IReadOnlyList<FixtureType> All { get; } =
    [
        new("Dimmer", [new(Dimmer, 1)]),
        new("RGB", [new(Red, 1, Default: 1), new(Green, 2, Default: 1), new(Blue, 3, Default: 1)]),
        new("RGBW", [new(Red, 1, Default: 1), new(Green, 2, Default: 1), new(Blue, 3, Default: 1), new(White, 4)]),
        new("RGBDim", [new(Dimmer, 1), new(Red, 2, Default: 1), new(Green, 3, Default: 1), new(Blue, 4, Default: 1)]),
        new("Mover",
        [
            new(Pan, 1, FineOffset: 2, Default: 0.5),
            new(Tilt, 3, FineOffset: 4, Default: 0.5),
            new(Dimmer, 5),
            new(Red, 6, Default: 1),
            new(Green, 7, Default: 1),
            new(Blue, 8, Default: 1),
        ]),
    ];

    public static FixtureType? Find(string name) =>
        All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
