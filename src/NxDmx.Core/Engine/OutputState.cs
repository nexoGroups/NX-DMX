namespace NxDmx.Core.Engine;

/// <summary>
/// The live output controls: blackout and the grand master. They are not part of a show and not programming steps,
/// so Oops does not touch them. Access goes through the console's lock.
/// </summary>
public sealed class OutputState
{
    private double _grandMaster = 1;

    public bool Blackout { get; set; }

    /// <summary>0..1, scales every intensity.</summary>
    public double GrandMaster
    {
        get => _grandMaster;
        set => _grandMaster = Math.Clamp(value, 0, 1);
    }

    /// <summary>The factor that applies to intensity right now.</summary>
    public double IntensityFactor => Blackout ? 0 : _grandMaster;
}
