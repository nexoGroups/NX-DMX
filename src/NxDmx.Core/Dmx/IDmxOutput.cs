namespace NxDmx.Core.Dmx;

public interface IDmxOutput
{
    /// <summary>Sends one universe. <paramref name="universe"/> is 1-based, as shown to the operator.</summary>
    void Send(int universe, ReadOnlySpan<byte> data);
}
