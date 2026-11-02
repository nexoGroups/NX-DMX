using System.Net;
using NxDmx.Core;
using NxDmx.Core.Commands;
using NxDmx.Core.Dmx;
using NxDmx.Core.Engine;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Programming;
using NxDmx.Core.Storage;
using static NxDmx.Tests.ProfileHelpers;

namespace NxDmx.Tests;

public class ValueCommandTests
{
    [Fact]
    public void Several_attributes_can_follow_each_other()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Thru 2 Type Mover At 1.1", "Fixture 1 Thru 2 Pan At 25 Tilt At 70");

        Assert.Equal(0.25, rig.Value(1, FixtureAttribute.Pan));
        Assert.Equal(0.70, rig.Value(2, FixtureAttribute.Tilt));
    }

    [Fact]
    public void A_function_puts_the_channel_at_the_start_of_its_range()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type \"Spot Mover\" At 1.1", "Fixture 1", "Gobo Function \"Gobo 2\"");

        Assert.Equal(16 / 255.0, rig.Value(1, FixtureAttribute.Gobo), 6);
        Assert.Equal(16, rig.Universe(1)[8]);
    }

    [Fact]
    public void An_unknown_function_is_refused_and_changes_nothing()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type \"Spot Mover\" At 1.1", "Fixture 1");

        var result = rig.Fail("Gobo Function \"Nope\"");

        Assert.Contains("Nope", result.Message);
        Assert.Empty(rig.Programmer.Values);
    }

    [Fact]
    public void A_function_needs_an_attribute()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1");

        Assert.Contains("attribute", rig.Fail("Function Open").Message);
    }

    [Fact]
    public void A_colour_sets_all_emitters_the_fixture_has()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type RGBW At 1.1", "Patch Fixture 2 Type RGB At 1.11", "Fixture 1 Thru 2", "Color White");

        Assert.Equal(1, rig.Value(1, FixtureAttribute.White));
        Assert.Equal(1, rig.Value(1, FixtureAttribute.Red));
        Assert.False(rig.Programmer.Values.ContainsKey(new ValueKey(2, FixtureAttribute.White)));
        Assert.Equal(1, rig.Value(2, FixtureAttribute.Blue));
    }

    [Fact]
    public void A_colour_name_with_spaces_works_quoted_and_unquoted()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type RGB At 1.1", "Fixture 1", "Color \"Warm White\"");
        var quoted = rig.Value(1, FixtureAttribute.Green);

        rig.Do("ClearAll", "Fixture 1", "Color Warm White");

        Assert.Equal(quoted, rig.Value(1, FixtureAttribute.Green));
    }

    [Fact]
    public void A_colour_uses_the_colour_wheel_function_with_the_same_name()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type \"Spot Mover\" At 1.1", "Fixture 1", "Color Blue");

        Assert.Equal(60 / 255.0, rig.Value(1, FixtureAttribute.ColorWheel), 6);
    }

    [Fact]
    public void An_unknown_colour_lists_the_known_ones()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1");

        Assert.Contains("Red", rig.Fail("Color Chartreuse").Message);
    }

    [Fact]
    public void A_dotted_address_is_one_token_but_not_a_cue_number()
    {
        var rig = Rig.WithPatch();

        // Must be a normal command error, never an exception.
        var result = rig.Fail("Record Cue 10.0.0.5");

        Assert.False(result.Success);
    }
}

public class OutputControlTests
{
    [Fact]
    public void Blackout_and_master_scale_intensity_and_oops_ignores_them()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Fixture 21 At 50");

        rig.Do("Master At 50");
        Assert.Equal(128, rig.Universe(1)[0]);
        Assert.Equal(64, rig.Universe(1)[100]);
        Assert.Equal(255, rig.Universe(1)[1]); // colour of an RGBDim stays; only intensity is scaled

        rig.Do("Blackout");
        Assert.Equal(0, rig.Universe(1)[0]);
        Assert.Equal(0, rig.Universe(1)[100]);

        rig.Do("Blackout Off", "Master At 100");
        Assert.Equal(255, rig.Universe(1)[0]);

