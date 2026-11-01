using NxDmx.Core.Programming;

namespace NxDmx.Core.Playback;

/// <summary>A recorded look. Holds only the values that were in the programmer; unlisted values track from earlier cues.</summary>
public sealed class Cue(decimal number, string label, Dictionary<ValueKey, double> values)
{
    public decimal Number { get; } = number;

    public string Label { get; set; } = label;

    public Dictionary<ValueKey, double> Values { get; } = values;
}
