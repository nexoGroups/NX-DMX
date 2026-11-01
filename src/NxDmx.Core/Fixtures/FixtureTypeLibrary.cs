using static NxDmx.Core.Fixtures.FixtureAttribute;

namespace NxDmx.Core.Fixtures;

/// <summary>Built-in fixture profiles. Profiles the operator creates live in the show (see <see cref="Show.CustomTypes"/>).</summary>
public static class FixtureTypeLibrary
{
    public static IReadOnlyList<FixtureType> All { get; } =
    [
        new("Dimmer", [new(Dimmer, 1)], isBuiltIn: true),
        new("RGB", [new(Red, 1, Default: 1), new(Green, 2, Default: 1), new(Blue, 3, Default: 1)], isBuiltIn: true),
        new("RGBW", [new(Red, 1, Default: 1), new(Green, 2, Default: 1), new(Blue, 3, Default: 1), new(White, 4)], isBuiltIn: true),
        new("RGBDim", [new(Dimmer, 1), new(Red, 2, Default: 1), new(Green, 3, Default: 1), new(Blue, 4, Default: 1)], isBuiltIn: true),
        new("Mover",
        [
            new(Pan, 1, FineOffset: 2, Default: 0.5),
            new(Tilt, 3, FineOffset: 4, Default: 0.5),
            new(Dimmer, 5),
            new(Red, 6, Default: 1),
            new(Green, 7, Default: 1),
            new(Blue, 8, Default: 1),
        ], isBuiltIn: true),
        new("Spot Mover",
        [
            new(Pan, 1, FineOffset: 2, Default: 0.5),
            new(Tilt, 3, FineOffset: 4, Default: 0.5),
            new(Speed, 5),
            new(Dimmer, 6),
            new(Strobe, 7, Functions: [new("Open", 0, 9), new("Strobe slow to fast", 10, 255)]),
            new(ColorWheel, 8, Functions:
            [
                new("Open", 0, 9), new("Red", 10, 19), new("Orange", 20, 29), new("Yellow", 30, 39), new("Green", 40, 49),
                new("Cyan", 50, 59), new("Blue", 60, 69), new("Magenta", 70, 79), new("Spin", 128, 255),
            ]),
            new(Gobo, 9, Functions:
            [
                new("Open", 0, 7), new("Gobo 1", 8, 15), new("Gobo 2", 16, 23), new("Gobo 3", 24, 31),
                new("Gobo 4", 32, 39), new("Gobo 5", 40, 47), new("Shake", 64, 127), new("Spin", 128, 255),
            ]),
            new(GoboRotation, 10),
            new(Prism, 11, Functions: [new("Off", 0, 127), new("Prism", 128, 255)]),
            new(Focus, 12, Default: 0.5),
            new(Zoom, 13, Default: 0.5),
            new(Frost, 14),
            new(Iris, 15, Default: 1),
        ], manufacturer: "Generic", isBuiltIn: true),
    ];

    /// <summary>Finds a built-in profile. A show's own profiles are found with <see cref="Show.FindType"/>.</summary>
    public static FixtureType? Find(string name) =>
        All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
