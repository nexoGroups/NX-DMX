namespace NxDmx.Core.Fixtures;

/// <summary>
/// What a DMX channel does. The first seven values keep their meaning from the early versions; show files store
/// the name, so new attributes are only ever appended.
/// </summary>
public enum FixtureAttribute
{
    Dimmer,
    Red,
    Green,
    Blue,
    White,
    Pan,
    Tilt,
    Amber,
    UV,
    Cyan,
    Magenta,
    Yellow,
    ColorWheel,
    Zoom,
    Focus,
    Iris,
    Frost,
    Prism,
    Gobo,
    Gobo2,
    GoboRotation,
    Strobe,
    Speed,
    Control,
    Custom1,
    Custom2,
}

/// <summary>The attribute pages of the programmer, as on ONYX and grandMA.</summary>
public enum AttributeGroup
{
    Intensity,
    Position,
    Color,
    Beam,
    Control,
}

public static class FixtureAttributeExtensions
{
    /// <summary>Intensity merges highest-takes-precedence between playbacks; everything else latest-takes-precedence.</summary>
    public static bool IsHtp(this FixtureAttribute attribute) => attribute == FixtureAttribute.Dimmer;

    /// <summary>Additive emitters. A fixture that has these but no dimmer channel gets a virtual dimmer that scales them.</summary>
    public static bool IsColor(this FixtureAttribute attribute) =>
        attribute is FixtureAttribute.Red or FixtureAttribute.Green or FixtureAttribute.Blue or FixtureAttribute.White
            or FixtureAttribute.Amber or FixtureAttribute.UV;

    public static AttributeGroup Group(this FixtureAttribute attribute) => attribute switch
    {
        FixtureAttribute.Dimmer => AttributeGroup.Intensity,
        FixtureAttribute.Pan or FixtureAttribute.Tilt => AttributeGroup.Position,
        FixtureAttribute.Red or FixtureAttribute.Green or FixtureAttribute.Blue or FixtureAttribute.White
            or FixtureAttribute.Amber or FixtureAttribute.UV or FixtureAttribute.Cyan or FixtureAttribute.Magenta
            or FixtureAttribute.Yellow or FixtureAttribute.ColorWheel => AttributeGroup.Color,
        FixtureAttribute.Zoom or FixtureAttribute.Focus or FixtureAttribute.Iris or FixtureAttribute.Frost
            or FixtureAttribute.Prism or FixtureAttribute.Gobo or FixtureAttribute.Gobo2
            or FixtureAttribute.GoboRotation or FixtureAttribute.Strobe => AttributeGroup.Beam,
        _ => AttributeGroup.Control,
    };

    /// <summary>Short text for narrow places (the programmer table).</summary>
    public static string ShortName(this FixtureAttribute attribute) => attribute switch
    {
        FixtureAttribute.Dimmer => "Dim",
        FixtureAttribute.Red => "R",
        FixtureAttribute.Green => "G",
        FixtureAttribute.Blue => "B",
        FixtureAttribute.White => "W",
        FixtureAttribute.Amber => "A",
        FixtureAttribute.Cyan => "C",
        FixtureAttribute.Magenta => "M",
        FixtureAttribute.Yellow => "Y",
        FixtureAttribute.ColorWheel => "ColW",
        FixtureAttribute.GoboRotation => "GRot",
        FixtureAttribute.Custom1 => "Cu1",
        FixtureAttribute.Custom2 => "Cu2",
        _ => attribute.ToString(),
    };

    public static bool TryParse(string word, out FixtureAttribute attribute) =>
        Enum.TryParse(word, ignoreCase: true, out attribute) && Enum.IsDefined(attribute) && !word.All(char.IsAsciiDigit);
}
