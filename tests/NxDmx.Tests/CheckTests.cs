using System.Text.Json;
using NxDmx.Core;
using NxDmx.Core.Commands;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Storage;

namespace NxDmx.Tests;

/// <summary>The check every operation goes through: refused when a rule forbids it, and then nothing has changed.</summary>
public class CheckTests
{
    [Fact]
    public void A_running_cuelist_cannot_be_deleted()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1", "Go");

        var result = rig.Run("Delete Cuelist 1");

        Assert.False(result.Success);
        Assert.True(result.Refused);
        Assert.StartsWith("Not allowed: ", result.Message);
        Assert.Contains("running", result.Message);
        Assert.True(rig.Show.Cuelists.ContainsKey(1));

        rig.Do("Off", "Delete Cuelist 1");
        Assert.Empty(rig.Show.Cuelists);
    }

    [Fact]
    public void The_cue_that_is_playing_cannot_be_deleted_but_another_one_can()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1", "Record Cue 2", "Go");

        var refused = rig.Run("Delete Cue 1");
        Assert.True(refused.Refused);
        Assert.NotNull(rig.Show.Cuelists[1].Find(1));

        rig.Do("Delete Cue 2");
        Assert.Null(rig.Show.Cuelists[1].Find(2));
    }

    [Fact]
    public void A_group_or_cuelist_on_a_fader_cannot_be_deleted()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1", "Record Group 1", "Fader 1 Group 1", "Fader 2 Cuelist 1");

        var group = rig.Run("Delete Group 1");
        var cuelist = rig.Run("Delete Cuelist 1");

        Assert.True(group.Refused);
        Assert.Contains("Fader 1", group.Message);
        Assert.True(cuelist.Refused);
        Assert.Contains("Fader 2", cuelist.Message);
        Assert.True(rig.Show.Groups.ContainsKey(1));
        Assert.True(rig.Show.Cuelists.ContainsKey(1));

        rig.Do("Fader 1 Clear", "Fader 2 Clear", "Delete Group 1", "Delete Cuelist 1");
    }

    [Fact]
    public void A_universe_with_fixtures_is_refused_with_the_reason()
    {
        var rig = Rig.WithPatch();
        rig.Do("Universe 1");

        var result = rig.Run("Delete Universe 1");

        Assert.True(result.Refused);
        Assert.Contains("patched fixture", result.Message);
    }

    [Fact]
    public void A_typing_mistake_is_an_error_not_a_refusal()
    {
        var rig = Rig.WithPatch();

        var result = rig.Run("Fixture 99");

        Assert.False(result.Success);
        Assert.False(result.Refused);
    }

    [Fact]
    public void Deleting_fixtures_that_have_cue_values_says_what_stays_behind()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Thru 2 At 100", "Record Cue 1");

        var result = rig.Run("Delete Fixture 1");

        Assert.True(result.Success);
        Assert.Contains("Note: 1 recorded cue value", result.Message);
    }

    [Fact]
    public void Overlapping_fixtures_are_refused_and_the_patch_stays_as_it_was()
    {
        var rig = Rig.WithPatch();
        var before = ShowFile.Serialize(rig.Show);

        var result = rig.Run("Patch Fixture 30 Type RGBDim At 1.3");

        Assert.False(result.Success);
        Assert.Equal(before, ShowFile.Serialize(rig.Show));
    }

    [Fact]
    public void The_validator_names_what_is_wrong_in_a_show()
    {
        var show = new Show();
        var rgb = FixtureTypeLibrary.Find("RGBDim")!;
        show.Patch.Add(new Fixture(1, rgb, 1, 1));
        show.Patch.Add(new Fixture(2, rgb, 1, 3));
        show.Faders[1] = new NxDmx.Core.Playback.FaderConfig(1) { Kind = NxDmx.Core.Playback.FaderKind.Cuelist, Target = 7 };

        var issues = ShowValidator.Check(show);

        Assert.Contains(issues, i => i.Key == "overlap:1");
        Assert.Contains(issues, i => i.Key == "overlap:2");
        Assert.Contains(issues, i => i.Key == "fader:1" && i.Message.Contains("Cuelist 7"));
    }

    [Fact]
    public void A_series_that_would_fail_half_way_changes_nothing()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Thru 2 Type RGBDim At 1.1");
        var before = console.ExportShow();

        var results = console.ExecuteMany("Fixture 1 At 100; Record Group 1; Delete Cuelist 9");

        var only = Assert.Single(results);
        Assert.False(only.Result.Success);
        Assert.Contains("Step 3 of 3", only.Result.Message);
        Assert.Contains("Nothing was changed", only.Result.Message);
        Assert.Equal(before, console.ExportShow());
    }

    [Fact]
    public void A_series_that_works_runs_completely()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Thru 2 Type RGBDim At 1.1");

        var results = console.ExecuteMany("Fixture 1 Thru 2; Record Group 1; Fixture 1 At 100; Record Cue 1");

        Assert.All(results, r => Assert.True(r.Result.Success, r.Result.Message));
        Assert.Equal(4, results.Count);
    }

    [Fact]
    public void The_check_sees_the_programmer_of_the_console()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 1 Type RGBDim At 1.1");
        console.Execute("Fixture 1 At 50");

        // Record Cue needs values in the programmer; the copy used for the check has to have them too.
        var check = console.Preflight(["Record Cue 1", "Record Group 1"]);

        Assert.True(check.Success, check.Message);
    }

    [Fact]
    public void Live_commands_are_recognised()
    {
        Assert.True(CommandInterpreter.IsLive("Go"));
        Assert.True(CommandInterpreter.IsLive("go please"));
        Assert.True(CommandInterpreter.IsLive("Fader 3 At 50"));
        Assert.True(CommandInterpreter.IsLive("Oops"));
        Assert.False(CommandInterpreter.IsLive("Record Cue 1"));
        Assert.False(CommandInterpreter.IsLive("Fader 3 Cuelist 1"));
    }
}

