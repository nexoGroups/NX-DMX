using System.Globalization;
using System.Text;

namespace NxDmx.Core.Dmx;

/// <summary>Which channels of a universe an export contains.</summary>
public enum CompanionScope
{
    /// <summary>Every channel of a patched fixture, zeros included, so firing it restores the whole look.</summary>
    Patched,

    /// <summary>Only the channels above zero.</summary>
    NonZero,

    /// <summary>All 512 channels.</summary>
    All,
}

/// <summary>
/// Writes the values of one universe in the text format of the Bitfocus Companion action "Generic Art-Net: Set Complex Value":
/// <c>channel,value;channel,value;</c> with the channel 1 to 512 and the value 0 to 255.
/// </summary>
public static class CompanionExport
{
    /// <param name="data">The 512 channel values of the universe.</param>
    /// <param name="scope">Which channels to include.</param>
    /// <param name="patched">The channel ranges (1-based, both ends included) of the patched fixtures. Used by <see cref="CompanionScope.Patched"/>.</param>
    public static string Format(byte[] data, CompanionScope scope, IEnumerable<(int Start, int End)> patched)
    {
        var include = new bool[DmxFrame.UniverseSize + 1];
        switch (scope)
        {
            case CompanionScope.Patched:
                foreach (var (start, end) in patched)
                {
                    for (var channel = Math.Max(start, 1); channel <= Math.Min(end, DmxFrame.UniverseSize); channel++)
                        include[channel] = true;
                }

                break;
            case CompanionScope.NonZero:
                for (var channel = 1; channel <= DmxFrame.UniverseSize; channel++)
                    include[channel] = channel <= data.Length && data[channel - 1] > 0;

                break;
            default:
                Array.Fill(include, true);
                break;
        }

        var text = new StringBuilder();
        for (var channel = 1; channel <= DmxFrame.UniverseSize; channel++)
        {
            if (!include[channel])
                continue;

            var value = channel <= data.Length ? data[channel - 1] : (byte)0;
            text.Append(channel.ToString(CultureInfo.InvariantCulture)).Append(',').Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        return text.ToString();
    }
}
