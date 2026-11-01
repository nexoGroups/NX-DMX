using System.Globalization;
using NxDmx.Core.ArtNet;
using NxDmx.Core.Engine;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Commands;

/// <summary>
/// Executes console commands in the style of grandMA and ONYX: <c>Fixture 1 Thru 10 At 50</c>,
/// <c>Record Cue 1</c>, <c>Go</c>. Not thread-safe; callers serialise access (see <see cref="LightingConsole"/>).
/// </summary>
public sealed partial class CommandInterpreter(Show show, Programmer programmer, OutputState? output = null)
{
    private const int MaxPatchRange = 4096;
    private const int MaxUndoSteps = 100;

    private readonly OutputState _output = output ?? new OutputState();

    /// <summary>Playback actions, output controls and help are live operations: they are not recorded for Oops.</summary>
    private static readonly HashSet<string> LiveCommands =
        ["go", "back", "goto", "off", "release", "toggle", "blackout", "master", "help"];

    private readonly List<(ShowSnapshot State, string Command)> _undo = [];

    private enum SelectionOp { Replace, Add, Remove }

    private sealed record Target(string Kind, decimal Number, int Cuelist);

    public const string HelpText = """
        Select      Fixture 1 Thru 10 | Fixture 1 + 5 + 7 Thru 9 | Fixture 1 Thru 10 - 5 | Group 2
        Values      At 50 | At Full | Full | Red At 100 | Pan At 25 Tilt At 70   (percent of the selection)
        Function    Gobo Function "Gobo 2" | ColorWheel Function Red   (named range of the profile)
        Color       Color Red | Color "Warm White"   (sets every colour emitter the fixture has)
        Record      Record Cue 1 ["Name"] [Cuelist 2] | Record Group 3 ["Name"]
        Update      Update Cue 1 [Cuelist 2]   (merges the programmer into the cue)
        Delete      Delete Cue 1 [Cuelist 2] | Delete Group 3 | Delete Cuelist 2 | Delete Fixture 1 Thru 5 | Delete Universe 3
        Label       Label Group 3 "Front" | Label Cue 1 "Intro" | Label Fixture 5 "Spot L" | Label Universe 2 "FOH"
        Playback    Go | Go - | Back | Goto Cue 5 | Off | Toggle   (add Cuelist n for another cuelist)
        Output      Blackout [On|Off] | Master At 80
        Patch       Patch Fixture 1 Thru 8 Type RGBDim At 1.1 [Label "Wash"]   (universe.address, or an absolute address)
                    Patch Fixture 5 At 2.1 moves, Patch Fixture 5 Type RGB changes the profile of existing fixtures
        Universe    Universe 2 [Label "FOH"] [Unicast 10.0.0.5 | Broadcast] [On | Off]
        Clear       Clear (deselect, then clear programmer) | ClearAll
        Oops        Undoes the last programming step, repeatable (not Go/Back/Off)
        Please      Optional at the end of a line, like on grandMA2: Fixture 1 At 50 Please
        Profiles    Dimmer, RGB, RGBW, RGBDim, Mover, Spot Mover, and the ones made in the Fixture Manager
        """;

