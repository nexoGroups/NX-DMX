using NxDmx.Core.Fixtures;

namespace NxDmx.Tests;

public class RenderingTests
{
    [Fact]
    public void Unset_fixtures_output_their_defaults()
    {
        var rig = Rig.WithPatch();

        var universe = rig.Universe(1);

        // RGBDim: dimmer closed, colour full white. Dimmer fixtures: closed.
        Assert.Equal(new byte[] { 0, 255, 255, 255 }, universe[..4]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, universe[100..104]);
        Assert.All(universe[104..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Programmer_values_reach_the_right_channels()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 2 At 50", "Fixture 2 Red At 0", "Fixture 22 Full");

        var universe = rig.Universe(1);

        Assert.Equal(128, universe[4]); // fixture 2 dimmer
        Assert.Equal(0, universe[5]); // red
        Assert.Equal(255, universe[6]);
        Assert.Equal(255, universe[7]);
        Assert.Equal(255, universe[101]); // fixture 22 at 1.102
        Assert.Equal(0, universe[100]);
    }

    [Fact]
    public void Colour_only_fixtures_use_a_virtual_dimmer()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 31 Thru 32 Type RGB At 2.1", "Fixture 31 At 50");

        Assert.Equal(new byte[] { 128, 128, 128, 0, 0, 0 }, rig.Universe(2)[..6]);

        rig.Do("Green At 0");
        Assert.Equal(new byte[] { 128, 0, 128 }, rig.Universe(2)[..3]);
    }

    [Fact]
    public void White_is_scaled_by_the_virtual_dimmer_too()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type RGBW At 1.1", "Fixture 1 At 100", "White At 50", "Red At 0");

        Assert.Equal(new byte[] { 0, 255, 255, 128 }, rig.Universe(1)[..4]);
    }

    [Fact]
    public void Pan_and_tilt_use_two_channels()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 41 Type Mover At 3.1");

        // Pan and tilt default to the middle: 0x8000.
        Assert.Equal(new byte[] { 0x80, 0x00, 0x80, 0x00, 0 }, rig.Universe(3)[..5]);

        rig.Do("Fixture 41 Pan At 100", "Tilt At 0", "Full");
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0x00, 0x00, 255 }, rig.Universe(3)[..5]);
    }

    [Fact]
    public void Only_universes_with_fixtures_are_rendered()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type Dimmer At 1.1", "Patch Fixture 2 Type Dimmer At 4.1");

        var frame = NxDmx.Core.Engine.FrameRenderer.Render(rig.Show, rig.Programmer);

        Assert.Equal([1, 4], frame.Universes.Keys);
        Assert.All(frame.Universes.Values, data => Assert.Equal(512, data.Length));
    }

    [Fact]
    public void Playback_output_follows_the_cuelist()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "ClearAll");
        Assert.Equal(0, rig.Universe(1)[0]);

        rig.Do("Go");
        Assert.Equal(255, rig.Universe(1)[0]);

        rig.Do("Off");
        Assert.Equal(0, rig.Universe(1)[0]);
    }

    [Fact]
    public void Cues_track_and_Back_removes_what_a_later_cue_added()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "ClearAll", "Fixture 2 Full", "Record Cue 2", "ClearAll");

        rig.Do("Go");
        Assert.Equal((255, 0), (rig.Universe(1)[0], rig.Universe(1)[4]));

        rig.Do("Go");
        Assert.Equal((255, 255), (rig.Universe(1)[0], rig.Universe(1)[4]));

        rig.Do("Back");
        Assert.Equal((255, 0), (rig.Universe(1)[0], rig.Universe(1)[4]));
    }

    [Fact]
    public void A_later_cue_overrides_an_earlier_value()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "Fixture 1 At 20", "Record Cue 2", "ClearAll");

        rig.Do("Go", "Go");

        Assert.Equal(51, rig.Universe(1)[0]);
    }

    [Fact]
    public void The_programmer_overrides_playback_until_it_is_cleared()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "ClearAll", "Go");
        Assert.Equal(255, rig.Universe(1)[0]);

        rig.Do("Fixture 1 At 10");
        Assert.Equal(26, rig.Universe(1)[0]);

        rig.Do("ClearAll");
        Assert.Equal(255, rig.Universe(1)[0]);
    }

    [Fact]
    public void Intensity_is_highest_takes_precedence_between_cuelists()
    {
        var rig = Rig.WithPatch();
        rig.Do(
            "Fixture 1 At 80", "Record Cue 1 Cuelist 1", "ClearAll",
            "Fixture 1 At 30", "Record Cue 1 Cuelist 2", "ClearAll",
            "Go Cuelist 1", "Go Cuelist 2");

        Assert.Equal(204, rig.Universe(1)[0]); // 80 %, not the later 30 %
    }

    [Fact]
    public void Other_attributes_follow_the_latest_cuelist()
    {
        var rig = Rig.WithPatch();
        rig.Do(
            "Fixture 1 Red At 100", "Record Cue 1 Cuelist 1", "ClearAll",
            "Fixture 1 Red At 20", "Record Cue 1 Cuelist 2", "ClearAll",
            "Go Cuelist 1", "Go Cuelist 2");
        Assert.Equal(51, rig.Universe(1)[1]);

        rig.Do("Off Cuelist 2");
        Assert.Equal(255, rig.Universe(1)[1]);
    }

    [Fact]
    public void Update_changes_what_the_cue_plays()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "ClearAll", "Fixture 2 Full", "Update Cue 1", "ClearAll", "Go");

        Assert.Equal((255, 255), (rig.Universe(1)[0], rig.Universe(1)[4]));
    }

    [Fact]
    public void Value_of_falls_back_to_the_type_default()
    {
        var rig = Rig.WithPatch();
        var fixture = rig.Show.Patch.Find(1)!;
        var resolved = NxDmx.Core.Engine.FrameRenderer.ResolveValues(rig.Show, rig.Programmer);

        Assert.Equal(1, NxDmx.Core.Engine.FrameRenderer.ValueOf(fixture, FixtureAttribute.Red, resolved));
        Assert.Equal(0, NxDmx.Core.Engine.FrameRenderer.ValueOf(fixture, FixtureAttribute.Dimmer, resolved));
        Assert.Equal(0, NxDmx.Core.Engine.FrameRenderer.ValueOf(fixture, FixtureAttribute.Pan, resolved));
    }
}
