namespace NxDmx.Core;

/// <summary>
/// The pre-built show offered at the start: a small rig that is patched and programmed through the normal commands,
/// so there is something to drive at once. Washes, movers and dimmers, two groups and a cuelist of three looks.
/// </summary>
public static class DemoShow
{
    public static readonly IReadOnlyList<string> Commands =
    [
        "Universe 1 Label \"Stage\"",
        "Patch Fixture 1 Thru 8 Type RGBDim At 1.1",
        "Patch Fixture 9 Thru 12 Type \"Spot Mover\" At 1.33",
        "Patch Fixture 21 Thru 24 Type Dimmer At 1.150",
        "Fixture 1 Thru 8",
        "Record Group 1 \"Wash\"",
        "Fixture 9 Thru 12",
        "Record Group 2 \"Movers\"",
        "Fixture 21 Thru 24",
        "Record Group 3 \"Dimmers\"",
        "Group 1",
        "Color Red",
        "At 100",
        "Record Cue 1 \"Red wash\"",
        "Color Blue",
        "Record Cue 2 \"Blue wash\"",
        "Color \"Warm White\"",
        "Group 3",
        "At 100",
        "Record Cue 3 \"Warm and dimmers\"",
        "Label Cuelist 1 \"Demo\"",
        "ClearAll",
    ];

    /// <summary>
    /// Builds the demo show in the console, replacing what is there. Returns the first command that failed, or null when
    /// everything worked.
    /// </summary>
    public static string? Build(LightingConsole console)
    {
        console.ImportShow(Storage.ShowFile.Serialize(new Show()));
        foreach (var line in Commands)
        {
            var result = console.Execute(line);
            if (!result.Success)
                return $"{line}: {result.Message}";
        }

        console.ClearHistory();
        return null;
    }
}
