using NxDmx.Core.Commands;
using NxDmx.Core.Fixtures;

namespace NxDmx.Tests;

public class CommandTests
{
    [Theory]
    [InlineData("Fixture 1 Thru 5", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("Fixture 1 + 3 + 5 Thru 6", new[] { 1, 3, 5, 6 })]
    [InlineData("Fixture 1 Thru 8 - 3 Thru 4", new[] { 1, 2, 5, 6, 7, 8 })]
    [InlineData("Fixture 21 Thru 24 - 22", new[] { 21, 23, 24 })]
    [InlineData("fix 2 thru 3", new[] { 2, 3 })]
    [InlineData("Fixture 5 Thru 1", new[] { 1, 2, 3, 4, 5 })]
    [InlineData("Fixture 1 Thru 100", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 21, 22, 23, 24 })]
    public void Selection_expressions(string command, int[] expected)
    {
        var rig = Rig.WithPatch();

        rig.Do(command);

        Assert.Equal(expected.Order(), rig.Programmer.Selection.Order());
    }

    [Fact]
    public void A_leading_plus_adds_to_the_selection()
    {
        var rig = Rig.WithPatch();

        rig.Do("Fixture 1", "+ Fixture 8", "- Fixture 1");

        Assert.Equal([8], rig.Programmer.Selection);
    }

    [Fact]
    public void Unpatched_fixture_is_an_error_and_keeps_the_selection()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1");

        var result = rig.Fail("Fixture 99");

