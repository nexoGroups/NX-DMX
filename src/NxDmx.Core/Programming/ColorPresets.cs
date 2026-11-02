using NxDmx.Core.Fixtures;

namespace NxDmx.Core.Programming;

/// <summary>A named colour as red/green/blue amounts (0..1) with the extra emitters some fixtures have.</summary>
public sealed record ColorPreset(string Name, double Red, double Green, double Blue, double White = 0, double Amber = 0, bool Custom = false)
{
    /// <summary>Parses <c>#RRGGBB</c> (the # is optional). False when the text is not a six-digit hex colour.</summary>
    public static bool TryParseHex(string text, out double red, out double green, out double blue)
    {
        red = green = blue = 0;
        var hex = text.Trim().TrimStart('#');
        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
            return false;

        red = Convert.ToInt32(hex[..2], 16) / 255.0;
        green = Convert.ToInt32(hex[2..4], 16) / 255.0;
        blue = Convert.ToInt32(hex[4..], 16) / 255.0;
        return true;
    }

    public static ColorPreset? FromHex(string name, string hex) =>
        TryParseHex(hex, out var r, out var g, out var b) ? new ColorPreset(name.Trim(), r, g, b, Custom: true) : null;

    /// <summary>For drawing a swatch of the colour on screen.</summary>
    public string Hex => $"#{(int)Math.Round(Red * 255):X2}{(int)Math.Round(Green * 255):X2}{(int)Math.Round(Blue * 255):X2}";

    /// <summary>
    /// The values this colour means for a fixture type: RGB, white and amber emitters, subtractive CMY, and the colour
    /// wheel when one of its functions carries the colour's name. Attributes the type does not have are left out.
    /// </summary>
    public IReadOnlyList<(FixtureAttribute Attribute, double Value)> ValuesFor(FixtureType type)
    {
        var values = new List<(FixtureAttribute, double)>();

        void Add(FixtureAttribute attribute, double value)
        {
            if (type.HasChannel(attribute))
                values.Add((attribute, value));
        }

        Add(FixtureAttribute.Red, Red);
        Add(FixtureAttribute.Green, Green);
        Add(FixtureAttribute.Blue, Blue);
        Add(FixtureAttribute.White, White);
        Add(FixtureAttribute.Amber, Amber);
        Add(FixtureAttribute.UV, 0);
        Add(FixtureAttribute.Cyan, 1 - Red);
        Add(FixtureAttribute.Magenta, 1 - Green);
        Add(FixtureAttribute.Yellow, 1 - Blue);

        var wheel = type.Find(FixtureAttribute.ColorWheel)?.FunctionList
            .FirstOrDefault(f => f.Name.Equals(Name, StringComparison.OrdinalIgnoreCase));
        if (wheel is not null)
            values.Add((FixtureAttribute.ColorWheel, wheel.StartValue));

        return values;
    }
}

public static class ColorPresets
{
    public static IReadOnlyList<ColorPreset> All { get; } =
    [
        new("Red", 1, 0, 0),
        new("Orange", 1, 0.45, 0, Amber: 0.5),
        new("Amber", 1, 0.7, 0, Amber: 1),
        new("Yellow", 1, 1, 0),
        new("Lime", 0.5, 1, 0),
        new("Green", 0, 1, 0),
        new("Cyan", 0, 1, 1),
        new("Blue", 0, 0, 1),
        new("Purple", 0.5, 0, 1),
        new("Magenta", 1, 0, 1),
        new("Pink", 1, 0.35, 0.65),
        new("Warm White", 1, 0.75, 0.45, Amber: 0.4),
        new("White", 1, 1, 1, White: 1),
        new("Cool White", 0.7, 0.85, 1, White: 0.5),
    ];

    public static ColorPreset? Find(string name) =>
        All.FirstOrDefault(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
}
