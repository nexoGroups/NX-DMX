namespace NxDmx.Core.Playback;

/// <summary>What a fader controls.</summary>
public enum FaderKind
{
    None,

    /// <summary>The level of a cuelist: above 0 starts it, 0 releases it, in between it scales its intensity.</summary>
    Cuelist,

    /// <summary>A submaster: scales the intensity of every fixture of a group.</summary>
    Group,

    /// <summary>The grand master.</summary>
    Master,
}

/// <summary>
/// One fader of the fader bar and what is assigned to it. The assignment is part of the show; the position of the
/// fader is not (it lives in <see cref="Engine.OutputState"/>).
/// </summary>
public sealed class FaderConfig(int number)
{
    public const int MaxFaders = 40;

    public int Number { get; } = number;

    public FaderKind Kind { get; set; }

    /// <summary>Cuelist or group number, depending on <see cref="Kind"/>.</summary>
    public int Target { get; set; }

    /// <summary>Text under the fader. Empty takes the name of the assigned cuelist or group.</summary>
    public string Label { get; set; } = "";

    /// <summary>Colour of the fader as <c>#RRGGBB</c>. Empty means the default.</summary>
    public string Color { get; set; } = "";

    public FaderConfig Clone() => new(Number) { Kind = Kind, Target = Target, Label = Label, Color = Color };
}
