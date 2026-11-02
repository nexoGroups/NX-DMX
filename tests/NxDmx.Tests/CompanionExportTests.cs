using NxDmx.Core.Dmx;

namespace NxDmx.Tests;

public class CompanionExportTests
{
    private static byte[] Universe(params (int Channel, byte Value)[] values)
    {
        var data = new byte[512];
        foreach (var (channel, value) in values)
            data[channel - 1] = value;

        return data;
    }

    [Fact]
    public void Non_zero_channels_use_the_channel_comma_value_schema()
    {
        var data = Universe((17, 255), (18, 128), (33, 255), (34, 128));

        Assert.Equal("17,255;18,128;33,255;34,128;", CompanionExport.Format(data, CompanionScope.NonZero, []));
    }

    [Fact]
    public void Patched_scope_keeps_zeros_of_patched_channels_and_skips_the_rest()
    {
        var data = Universe((1, 200), (3, 9), (40, 77));

        Assert.Equal("1,200;2,0;3,9;", CompanionExport.Format(data, CompanionScope.Patched, [(1, 3)]));
    }

    [Fact]
    public void Patched_ranges_are_clipped_to_the_universe()
    {
        var text = CompanionExport.Format(new byte[512], CompanionScope.Patched, [(511, 600), (-2, 1)]);

        Assert.Equal("1,0;511,0;512,0;", text);
    }

    [Fact]
    public void All_scope_writes_512_pairs()
    {
        var text = CompanionExport.Format(Universe((512, 5)), CompanionScope.All, []);

        Assert.StartsWith("1,0;2,0;", text);
        Assert.EndsWith("512,5;", text);
        Assert.Equal(512, text.Count(c => c == ';'));
    }

    [Fact]
    public void An_empty_universe_gives_empty_text()
    {
        Assert.Equal("", CompanionExport.Format(new byte[512], CompanionScope.NonZero, []));
    }
}
