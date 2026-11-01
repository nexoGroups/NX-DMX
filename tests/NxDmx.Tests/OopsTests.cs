using NxDmx.Core.Fixtures;

namespace NxDmx.Tests;

public class OopsTests
{
    [Fact]
    public void Oops_undoes_selection_and_value_of_the_last_command()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 50");

        var result = rig.Run("Oops");

        Assert.True(result.Success);
        Assert.Equal("Oops: undid \"Fixture 1 At 50\"", result.Message);
        Assert.Empty(rig.Programmer.Selection);
        Assert.True(rig.Programmer.IsEmpty);
    }

    [Fact]
    public void Oops_can_be_repeated_to_walk_back_further()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 50", "Fixture 2 At 70", "Fixture 3 At 90");

        rig.Do("Oops");
        Assert.Equal(2, rig.Programmer.Values.Count);
        Assert.Equal([2], rig.Programmer.Selection);

        rig.Do("Oops");
        Assert.Single(rig.Programmer.Values);
        Assert.Equal(0.5, rig.Value(1, FixtureAttribute.Dimmer));

        rig.Do("Oops");
        Assert.True(rig.Programmer.IsEmpty);

        Assert.Contains("Nothing to undo", rig.Fail("Oops").Message);
    }

    [Fact]
    public void Oops_takes_back_a_record()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 100", "Record Cue 1 \"Intro\"", "Record Group 1");

        rig.Do("Oops");
        Assert.Empty(rig.Show.Groups);
        Assert.NotNull(rig.Show.Cuelists[1].Find(1));

        rig.Do("Oops");
        Assert.Empty(rig.Show.Cuelists);
        Assert.Equal([1], rig.Programmer.Selection);
    }

    [Fact]
    public void Oops_restores_a_deleted_cue_with_its_values()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Thru 2 At 60", "Record Cue 1 \"Intro\"", "Delete Cue 1");
        Assert.Null(rig.Show.Cuelists[1].Find(1));

        rig.Do("Oops");

        var cue = rig.Show.Cuelists[1].Find(1)!;
        Assert.Equal("Intro", cue.Label);
        Assert.Equal(2, cue.Values.Count);
    }

    [Fact]
    public void Oops_restores_a_deleted_fixture_and_its_group_membership()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Thru 3", "Record Group 1", "Delete Fixture 3");
        Assert.Equal([1, 2], rig.Show.Groups[1].FixtureIds);

        rig.Do("Oops");

        Assert.Equal((1, 9), (rig.Show.Patch.Find(3)!.Universe, rig.Show.Patch.Find(3)!.Address));
        Assert.Equal([1, 2, 3], rig.Show.Groups[1].FixtureIds);
    }

    [Fact]
    public void Oops_takes_back_a_patch_and_a_label()
    {
        var rig = new Rig();
        rig.Do("Patch Fixture 1 Type Dimmer At 1.1", "Label Fixture 1 \"Spot\"");

        rig.Do("Oops");
        Assert.Equal("Dimmer 1", rig.Show.Patch.Find(1)!.Label);

        rig.Do("Oops");
        Assert.Equal(0, rig.Show.Patch.Count);
    }

    [Fact]
    public void Oops_does_not_undo_playback_moves()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "ClearAll", "Fixture 1 At 20", "Record Cue 2", "Go", "Go");
        Assert.Equal(2m, rig.Show.Cuelists[1].CurrentNumber);

        // Undoes "Record Cue 2" -- not the two Go commands. The cuelist stays where the operator took it.
        rig.Do("Oops");

        Assert.Null(rig.Show.Cuelists[1].Find(2));
        Assert.Equal(2m, rig.Show.Cuelists[1].CurrentNumber);
    }

    [Fact]
    public void Oops_keeps_the_live_position_of_a_cuelist()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "Record Cue 2", "Go", "Label Cue 1 \"X\"", "Go");

        rig.Do("Oops");

        Assert.Equal("", rig.Show.Cuelists[1].Find(1)!.Label);
        Assert.Equal(2m, rig.Show.Cuelists[1].CurrentNumber);
    }

    [Fact]
    public void Go_and_help_are_not_recorded_for_Oops()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 Full", "Record Cue 1", "Go", "Help");

        rig.Do("Oops");

        Assert.Empty(rig.Show.Cuelists);
    }

    [Fact]
    public void Failed_commands_are_not_recorded_for_Oops()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 50");
        rig.Fail("Fixture 99");
        rig.Fail("Banana");

        rig.Do("Oops");

        Assert.True(rig.Programmer.IsEmpty);
        Assert.Contains("Nothing to undo", rig.Fail("Oops").Message);
    }

    [Fact]
    public void Oops_works_with_please_and_any_case()
    {
        var rig = Rig.WithPatch();
        rig.Do("Fixture 1 At 50");

        rig.Do("OOPS Please");

        Assert.True(rig.Programmer.IsEmpty);
    }

    [Fact]
    public void Only_the_last_hundred_steps_can_be_undone()
    {
        var rig = Rig.WithPatch();
        for (var i = 0; i < 105; i++)
            rig.Do($"Fixture 1 At {i % 100}");

        var undone = 0;
        while (rig.Run("Oops").Success)
            undone++;

        Assert.Equal(100, undone);
    }
}