        rig.Do("Oops"); // undoes Fixture 21 At 50, not the output controls
        Assert.Equal(0, rig.Universe(1)[100]);
    }

    [Fact]
    public void Blackout_scales_the_virtual_dimmer_of_colour_only_fixtures()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type RGB At 1.1", "Fixture 1 At 100");
        Assert.Equal(255, rig.Universe(1)[0]);

        rig.Do("Blackout On");

        Assert.Equal(new byte[] { 0, 0, 0 }, rig.Universe(1)[..3]);
    }

    [Fact]
    public void Master_must_be_a_percentage()
    {
        var rig = new Rig();

        rig.Fail("Master At 120");
        rig.Fail("Master");
    }

    [Fact]
    public void Toggle_starts_and_releases_a_cuelist()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1");

        rig.Do("Toggle");
        Assert.True(rig.Show.Cuelists[1].IsActive);

        rig.Do("Toggle Cuelist 1");
        Assert.False(rig.Show.Cuelists[1].IsActive);
    }
}

public class PatchCommandTests
{
    [Fact]
    public void Patching_creates_the_universes_it_uses()
    {
        var rig = new Rig();

        rig.Do("Patch Fixture 1 Thru 200 Type RGBDim At 1.1");

        Assert.Equal([1, 2], rig.Show.Universes.Keys);
    }

    [Fact]
    public void Patching_onto_other_fixtures_is_refused()
    {
        var rig = Rig.WithPatch();

        var result = rig.Fail("Patch Fixture 30 Type RGB At 1.3");

        Assert.Contains("overlap Fixture 1", result.Message);
        Assert.Null(rig.Show.Patch.Find(30));
    }

    [Fact]
    public void Fixtures_can_be_moved_without_naming_the_type()
    {
        var rig = Rig.WithPatch();

        rig.Do("Patch Fixture 1 Thru 2 At 3.1");

        Assert.Equal((3, 1), (rig.Show.Patch.Find(1)!.Universe, rig.Show.Patch.Find(1)!.Address));
        Assert.Equal((3, 5), (rig.Show.Patch.Find(2)!.Universe, rig.Show.Patch.Find(2)!.Address));
        Assert.Equal("RGBDim", rig.Show.Patch.Find(1)!.Type.Name);
        Assert.Contains(3, rig.Show.Universes.Keys);
    }

    [Fact]
    public void A_fixture_can_be_moved_onto_its_own_old_channels()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type RGBDim At 1.1");

        rig.Do("Patch Fixture 1 At 1.3");

        Assert.Equal(3, rig.Show.Patch.Find(1)!.Address);
    }

    [Fact]
    public void The_type_of_existing_fixtures_can_be_changed_and_follows_the_default_label()
    {
        var rig = Rig.WithPatch();
        rig.Do("Label Fixture 2 \"Spot L\"");

        rig.Do("Patch Fixture 1 Thru 2 Type RGB");

        Assert.Equal("RGB", rig.Show.Patch.Find(1)!.Type.Name);
        Assert.Equal("RGB 1", rig.Show.Patch.Find(1)!.Label);
        Assert.Equal("Spot L", rig.Show.Patch.Find(2)!.Label);
        Assert.Equal(1, rig.Show.Patch.Find(1)!.Address);
    }

    [Fact]
    public void Changing_to_a_larger_type_that_hits_a_neighbour_is_refused()
    {
        var rig = Rig.WithPatch();

        var result = rig.Fail("Patch Fixture 1 Type Mover");

        Assert.Contains("overlap", result.Message);
        Assert.Equal("RGBDim", rig.Show.Patch.Find(1)!.Type.Name);
    }

    [Fact]
    public void New_fixtures_need_a_type_and_an_address()
    {
        var rig = new Rig();

        Assert.Contains("profile", rig.Fail("Patch Fixture 1 At 1.1").Message);
        Assert.Contains("address", rig.Fail("Patch Fixture 1 Type RGB").Message);
        Assert.Contains("what", rig.Fail("Patch Fixture 1").Message);
    }

    [Fact]
    public void A_label_is_numbered_for_ranges()
    {
        var rig = new Rig();

        rig.Do("Patch Fixture 1 Thru 2 Type RGB At 1.1 Label \"Wash\"", "Patch Fixture 5 Type RGB At 1.20 Label \"Solo\"");

        Assert.Equal("Wash 1", rig.Show.Patch.Find(1)!.Label);
        Assert.Equal("Wash 2", rig.Show.Patch.Find(2)!.Label);
        Assert.Equal("Solo", rig.Show.Patch.Find(5)!.Label);
    }

    [Fact]
    public void Patch_is_undone_with_oops_including_the_new_universe()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type RGB At 4.1");

        rig.Do("Oops");

        Assert.Equal(0, rig.Show.Patch.Count);
        Assert.Empty(rig.Show.Universes);
    }

    [Fact]
    public void Free_addresses_and_conflicts_are_found()
    {
        var rig = Rig.WithPatch();

        Assert.Equal(33, rig.Show.Patch.FindFreeAddress(1, 4));
        Assert.Equal(1, rig.Show.Patch.FindFreeAddress(2, 512));
        Assert.Null(rig.Show.Patch.FindFreeAddress(1, 512));
        Assert.Empty(rig.Show.Patch.FindConflicts());

        rig.Show.Patch.Add(new Fixture(50, FixtureTypeLibrary.Find("RGB")!, 1, 2));
        Assert.Equal([1, 50], rig.Show.Patch.FindConflicts().Order());
        Assert.Equal(51, rig.Show.Patch.NextFreeId(21 + 4 > 50 ? 1 : 50));
    }
}

