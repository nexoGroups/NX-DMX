namespace NxDmx.Core.Engine;

/// <summary>
/// The live output controls: blackout, the grand master and the positions of the faders. They are not part of a show
/// and not programming steps, so Oops does not touch them. Access goes through the console's lock.
/// </summary>
public sealed class OutputState
{
    private double _grandMaster = 1;
    private readonly Dictionary<int, double> _faders = new();

    public bool Blackout { get; set; }

    /// <summary>0..1, scales every intensity.</summary>
    public double GrandMaster
    {
        get => _grandMaster;
        set => _grandMaster = Math.Clamp(value, 0, 1);
    }

    /// <summary>The factor that applies to intensity right now.</summary>
    public double IntensityFactor => Blackout ? 0 : _grandMaster;

    /// <summary>Position of a fader, or <paramref name="fallback"/> when it was never moved.</summary>
    public double FaderLevel(int fader, double fallback) => _faders.GetValueOrDefault(fader, fallback);

    public void SetFaderLevel(int fader, double level) => _faders[fader] = Math.Clamp(level, 0, 1);

    public void ForgetFader(int fader) => _faders.Remove(fader);
}