public class DemoShowTests
{
    [Fact]
    public void The_pre_built_show_builds_without_a_single_failure()
    {
        var console = new LightingConsole();

        var failure = DemoShow.Build(console);

        Assert.Null(failure);
        var snapshot = console.Read((show, _) => (Fixtures: show.Patch.Count, Groups: show.Groups.Count, Cues: show.Cuelists[1].Cues.Count()));
        Assert.Equal((16, 3, 3), snapshot);
        Assert.Equal("Nothing to undo", console.Execute("Oops").Message);
    }

    [Fact]
    public void The_pre_built_show_replaces_what_was_there()
    {
        var console = new LightingConsole();
        console.Execute("Patch Fixture 100 Type Dimmer At 3.1");

        DemoShow.Build(console);

        Assert.False(console.Read((show, _) => show.Patch.Find(100) is not null));
    }

    [Fact]
    public void The_pre_built_show_passes_the_check_of_the_rules()
    {
        var console = new LightingConsole();
        DemoShow.Build(console);

        Assert.Empty(console.Read((show, programmer) => ShowValidator.Check(show, programmer)));
    }
}

public class ProgressTests
{
    private sealed class Recorder : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value) => Values.Add(value);
    }

    private static LightingConsole Console()
    {
        var console = new LightingConsole();
        DemoShow.Build(console);
        return console;
    }

    [Fact]
    public void Writing_reports_rising_progress_that_ends_at_one()
    {
        var recorder = new Recorder();

        Console().ExportShow(recorder);

        Assert.True(recorder.Values.Count > 3);
        Assert.Equal(recorder.Values.Order(), recorder.Values);
        Assert.Equal(1, recorder.Values[^1]);
        Assert.All(recorder.Values, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void Reading_reports_rising_progress_that_ends_at_one()
    {
        var json = Console().ExportShow();
        var recorder = new Recorder();

        ShowFile.Deserialize(json, recorder);

        Assert.True(recorder.Values.Count > 3);
        Assert.Equal(recorder.Values.Order(), recorder.Values);
        Assert.Equal(1, recorder.Values[^1]);
    }

    [Fact]
    public void Writing_with_progress_gives_the_same_text_as_a_serializer_would()
    {
        var json = Console().ExportShow();

        // The same document written by the standard serializer from the parsed text: same layout, same order.
        using var document = JsonDocument.Parse(json);
        var again = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });

        Assert.Equal(again, json);
    }

    [Fact]
    public void A_show_survives_writing_and_reading()
    {
        var console = Console();
        var json = console.ExportShow();

        var other = new LightingConsole();
        other.ImportShow(json, new Recorder());

        Assert.Equal(json, other.ExportShow());
    }

    [Fact]
    public void A_scaled_progress_maps_into_its_share()
    {
        var recorder = new Recorder();
        var scaled = new ScaledProgress(recorder, 0.2, 0.6);

        scaled.Report(0);
        scaled.Report(0.5);
        scaled.Report(1);

        Assert.Equal([0.2, 0.4, 0.6], recorder.Values.Select(v => Math.Round(v, 10)));
    }

    [Fact]
    public void The_meter_reports_only_what_is_done()
    {
        var recorder = new Recorder();
        var meter = new ProgressMeter(recorder, 200);

        meter.Add(100);

        Assert.Equal(0.5, recorder.Values[^1]);
    }
}

public class ProfileCheckTests
{
    [Fact]
    public void A_profile_edit_that_makes_fixtures_overlap_is_refused_and_changes_nothing()
    {
        var console = new LightingConsole();
        console.SaveProfile(null, new FixtureType("Par", [ProfileHelpers.Ch(FixtureAttribute.Dimmer, 1)]));
        console.Execute("Patch Fixture 1 Thru 3 Type Par At 1.1");
        var before = console.ExportShow();

        var result = console.SaveProfile("Par", new FixtureType("Par", [ProfileHelpers.Ch(FixtureAttribute.Dimmer, 1), ProfileHelpers.Ch(FixtureAttribute.Red, 2)]));

        Assert.True(result.Refused);
        Assert.Contains("shares DMX channels", result.Message);
        Assert.Equal(before, console.ExportShow());
    }
}