public class UniverseCommandTests
{
    [Fact]
    public void A_universe_is_created_and_configured_independently()
    {
        var rig = new Rig();

        rig.Do("Universe 1 Label \"Stage\" Unicast 10.0.0.5", "Universe 2 Broadcast Off");

        var one = rig.Show.Universes[1];
        Assert.Equal("Stage", one.Label);
        Assert.Equal(UniverseDestination.Unicast, one.Destination);
        Assert.Equal("10.0.0.5", one.UnicastAddress);
        Assert.True(one.Enabled);

        var two = rig.Show.Universes[2];
        Assert.False(two.Enabled);
        Assert.Equal(UniverseDestination.Broadcast, two.Destination);
    }

    [Fact]
    public void Bad_universe_input_is_refused()
    {
        var rig = new Rig();

        Assert.Contains("IPv4", rig.Fail("Universe 1 Unicast 300.1.1.1").Message);
        Assert.Contains("1..", rig.Fail("Universe 99999").Message);
        Assert.Empty(rig.Show.Universes);
    }

    [Fact]
    public void A_universe_with_fixtures_cannot_be_deleted()
    {
        var rig = Rig.WithPatch();
        rig.Do("Universe 1", "Universe 5");

        Assert.Contains("12 patched", rig.Fail("Delete Universe 1").Message);
        rig.Do("Delete Universe 5");
        Assert.DoesNotContain(5, rig.Show.Universes.Keys);
    }

    [Fact]
    public void A_universe_can_be_relabelled()
    {
        var rig = new Rig();
        rig.Do("Universe 3");

        rig.Do("Label Universe 3 \"FOH\"");

        Assert.Equal("FOH", rig.Show.Universes[3].Label);
        Assert.Contains("does not exist", rig.Fail("Label Universe 4 \"x\"").Message);
    }

    [Fact]
    public void Empty_universes_are_rendered_and_routes_follow_the_settings()
    {
        var rig = new Rig();
        rig.Do("Universe 1", "Universe 2 Unicast 10.0.0.9", "Universe 3 Off", "Universe 4 Unicast 10.0.0.9");
        rig.Show.Universes[4].UnicastAddress = "not an address";

        var frame = FrameRenderer.Render(rig.Show, rig.Programmer);

        Assert.Equal([1, 2, 3, 4], frame.Universes.Keys);
        Assert.True(frame.RouteOf(1).Enabled);
        Assert.Null(frame.RouteOf(1).Unicast);
        Assert.Equal(IPAddress.Parse("10.0.0.9"), frame.RouteOf(2).Unicast);
        Assert.False(frame.RouteOf(3).Enabled);
        Assert.False(frame.RouteOf(4).Enabled); // a unicast universe without a usable address sends nothing
    }

    [Fact]
    public void The_engine_sends_each_universe_to_its_own_node_and_skips_disabled_ones()
    {
        var console = new LightingConsole();
        console.Execute("Universe 1");
        console.Execute("Universe 2 Unicast 10.0.0.9");
        console.Execute("Universe 3 Off");
        var output = new RoutedRecorder();

        using var engine = new DmxEngine(console) { Output = output };
        engine.Tick();

        Assert.Equal([(1, (IPAddress?)null), (2, IPAddress.Parse("10.0.0.9"))], output.Sent);
    }

    [Fact]
    public void One_failing_universe_does_not_keep_back_the_others()
    {
        var console = new LightingConsole();
        console.Execute("Universe 1");
        console.Execute("Universe 2");
        var output = new RoutedRecorder { FailOn = 1 };

        using var engine = new DmxEngine(console) { Output = output };
        engine.Tick();

        Assert.Equal([(2, (IPAddress?)null)], output.Sent);
        Assert.Contains("Universe 1", engine.LastError);
    }