        Assert.Contains("99", result.Message);
        Assert.Equal([1], rig.Programmer.Selection);
    }

    [Fact]
    public void At_sets_the_dimmer_of_the_selection_in_percent()
    {
        var rig = Rig.WithPatch();

        var result = rig.Run("Fixture 1 Thru 3 At 50");

        Assert.True(result.Success);
        Assert.Equal("Fixture 1 Thru 3: Dimmer 50 %", result.Message);
        foreach (var id in new[] { 1, 2, 3 })
            Assert.Equal(0.5, rig.Value(id, FixtureAttribute.Dimmer));
        Assert.DoesNotContain(rig.Programmer.Values.Keys, k => k.FixtureId == 4);
    }

    [Theory]
    [InlineData("Fixture 2 Full", 1.0)]
    [InlineData("Fixture 2 At Full", 1.0)]
    [InlineData("Fixture 2 @ 25", 0.25)]
    [InlineData("Fixture 2 At 12,5", 0.125)]
    [InlineData("Fixture 2 AT 0", 0.0)]
    public void Value_forms(string command, double expected)
    {
        var rig = Rig.WithPatch();

        rig.Do(command);

        Assert.Equal(expected, rig.Value(2, FixtureAttribute.Dimmer), 6);
    }

    [Fact]
    public void At_applies_to_the_current_selection_and_a_named_attribute()
    {
        var rig = Rig.WithPatch();

        rig.Do("Fixture 1 Thru 2", "Green At 40");

        Assert.Equal(0.4, rig.Value(1, FixtureAttribute.Green));
        Assert.Equal(0.4, rig.Value(2, FixtureAttribute.Green));
    }

    [Theory]
    [InlineData("At 50", "Nothing selected")]
    [InlineData("Fixture 1 At 101", "between 0 and 100")]
    [InlineData("Fixture 21 Red At 50", "no Red attribute")]
    [InlineData("Fixture 1 At abc", "Value")]
    [InlineData("Fixture 1 Group 2", "'at' expected")]
    [InlineData("Banana", "Unknown command")]
    [InlineData("Fixture 1 $", "Unexpected character")]
    [InlineData("Label Group 1 \"open", "closing quote")]
    public void Errors_are_reported(string command, string messagePart)
    {
        var rig = Rig.WithPatch();

        var result = rig.Fail(command);

        Assert.Contains(messagePart, result.Message);
    }

    [Fact]
    public void A_failed_command_leaves_everything_as_it_was()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 5 At 30");

        rig.Fail("Fixture 1 Thru 3 At abc");

        Assert.Equal([5], rig.Programmer.Selection);
        Assert.Equal(0.3, rig.Value(5, FixtureAttribute.Dimmer));
        Assert.Single(rig.Programmer.Values);
    }

    [Fact]
    public void Clear_steps_back_selection_then_programmer()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 50");

        rig.Do("Clear");
        Assert.Empty(rig.Programmer.Selection);
        Assert.False(rig.Programmer.IsEmpty);

        rig.Do("Clear");
        Assert.True(rig.Programmer.IsEmpty);
    }

    [Fact]
    public void ClearAll_clears_in_one_go()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 50", "ClearAll");

        Assert.Empty(rig.Programmer.Selection);
        Assert.True(rig.Programmer.IsEmpty);
    }

    [Theory]
    [InlineData("Fixture 1 Thru 3 At 50 Please")]
    [InlineData("Fixture 1 Thru 3 At 50 please")]
    [InlineData("  Fixture 1 Thru 3   At 50   PLEASE ")]
    public void Please_ends_a_command_like_on_grandMA2(string command)
    {
        var rig = Rig.WithPatch();

        rig.Do(command);

        Assert.Equal(0.5, rig.Value(3, FixtureAttribute.Dimmer));
    }

    [Fact]
    public void Please_alone_does_nothing()
    {
        var rig = Rig.WithPatch();

        var result = rig.Run("Please");

        Assert.True(result.Success);
        Assert.Empty(result.Message);
    }

    [Fact]
    public void Help_lists_the_commands()
    {
        var result = Rig.WithPatch().Run("Help");

        Assert.True(result.Success);
        Assert.Contains("Record Cue", result.Message);
        Assert.Contains("Oops", result.Message);
    }

    [Fact]
    public void Group_records_and_selects()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 2 Thru 4", "Record Group 1 \"Front\"", "Clear", "Group 1");

        Assert.Equal([2, 3, 4], rig.Programmer.Selection);
        Assert.Equal("Front", rig.Show.Groups[1].Label);

        rig.Do("Group 1 At 80");
        Assert.Equal(0.8, rig.Value(3, FixtureAttribute.Dimmer));
    }

    [Fact]
    public void Group_can_be_combined_with_fixtures()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Thru 2", "Record Group 3", "ClearAll", "Fixture 8 + Group 3");

        Assert.Equal([8, 1, 2], rig.Programmer.Selection);
    }

    [Fact]
    public void Missing_group_is_an_error()
    {
        Assert.Contains("Group 7", Rig.WithPatch().Fail("Group 7").Message);
    }

    [Fact]
    public void Record_needs_programmer_content_or_a_selection()
    {
        var rig = Rig.WithPatch();

        Assert.Contains("empty", rig.Fail("Record Cue 1").Message);
        Assert.Contains("Nothing selected", rig.Fail("Record Group 1").Message);
    }

    [Fact]
    public void Record_stores_programmer_values_and_label()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Thru 2 At 70", "Record Cue 1 \"Intro\"");

        var cue = rig.Show.Cuelists[1].Find(1)!;
        Assert.Equal("Intro", cue.Label);
        Assert.Equal(2, cue.Values.Count);
    }

    [Fact]
    public void Recording_over_an_existing_cue_replaces_it_and_keeps_its_label()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 70", "Record Cue 1 \"Intro\"", "ClearAll", "Fixture 2 At 20");

        var result = rig.Run("Record Cue 1");

        Assert.Contains("Replaced", result.Message);
        var cue = rig.Show.Cuelists[1].Find(1)!;
        Assert.Equal("Intro", cue.Label);
        Assert.Single(cue.Values);
        Assert.Equal(2, cue.Values.Keys.Single().FixtureId);
    }

    [Fact]
    public void Update_merges_the_programmer_into_the_cue()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1", "ClearAll", "Fixture 2 At 50", "Update Cue 1");

        var cue = rig.Show.Cuelists[1].Find(1)!;
        Assert.Equal(2, cue.Values.Count);
        Assert.Contains("does not exist", rig.Fail("Update Cue 9").Message);
    }

    [Fact]
    public void Delete_removes_cues_groups_cuelists_and_fixtures()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Thru 3 At 100", "Record Cue 1", "Record Cue 2", "Record Group 1");

        rig.Do("Delete Cue 2");
        Assert.Null(rig.Show.Cuelists[1].Find(2));

        rig.Do("Delete Fixture 3");
        Assert.Null(rig.Show.Patch.Find(3));
        Assert.Equal([1, 2], rig.Show.Groups[1].FixtureIds);
        Assert.DoesNotContain(3, rig.Programmer.Selection);
        Assert.DoesNotContain(rig.Programmer.Values.Keys, k => k.FixtureId == 3);

        rig.Do("Delete Group 1", "Delete Cuelist 1");
        Assert.Empty(rig.Show.Groups);
        Assert.Empty(rig.Show.Cuelists);
        Assert.Contains("does not exist", rig.Fail("Delete Group 1").Message);
    }

    [Fact]
    public void Label_names_things()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1", "Record Group 2");

        rig.Do("Label Cue 1 \"Opening\"", "Label Group 2 \"Front\"", "Label Fixture 1 \"Spot L\"", "Label Cuelist 1 \"Main\"");

        Assert.Equal("Opening", rig.Show.Cuelists[1].Find(1)!.Label);
        Assert.Equal("Front", rig.Show.Groups[2].Label);
        Assert.Equal("Spot L", rig.Show.Patch.Find(1)!.Label);
        Assert.Equal("Main", rig.Show.Cuelists[1].Label);
        Assert.Contains("quotes", rig.Fail("Label Group 2").Message);
    }

    [Fact]
    public void Go_walks_forward_in_cue_number_order_including_decimals()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 2", "Record Cue 1.5", "Record Cue 1");
        var cuelist = rig.Show.Cuelists[1];

        rig.Do("Go");
        Assert.Equal(1m, cuelist.CurrentNumber);
        rig.Do("Go");
        Assert.Equal(1.5m, cuelist.CurrentNumber);
        rig.Do("Go");
        Assert.Equal(2m, cuelist.CurrentNumber);
        Assert.Contains("last cue", rig.Fail("Go").Message);

        rig.Do("Go -");
        Assert.Equal(1.5m, cuelist.CurrentNumber);
        rig.Do("Back");
        Assert.Equal(1m, cuelist.CurrentNumber);
        Assert.Contains("first cue", rig.Fail("Back").Message);

        rig.Do("Goto Cue 2");
        Assert.Equal(2m, cuelist.CurrentNumber);
        rig.Do("Off");
        Assert.False(cuelist.IsActive);
        Assert.Contains("is off", rig.Fail("Back").Message);
    }

    [Fact]
    public void Playback_commands_name_the_cuelist()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1 Cuelist 2");

        rig.Do("Go Cuelist 2");

        Assert.Equal(1m, rig.Show.Cuelists[2].CurrentNumber);
        Assert.Contains("Cuelist 1 does not exist", rig.Fail("Go").Message);
        Assert.Contains("Cue 4", rig.Fail("Goto Cue 4 Cuelist 2").Message);
    }

    [Fact]
    public void Patch_places_fixtures_one_after_another()
    {
        var rig = new Rig();

        var result = rig.Run("Patch Fixture 1 Thru 3 Type RGBDim At 2.10");

        Assert.True(result.Success);
        Assert.Equal((2, 10), (rig.Show.Patch.Find(1)!.Universe, rig.Show.Patch.Find(1)!.Address));
        Assert.Equal((2, 14), (rig.Show.Patch.Find(2)!.Universe, rig.Show.Patch.Find(2)!.Address));
        Assert.Equal((2, 18), (rig.Show.Patch.Find(3)!.Universe, rig.Show.Patch.Find(3)!.Address));
    }

    [Fact]
    public void Patch_continues_in_the_next_universe_when_a_fixture_does_not_fit()
    {
        var rig = new Rig();

        rig.Do("Patch Fixture 1 Thru 200 Type RGB At 1.1");

        Assert.Equal((1, 508), (rig.Show.Patch.Find(170)!.Universe, rig.Show.Patch.Find(170)!.Address));
        Assert.Equal((2, 1), (rig.Show.Patch.Find(171)!.Universe, rig.Show.Patch.Find(171)!.Address));
    }

    [Fact]
    public void Patch_accepts_absolute_addresses()
    {
        var rig = new Rig();

        rig.Do("Patch Fixture 1 Type Dimmer At 513");

        Assert.Equal((2, 1), (rig.Show.Patch.Find(1)!.Universe, rig.Show.Patch.Find(1)!.Address));
    }

    [Theory]
    [InlineData("Patch Fixture 1 Type Dimmer At 1.513")]
    [InlineData("Patch Fixture 1 Type Dimmer At 0.5")]
    [InlineData("Patch Fixture 1 Type Dimmer At 1.0")]
    [InlineData("Patch Fixture 1 Type Laser At 1.1")]
    [InlineData("Patch Fixture 1 Thru 9999 Type Dimmer At 1.1")]
    [InlineData("Patch Fixture 1 Type Dimmer")]
    public void Bad_patches_are_rejected_without_changing_the_patch(string command)
    {
        var rig = new Rig();

        rig.Fail(command);

        Assert.Equal(0, rig.Show.Patch.Count);
    }

    [Fact]
    public void Unknown_fixture_type_lists_the_available_ones()
    {
        var message = new Rig().Fail("Patch Fixture 1 Type Laser At 1.1").Message;

        Assert.Contains("laser", message);
        Assert.Contains("RGBDim", message);
    }

    [Fact]
    public void Repatching_a_fixture_number_replaces_it()
    {
        var rig = Rig.WithPatch();

        rig.Do("Patch Fixture 1 Type Mover At 3.1");

        Assert.Equal("Mover", rig.Show.Patch.Find(1)!.Type.Name);
        Assert.Equal(12, rig.Show.Patch.Count);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5, 8 }, "1 Thru 5 + 8")]
    [InlineData(new[] { 1, 2 }, "1 + 2")]
    [InlineData(new[] { 5, 3, 4 }, "3 Thru 5")]
    [InlineData(new[] { 7 }, "7")]
    [InlineData(new int[0], "")]
    [InlineData(new[] { 1, 3, 5 }, "1 + 3 + 5")]
    public void Selections_are_formatted_as_typed(int[] ids, string expected)
    {
        Assert.Equal(expected, SelectionFormatter.Format(ids));
    }
}