    /// <summary>
    /// Runs one command line. A trailing <c>Please</c> is accepted and ignored, as on grandMA2. A command that
    /// fails leaves the show and programmer untouched; one that succeeds can be taken back with <c>Oops</c>.
    /// </summary>
    public CommandResult Execute(string line)
    {
        ShowSnapshot? before = null;
        try
        {
            var tokenList = Tokenizer.Tokenize(line);
            if (tokenList.Count > 0 && tokenList[^1] is { Kind: TokenKind.Word, Text: "please" })
                tokenList.RemoveAt(tokenList.Count - 1);

            var tokens = new TokenStream(tokenList);
            if (tokens.AtEnd)
                return CommandResult.Ok();

            var head = tokens.Peek is { Kind: TokenKind.Word } word ? word.Text : null;
            if (head == "oops")
            {
                tokens.Next();
                tokens.ExpectEnd();
                return Oops();
            }

            if (head is not null && LiveCommands.Contains(head))
                return Run(tokens);

            before = ShowSnapshot.Capture(show, programmer);
            var result = Run(tokens);

            _undo.Add((before, string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
            if (_undo.Count > MaxUndoSteps)
                _undo.RemoveAt(0);

            return result;
        }
        catch (CommandException ex)
        {
            before?.Restore(show, programmer);
            return CommandResult.Error(ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A bug must never take the console down in the middle of a show: undo what the command did so far and say so.
            before?.Restore(show, programmer);
            return CommandResult.Error($"Internal error, nothing was changed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Forgets all Oops steps, e.g. after loading a show.</summary>
    public void ClearHistory() => _undo.Clear();

    private CommandResult Oops()
    {
        if (_undo.Count == 0)
            throw new CommandException("Nothing to undo");

        var (state, command) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        state.Restore(show, programmer);
        return CommandResult.Ok($"Oops: undid \"{command}\"");
    }

    private CommandResult Run(TokenStream t)
    {
        if (t.Peek is { Kind: TokenKind.Plus or TokenKind.Minus })
            return SelectAndApply(t);

        if (t.Peek is not { Kind: TokenKind.Word })
            throw new CommandException($"Unexpected '{t.Peek!.Value.Text}'");

        var head = t.Peek.Value;
        if (FixtureAttributeExtensions.TryParse(head.Text, out _))
            return ApplyValue(t);

        switch (head.Text)
        {
            case "fixture" or "group":
                return SelectAndApply(t);
            case "at" or "full" or "function":
                return ApplyValue(t);
        }

        t.Next();
        return head.Text switch
        {
            "record" => Record(t),
            "update" => Update(t),
            "delete" => Delete(t),
            "label" => Label(t),
            "go" => Go(t),
            "back" => Back(t),
            "goto" => GoTo(t),
            "off" or "release" => Off(t),
            "toggle" => Toggle(t),
            "blackout" => Blackout(t),
            "master" => Master(t),
            "color" => ApplyColor(t),
            "universe" => UniverseCommand(t),
            "patch" => PatchFixtures(t),
            "clear" => Clear(t, everything: false),
            "clearall" => Clear(t, everything: true),
            "help" => CommandResult.Ok(HelpText),
            _ => throw new CommandException($"Unknown command '{head.Text}'. Type Help for the list."),
        };
    }

    // ---- selection -------------------------------------------------------------------------------------------

    private CommandResult SelectAndApply(TokenStream t)
    {
        ParseSelection(t);
        return t.AtEnd
            ? CommandResult.Ok($"Selected Fixture {SelectionFormatter.Format(programmer.Selection)}")
            : ApplyValue(t);
    }

    /// <summary>
    /// <c>Fixture 1 Thru 5 + 8 - 3</c>: the first term replaces the selection (unless it starts with a sign),
    /// later terms need a sign. The selection is only changed once the whole expression is valid.
    /// </summary>
    private void ParseSelection(TokenStream t)
    {
        var steps = new List<(SelectionOp Op, List<int> Ids)>();
        string? noun = null;

        while (true)
        {
            var op = SelectionOp.Replace;
            var signed = false;
            if (t.Accept(TokenKind.Plus))
            {
                op = SelectionOp.Add;
                signed = true;
            }
            else if (t.Accept(TokenKind.Minus))
            {
                op = SelectionOp.Remove;
                signed = true;
            }

            if (steps.Count > 0 && !signed)
                break;

            if (t.PeekWord("fixture") || t.PeekWord("group"))
                noun = t.Next().Text;
            if (noun is null)
                throw new CommandException("Fixture or Group expected");

            var (from, to) = ParseRange(t);
            var ids = noun == "fixture" ? FixturesInRange(from, to) : GroupFixtures(from, to);
            steps.Add((op, ids));
        }

        foreach (var (op, ids) in steps)
        {
            switch (op)
            {
                case SelectionOp.Replace:
                    programmer.Select(ids);
                    break;
                case SelectionOp.Add:
                    programmer.AddToSelection(ids);
                    break;
                case SelectionOp.Remove:
                    programmer.RemoveFromSelection(ids);
                    break;
            }
        }
    }

    private List<int> FixturesInRange(int from, int to)
    {
        var ids = show.Patch.Fixtures.Where(f => f.Id >= from && f.Id <= to).Select(f => f.Id).ToList();
        if (ids.Count == 0)
            throw new CommandException(from == to ? $"Fixture {from} is not patched" : $"No fixtures patched in {from} Thru {to}");

        return ids;
    }

    private List<int> GroupFixtures(int from, int to)
    {
        var groups = show.Groups.Values.Where(g => g.Number >= from && g.Number <= to).ToList();
        if (groups.Count == 0)
            throw new CommandException(from == to ? $"Group {from} does not exist" : $"No groups in {from} Thru {to}");

        return groups.SelectMany(g => g.FixtureIds).Where(id => show.Patch.Find(id) is not null).Distinct().ToList();
    }

    private static (int From, int To) ParseRange(TokenStream t)
    {
        var from = t.ExpectInteger("Number");
        var to = t.AcceptWord("thru") ? t.ExpectInteger("Number after Thru") : from;
        return from <= to ? (from, to) : (to, from);
    }

    // ---- values ----------------------------------------------------------------------------------------------


    // ---- record / update / delete / label --------------------------------------------------------------------

    private CommandResult Record(TokenStream t)
    {
        var target = ParseTarget(t);
        var label = t.AcceptText();
        t.ExpectEnd();

        switch (target.Kind)
        {
            case "cue":
            {
                if (programmer.IsEmpty)
                    throw new CommandException("Programmer is empty. Nothing to record");

                var cuelist = show.GetOrAddCuelist(target.Cuelist);
                var existing = cuelist.Find(target.Number);
                cuelist.Set(new Cue(target.Number, label ?? existing?.Label ?? "", new Dictionary<ValueKey, double>(programmer.Values)));
                return CommandResult.Ok($"{(existing is null ? "Recorded" : "Replaced")} {Describe(target)}");
            }
            case "group":
            {
                if (programmer.Selection.Count == 0)
                    throw new CommandException("Nothing selected. Select fixtures first");

                var number = (int)target.Number;
                var existing = show.Groups.GetValueOrDefault(number);
                show.Groups[number] = new Group(number, label ?? existing?.Label ?? $"Group {number}", [.. programmer.Selection]);
                return CommandResult.Ok($"{(existing is null ? "Recorded" : "Replaced")} Group {number}");
            }
            default:
                throw new CommandException("Record Cue or Record Group");
        }
    }

    private CommandResult Update(TokenStream t)
    {
        var target = ParseTarget(t);
        t.ExpectEnd();

        if (target.Kind != "cue")
            throw new CommandException("Update Cue <number>");
        if (programmer.IsEmpty)
            throw new CommandException("Programmer is empty. Nothing to merge");

        var cue = FindCue(target);
        foreach (var (key, value) in programmer.Values)
            cue.Values[key] = value;

        return CommandResult.Ok($"Updated {Describe(target)}");
    }

    private CommandResult Delete(TokenStream t)
    {
        if (t.AcceptWord("fixture"))
        {
            var (from, to) = ParseRange(t);
            t.ExpectEnd();

            var ids = FixturesInRange(from, to);
            foreach (var id in ids)
            {
                show.Patch.Remove(id);
                programmer.RemoveFixture(id);
                foreach (var group in show.Groups.Values)
                    group.FixtureIds.Remove(id);
            }

            return CommandResult.Ok($"Deleted Fixture {SelectionFormatter.Format(ids)}");
        }

        var target = ParseTarget(t);
        t.ExpectEnd();

        switch (target.Kind)
        {
            case "cue":
                FindCue(target);
                show.Cuelists[target.Cuelist].Remove(target.Number);
                break;
            case "group":
                if (!show.Groups.Remove((int)target.Number))
                    throw new CommandException($"Group {target.Number} does not exist");
                break;
            case "cuelist":
                if (!show.Cuelists.Remove((int)target.Number))
                    throw new CommandException($"Cuelist {target.Number} does not exist");
                break;
            case "universe":
                DeleteUniverse((int)target.Number);
                break;
        }

        return CommandResult.Ok($"Deleted {Describe(target)}");
    }

    private CommandResult Label(TokenStream t)
    {
        var target = ParseTarget(t);
        var text = t.AcceptText() ?? throw new CommandException("Label text in quotes expected, e.g. Label Group 1 \"Front\"");
        t.ExpectEnd();

        var number = (int)target.Number;
        switch (target.Kind)
        {
            case "cue":
                FindCue(target).Label = text;
                break;
            case "group":
                (show.Groups.GetValueOrDefault(number) ?? throw new CommandException($"Group {number} does not exist")).Label = text;
                break;
            case "cuelist":
                (show.Cuelists.GetValueOrDefault(number) ?? throw new CommandException($"Cuelist {number} does not exist")).Label = text;
                break;
            case "fixture":
                (show.Patch.Find(number) ?? throw new CommandException($"Fixture {number} is not patched")).Label = text;
                break;
            case "universe":
                (show.Universes.GetValueOrDefault(number) ?? throw new CommandException($"Universe {number} does not exist")).Label = text;
                break;
        }

        return CommandResult.Ok($"Labelled {Describe(target)} \"{text}\"");
    }

    // ---- playback --------------------------------------------------------------------------------------------

    private CommandResult Go(TokenStream t)
    {
        var backwards = t.Accept(TokenKind.Minus);
        if (!backwards)
            t.Accept(TokenKind.Plus);

        var cuelist = ParseCuelistSuffix(t);
        return backwards ? StepBack(cuelist) : StepForward(cuelist);
    }

    private CommandResult Back(TokenStream t) => StepBack(ParseCuelistSuffix(t));

    private CommandResult GoTo(TokenStream t)
    {
        var target = ParseTarget(t);
        t.ExpectEnd();

        if (target.Kind != "cue")
            throw new CommandException("Goto Cue <number>");

        FindCue(target);
        show.Cuelists[target.Cuelist].GoTo(target.Number);
        return CommandResult.Ok($"Cuelist {target.Cuelist} is on {Describe(target)}");
    }

    private CommandResult Off(TokenStream t)
    {
        var cuelist = ParseCuelistSuffix(t);
        cuelist.Release();
        return CommandResult.Ok($"Cuelist {cuelist.Number} is off");
    }

    private CommandResult StepForward(Cuelist cuelist)
    {
        var cue = cuelist.Go();
        if (cue is null)
        {
            throw new CommandException(cuelist.IsActive
                ? $"Cuelist {cuelist.Number} is already on its last cue"
                : $"Cuelist {cuelist.Number} has no cues");
        }

        return CommandResult.Ok($"Cuelist {cuelist.Number}: {DescribeCue(cue)}");
    }

    private CommandResult StepBack(Cuelist cuelist)
    {
        if (!cuelist.IsActive)
            throw new CommandException($"Cuelist {cuelist.Number} is off");

        var cue = cuelist.Back() ?? throw new CommandException($"Cuelist {cuelist.Number} is already on its first cue");
        return CommandResult.Ok($"Cuelist {cuelist.Number}: {DescribeCue(cue)}");
    }

    /// <summary>Optional <c>Cuelist n</c> at the end of a playback command; defaults to cuelist 1.</summary>
    private Cuelist ParseCuelistSuffix(TokenStream t)
    {
        var number = t.AcceptWord("cuelist") ? t.ExpectInteger("Cuelist number") : 1;
        t.ExpectEnd();
        return show.Cuelists.GetValueOrDefault(number) ?? throw new CommandException($"Cuelist {number} does not exist");
    }

    // ---- clear -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Clear steps back one level at a time: first the selection, then the programmer values.
    /// (The command line itself is cleared first by the UI.) ClearAll does everything at once.
    /// </summary>
    private CommandResult Clear(TokenStream t, bool everything)
    {
        t.ExpectEnd();

        if (everything)
        {
            programmer.ClearSelection();
            programmer.ClearValues();
            return CommandResult.Ok("Cleared selection and programmer");
        }

        if (programmer.Selection.Count > 0)
        {
            programmer.ClearSelection();
            return CommandResult.Ok("Selection cleared");
        }

        if (!programmer.IsEmpty)
        {
            programmer.ClearValues();
            return CommandResult.Ok("Programmer cleared");
        }

        return CommandResult.Ok("Nothing to clear");
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    /// <summary><c>Cue 5</c>, <c>Cue 5 Cuelist 2</c>, <c>Group 3</c>, <c>Cuelist 2</c> or <c>Fixture 7</c>.</summary>
    private static Target ParseTarget(TokenStream t)
    {
        var kind = t.Peek is { Kind: TokenKind.Word } word ? word.Text : null;

        switch (kind)
        {
            case "cue":
            {
                t.Next();
                var number = t.ExpectNumber("Cue number");
                var cuelist = t.AcceptWord("cuelist") ? t.ExpectInteger("Cuelist number") : 1;
                return new Target("cue", number, cuelist);
            }
            case "group" or "cuelist" or "fixture" or "universe":
                t.Next();
                return new Target(kind, t.ExpectInteger($"{kind} number"), 1);
            default:
                throw new CommandException("Cue, Group, Cuelist, Fixture or Universe expected");
        }
    }

    private Cue FindCue(Target target) =>
        show.Cuelists.GetValueOrDefault(target.Cuelist)?.Find(target.Number)
        ?? throw new CommandException($"{Describe(target)} does not exist");

    private static string Describe(Target target) => target.Kind switch
    {
        "cue" => $"Cue {FormatNumber(target.Number)}{(target.Cuelist != 1 ? $" in Cuelist {target.Cuelist}" : "")}",
        _ => $"{char.ToUpperInvariant(target.Kind[0])}{target.Kind[1..]} {FormatNumber(target.Number)}",
    };

    private static string DescribeCue(Cue cue) =>
        $"Cue {FormatNumber(cue.Number)}{(cue.Label.Length > 0 ? $" \"{cue.Label}\"" : "")}";

    private static string FormatNumber(decimal number) => number.ToString("0.###", CultureInfo.InvariantCulture);
}