    private sealed class RoutedRecorder : IRoutedDmxOutput
    {
        public List<(int Universe, IPAddress? Target)> Sent { get; } = [];

        public int? FailOn { get; init; }

        public void Send(int universe, ReadOnlySpan<byte> data) => Send(universe, data, null);

        public void Send(int universe, ReadOnlySpan<byte> data, IPAddress? target)
        {
            if (universe == FailOn)
                throw new InvalidOperationException("node unreachable");

            Sent.Add((universe, target));
        }
    }
}

public class ProfileTests
{
    private static FixtureType Profile(string name, params ChannelDefinition[] channels) => new(name, channels);

    [Fact]
    public void A_good_profile_is_valid_and_keeps_unused_channels_in_its_footprint()
    {
        var type = new FixtureType("Par", [Ch(FixtureAttribute.Dimmer, 1), Ch(FixtureAttribute.Strobe, 3)], channelCount: 6);

        Assert.Empty(type.Validate());
        Assert.Equal(6, type.Footprint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_profile_needs_a_name(string name)
    {
        Assert.NotEmpty(Profile(name, Ch(FixtureAttribute.Dimmer, 1)).Validate());
    }

    [Fact]
    public void Channels_attributes_and_ranges_are_checked()
    {
        Assert.Contains(Profile("x", Ch(FixtureAttribute.Dimmer, 1), Ch(FixtureAttribute.Dimmer, 2)).Validate(), p => p.Contains("twice"));
        Assert.Contains(Profile("x", Ch(FixtureAttribute.Dimmer, 1), Ch(FixtureAttribute.Red, 1)).Validate(), p => p.Contains("Channel 1"));
        Assert.Contains(Profile("x", Ch(FixtureAttribute.Pan, 1, FineOffset: 1)).Validate(), p => p.Contains("fine"));
        Assert.Contains(Profile("x", Ch(FixtureAttribute.Pan, 1, FineOffset: 2), Ch(FixtureAttribute.Tilt, 2)).Validate(), p => p.Contains("Channel 2"));
        Assert.Contains(Profile("x", Ch(FixtureAttribute.Dimmer, 1, Default: 1.5)).Validate(), p => p.Contains("default"));
        Assert.NotEmpty(Profile("x").Validate());
    }

    [Fact]
    public void Function_ranges_must_be_inside_0_to_255_and_not_overlap()
    {
        ChannelDefinition Gobo(params ChannelFunction[] f) => Ch(FixtureAttribute.Gobo, 1, Functions: f);

        Assert.Empty(Profile("x", Gobo(Fn("A", 0, 9), Fn("B", 10, 19))).Validate());
        Assert.Contains(Profile("x", Gobo(Fn("A", 0, 10), Fn("B", 10, 19))).Validate(), p => p.Contains("overlap"));
        Assert.Contains(Profile("x", Gobo(Fn("A", 0, 300))).Validate(), p => p.Contains("0..255"));
        Assert.Contains(Profile("x", Gobo(Fn("A", 20, 10))).Validate(), p => p.Contains("From <= To"));
        Assert.Contains(Profile("x", Gobo(Fn("", 0, 9))).Validate(), p => p.Contains("name"));
        Assert.Contains(Profile("x", Gobo(Fn("A", 0, 9), Fn("a", 20, 30))).Validate(), p => p.Contains("unique"));
    }

    [Fact]
    public void The_function_at_a_value_is_found()
    {
        var gobo = FixtureTypeLibrary.Find("Spot Mover")!.Find(FixtureAttribute.Gobo)!;

        Assert.Equal("Gobo 1", gobo.FunctionAt(10 / 255.0)!.Name);
        Assert.Null(gobo.FunctionAt(55 / 255.0));
    }

    [Fact]
    public void Built_in_profiles_are_valid()
    {
        Assert.All(FixtureTypeLibrary.All, type =>
        {
            Assert.Empty(type.Validate());
            Assert.True(type.IsBuiltIn);
        });
    }

    [Fact]
    public void A_saved_profile_can_be_patched_and_rendered()
    {
        var console = new LightingConsole();
        var type = new FixtureType("My Par", [Ch(FixtureAttribute.Dimmer, 2), Ch(FixtureAttribute.Red, 3, Default: 1)], channelCount: 4);

        Assert.True(console.SaveProfile(null, type).Success);
        Assert.True(console.Execute("Patch Fixture 1 Type \"My Par\" At 1.10").Success);
        console.Execute("Fixture 1 At 100");

        var frame = console.Render();
        Assert.Equal(new byte[] { 0, 255, 255, 0 }, frame.Universes[1][9..13]);
    }

    [Fact]
    public void Saving_a_profile_is_refused_when_invalid_or_when_the_name_is_taken()
    {
        var console = new LightingConsole();

        Assert.False(console.SaveProfile(null, Profile("RGB", Ch(FixtureAttribute.Dimmer, 1))).Success);
        Assert.False(console.SaveProfile(null, Profile("", Ch(FixtureAttribute.Dimmer, 1))).Success);
        Assert.True(console.SaveProfile(null, Profile("A", Ch(FixtureAttribute.Dimmer, 1))).Success);
        Assert.False(console.SaveProfile(null, Profile("a", Ch(FixtureAttribute.Dimmer, 1))).Success);
        Assert.False(console.SaveProfile("Missing", Profile("B", Ch(FixtureAttribute.Dimmer, 1))).Success);
        Assert.Equal(1, console.Read((show, _) => show.CustomTypes.Count));
    }

    [Fact]
    public void Editing_a_profile_updates_the_fixtures_that_use_it()
    {
        var console = new LightingConsole();
        console.SaveProfile(null, Profile("Par", Ch(FixtureAttribute.Dimmer, 1)));
        console.Execute("Patch Fixture 1 Type Par At 1.1");
        console.Execute("Patch Fixture 2 Type Par At 1.11");
        console.Execute("Patch Fixture 3 Type Par At 1.21");

        var result = console.SaveProfile("Par", Profile("Par 2", Ch(FixtureAttribute.Dimmer, 1), Ch(FixtureAttribute.Red, 2)));

        Assert.True(result.Success);
        Assert.Contains("3 fixtures", result.Message);
        console.Read((show, _) =>
        {
            Assert.Equal(["Par 2"], show.CustomTypes.Keys);
            Assert.All(show.Patch.Fixtures, f => Assert.Equal("Par 2", f.Type.Name));
            Assert.Equal("Par 2 1", show.Patch.Find(1)!.Label);
            return 0;
        });
    }

    [Fact]
    public void A_profile_that_no_longer_fits_the_universe_is_refused_and_nothing_changes()
    {
        var console = new LightingConsole();
        console.SaveProfile(null, Profile("Par", Ch(FixtureAttribute.Dimmer, 1)));
        console.Execute("Patch Fixture 1 Type Par At 1.512");

        var result = console.SaveProfile("Par", Profile("Par", Ch(FixtureAttribute.Dimmer, 1), Ch(FixtureAttribute.Red, 2)));

        Assert.False(result.Success);
        Assert.Contains("no longer fit", result.Message);
        Assert.Equal(1, console.Read((show, _) => show.Patch.Find(1)!.Type.Footprint));
    }

    [Fact]
    public void A_profile_in_use_cannot_be_deleted_and_a_built_in_one_never_can()
    {
        var console = new LightingConsole();
        console.SaveProfile(null, Profile("Par", Ch(FixtureAttribute.Dimmer, 1)));
        console.Execute("Patch Fixture 1 Type Par At 1.1");

        Assert.Contains("used by 1", console.DeleteProfile("Par").Message);
        Assert.Contains("built-in", console.DeleteProfile("RGB").Message);

        console.Execute("Delete Fixture 1");
        Assert.True(console.DeleteProfile("Par").Success);
    }

    [Fact]
    public void Profile_edits_can_be_taken_back_with_oops()
    {
        var console = new LightingConsole();
        console.SaveProfile(null, Profile("Par", Ch(FixtureAttribute.Dimmer, 1)));

        var result = console.Execute("Oops");

        Assert.True(result.Success);
        Assert.Equal(0, console.Read((show, _) => show.CustomTypes.Count));
    }

    [Fact]
    public void Profiles_are_saved_with_the_show_and_come_back_with_functions()
    {
        var console = new LightingConsole();
        console.SaveProfile(null, new FixtureType("Wash", [
            Ch(FixtureAttribute.Dimmer, 1),
            Ch(FixtureAttribute.Gobo, 3, Default: 0.1, Functions: [Fn("Open", 0, 9), Fn("Dots", 10, 19)]),
            Ch(FixtureAttribute.Pan, 4, FineOffset: 5, Default: 0.5)], "Acme", channelCount: 8));
        console.Execute("Patch Fixture 1 Type Wash At 2.1");
        console.Execute("Universe 2 Label \"Dimmer rack\" Unicast 10.1.2.3");

        var json = console.ExportShow();
        var reloaded = new LightingConsole();
        reloaded.ImportShow(json);

        Assert.Equal(json, reloaded.ExportShow());
        reloaded.Read((show, _) =>
        {
            var wash = show.CustomTypes["Wash"];
            Assert.Equal("Acme", wash.Manufacturer);
            Assert.Equal(8, wash.Footprint);
            Assert.Equal(["Open", "Dots"], wash.Find(FixtureAttribute.Gobo)!.FunctionList.Select(f => f.Name));
            Assert.Same(wash, show.Patch.Find(1)!.Type);
            Assert.Equal("10.1.2.3", show.Universes[2].UnicastAddress);
            Assert.Equal("Dimmer rack", show.Universes[2].Label);
            return 0;
        });
    }

    [Fact]
    public void A_version_1_show_file_still_opens_and_gets_its_universes()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Type RGB At 3.1");
        var v1 = System.Text.Json.Nodes.JsonNode.Parse(console.ExportShow())!.AsObject();
        v1["Version"] = 1;
        v1.Remove("Universes");
        v1.Remove("Profiles");

        var loaded = ShowFile.Deserialize(v1.ToJsonString());

        Assert.Equal([3], loaded.Universes.Keys);
        Assert.Equal("RGB", loaded.Patch.Find(1)!.Type.Name);
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("unicast")]
    [InlineData("universe")]
    [InlineData("profile")]
    public void Invalid_universes_and_profiles_in_a_show_are_rejected(string what)
    {
        var console = new LightingConsole();
        console.SaveProfile(null, Profile("Par", Ch(FixtureAttribute.Dimmer, 1)));
        console.Execute("Universe 1");
        var node = System.Text.Json.Nodes.JsonNode.Parse(console.ExportShow())!.AsObject();
        var universe = node["Universes"]![0]!.AsObject();
        var profile = node["Profiles"]![0]!.AsObject();
        switch (what)
        {
            case "destination": universe["Destination"] = "Carrier pigeon"; break;
            case "unicast": universe["Destination"] = "Unicast"; universe["UnicastAddress"] = "nope"; break;
            case "universe": universe["Number"] = 0; break;
            case "profile": profile["Channels"]![0]!["Offset"] = 900; break;
        }

        Assert.Throws<ShowFileException>(() => ShowFile.Deserialize(node.ToJsonString()));
    }

    [Fact]
    public void A_profile_file_round_trips_and_rejects_garbage()
    {
        var type = new FixtureType("Wash", [Ch(FixtureAttribute.Dimmer, 1, Functions: [Fn("All", 0, 255)])], "Acme", 4);

        var loaded = ProfileFile.Deserialize(ProfileFile.Serialize(type));

        Assert.Equal("Wash", loaded.Name);
        Assert.Equal(4, loaded.Footprint);
        Assert.Equal("All", loaded.Channels[0].FunctionList[0].Name);
        Assert.Throws<ShowFileException>(() => ProfileFile.Deserialize("{}"));
        Assert.Throws<ShowFileException>(() => ProfileFile.Deserialize("nope"));
    }
}

public class CommandTextTests
{
    [Fact]
    public void Semicolons_split_commands_but_not_inside_quotes()
    {
        Assert.Equal(["Group 1", "Color Red", "Label Group 1 \"A;B\""], CommandText.Split("Group 1; Color Red ;; Label Group 1 \"A;B\" "));
    }

    [Fact]
    public void Many_commands_that_would_fail_run_none_of_them()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Type RGB At 1.1");

        var results = console.ExecuteMany("Fixture 1; Color Chartreuse; Color Red");

        var only = Assert.Single(results);
        Assert.False(only.Result.Success);
        Assert.Contains("Step 2 of 3", only.Result.Message);
        Assert.Empty(console.Read((_, programmer) => programmer.Selection));
    }
}

internal static class ProfileHelpers
{
    public static ChannelDefinition Ch(FixtureAttribute attribute, int offset, int? FineOffset = null, double Default = 0,
        IReadOnlyList<ChannelFunction>? Functions = null) => new(attribute, offset, FineOffset, Default, Functions);

    public static ChannelFunction Fn(string name, int from, int to) => new(name, from, to);
}
