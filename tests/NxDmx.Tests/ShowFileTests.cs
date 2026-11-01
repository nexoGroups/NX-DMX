using System.Text.Json;
using System.Text.Json.Nodes;
using NxDmx.Core;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Storage;

namespace NxDmx.Tests;

public class ShowFileTests
{
    private static Rig ProgrammedRig()
    {
        var rig = Rig.WithPatch();
        rig.Show.Patch.Add(new Fixture(9, FixtureTypeLibrary.Find("Mover")!, 2, 33, "Spot \"L\" ü"));
        rig.Do(
            "Fixture 1 Thru 4 At 100", "Red At 100", "Green At 55",
            "Record Cue 1 \"Warm Wash\"", "Record Group 1 \"Front\"",
            "Fixture 9 At 100", "Pan At 30.5", "Tilt At 70",
            "Record Cue 1.5 \"Spot\"",
            "Fixture 21 Thru 22 At 40", "Record Cue 2 Cuelist 2",
            "Label Cuelist 2 \"Dimmers\"");
        return rig;
    }

    private static string Edit(string json, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    [Fact]
    public void A_show_survives_a_round_trip_unchanged()
    {
        var rig = ProgrammedRig();

        var json = ShowFile.Serialize(rig.Show);
        var loaded = ShowFile.Deserialize(json);

        Assert.Equal(json, ShowFile.Serialize(loaded));
        Assert.Equal("Spot \"L\" ü", loaded.Patch.Find(9)!.Label);
        Assert.Equal(2, loaded.Patch.Find(9)!.Universe);
        Assert.Equal("Front", loaded.Groups[1].Label);
        Assert.Equal("Dimmers", loaded.Cuelists[2].Label);
        Assert.Equal(1.5m, loaded.Cuelists[1].Cues.Skip(1).Single().Number);
    }

    [Fact]
    public void A_loaded_show_produces_the_same_output()
    {
        var rig = ProgrammedRig();
        var loaded = ShowFile.Deserialize(ShowFile.Serialize(rig.Show));

        rig.Programmer.ClearValues();
        rig.Programmer.ClearSelection();
        rig.Do("Go Cuelist 1", "Go Cuelist 1", "Go Cuelist 2");
        var expected = Core_Render(rig.Show, rig.Programmer);

        var programmer = new NxDmx.Core.Programming.Programmer();
        var interpreter = new NxDmx.Core.Commands.CommandInterpreter(loaded, programmer);
        foreach (var line in new[] { "Go Cuelist 1", "Go Cuelist 1", "Go Cuelist 2" })
            Assert.True(interpreter.Execute(line).Success);

        var actual = Core_Render(loaded, programmer);
        Assert.Equal(expected.Keys, actual.Keys);
        Assert.Equal(expected[1], actual[1]);
        Assert.Equal(expected[2], actual[2]);
    }

    private static Dictionary<int, byte[]> Core_Render(Show show, NxDmx.Core.Programming.Programmer programmer) =>
        NxDmx.Core.Engine.FrameRenderer.Render(show, programmer).Universes.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Live_playback_position_is_not_saved()
    {
        var rig = ProgrammedRig();
        rig.Do("Go Cuelist 1");

        var loaded = ShowFile.Deserialize(ShowFile.Serialize(rig.Show));

        Assert.All(loaded.Cuelists.Values, l => Assert.False(l.IsActive));
    }

    [Fact]
    public void An_empty_show_round_trips()
    {
        var loaded = ShowFile.Deserialize(ShowFile.Serialize(new Show()));

        Assert.Equal(0, loaded.Patch.Count);
        Assert.Empty(loaded.Groups);
        Assert.Empty(loaded.Cuelists);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"Format\":\"something-else\",\"Version\":1}")]
    public void Garbage_is_rejected(string text)
    {
        Assert.Throws<ShowFileException>(() => ShowFile.Deserialize(text));
    }

    [Fact]
    public void A_newer_file_version_asks_for_an_update()
    {
        var json = Edit(ShowFile.Serialize(new Show()), n => n["Version"] = 99);

        var ex = Assert.Throws<ShowFileException>(() => ShowFile.Deserialize(json));

        Assert.Contains("newer", ex.Message);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("address")]
    [InlineData("overflow")]
    [InlineData("universe")]
    [InlineData("duplicate")]
    [InlineData("attribute")]
    [InlineData("value")]
    public void Invalid_content_is_rejected(string what)
    {
        var json = Edit(ShowFile.Serialize(ProgrammedRig().Show), n =>
        {
            var fixtures = n["Fixtures"]!.AsArray();
            var first = fixtures[0]!.AsObject();
            var value = n["Cuelists"]![0]!["Cues"]![0]!["Values"]![0]!.AsObject();
            switch (what)
            {
                case "type": first["Type"] = "Laser"; break;
                case "address": first["Address"] = 0; break;
                case "overflow": first["Address"] = 512; break;   // RGBDim needs 4 channels
                case "universe": first["Universe"] = 40000; break;
                case "duplicate": fixtures.Add(JsonNode.Parse(first.ToJsonString())); break;
                case "attribute": value["Attribute"] = "Bogus"; break;
                case "value": value["Value"] = 1.5; break;
            }
        });

        Assert.Throws<ShowFileException>(() => ShowFile.Deserialize(json));
    }

    [Fact]
    public void Importing_replaces_the_show_and_empties_programmer_and_history()
    {
        var source = ProgrammedRig();
        var json = ShowFile.Serialize(source.Show);

        var console = new LightingConsole();
        console.Execute("Patch Fixture 50 Type Dimmer At 3.1");
        console.Execute("Fixture 50 At 80");

        console.ImportShow(json);

        Assert.Equal(json, console.ExportShow());
        Assert.Equal(0, console.Read((_, programmer) => programmer.Values.Count + programmer.Selection.Count));
        Assert.False(console.Execute("Oops").Success);
    }

    [Fact]
    public void A_failed_import_changes_nothing()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Thru 2 Type Dimmer At 1.1");
        console.Execute("Fixture 1 At 50");
        var before = console.ExportShow();

        Assert.Throws<ShowFileException>(() => console.ImportShow("{ nope"));

        Assert.Equal(before, console.ExportShow());
        Assert.Equal(1, console.Read((_, programmer) => programmer.Values.Count));
        Assert.True(console.Execute("Oops").Success);
    }

    [Fact]
    public void Output_is_written_with_a_version_and_readable_indentation()
    {
        var json = ShowFile.Serialize(new Show());

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("nx-dmx-show", doc.RootElement.GetProperty("Format").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("Version").GetInt32());
        Assert.Contains('\n', json);
    }
}
