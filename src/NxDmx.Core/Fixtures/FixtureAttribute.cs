namespace NxDmx.Core.Fixtures;

public enum FixtureAttribute
{
    Dimmer,
    Red,
    Green,
    Blue,
    White,
    Pan,
    Tilt,
}

public static class FixtureAttributeExtensions
{
    /// <summary>Intensity merges highest-takes-precedence between playbacks; everything else latest-takes-precedence.</summary>
    public static bool IsHtp(this FixtureAttribute attribute) => attribute == FixtureAttribute.Dimmer;

    public static bool IsColor(this FixtureAttribute attribute) =>
        attribute is FixtureAttribute.Red or FixtureAttribute.Green or FixtureAttribute.Blue or FixtureAttribute.White;

    public static bool TryParse(string word, out FixtureAttribute attribute) =>
        Enum.TryParse(word, ignoreCase: true, out attribute) && Enum.IsDefined(attribute);
}
