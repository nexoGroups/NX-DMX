using NxDmx.Core.Playback;

namespace NxDmx.Core.Commands;

public sealed partial class CommandInterpreter
{
    // ---- the check before and after an operation ---------------------------------------------------------------
    //
    // Like on ONYX, every operation is checked: the rules that depend on the operation (a running cuelist cannot be
    // deleted) are asked before it changes anything, and the rules every show must keep (see ShowValidator) are asked
    // again afterwards. An operation that breaks one is taken back and answered with "Not allowed: ...".

    /// <summary>Throws when the operation made the show worse than it was before: a rule that held before no longer does.</summary>
    private void Seal(IReadOnlyList<ShowIssue> baseline)
    {
        var known = baseline.Select(i => i.Key).ToHashSet();
        var added = ShowValidator.Check(show, programmer).FirstOrDefault(i => !known.Contains(i.Key));
        if (added is not null)
            throw new CommandRefusedException(added.Message);
    }

    private void RequireNotOnFader(FaderKind kind, int target, string what)
    {
        var fader = show.Faders.Values.FirstOrDefault(f => f.Kind == kind && f.Target == target);
        if (fader is not null)
            throw new CommandRefusedException($"{what} is assigned to Fader {fader.Number}. Clear the fader first (Fader {fader.Number} Clear)");
    }

    private void RequireCueNotPlaying(Target target)
    {
        if (show.Cuelists.GetValueOrDefault(target.Cuelist) is { } cuelist && cuelist.CurrentNumber == target.Number)
            throw new CommandRefusedException($"{Describe(target)} is playing. Release the cuelist first (Off) or go to another cue");
    }

    /// <summary>How many recorded cue values belong to these fixtures. They stay in the cues when a fixture is deleted.</summary>
    private int CountCueValuesOf(IReadOnlyCollection<int> fixtureIds)
    {
        var ids = fixtureIds.ToHashSet();
        return show.Cuelists.Values.SelectMany(l => l.Cues).Sum(c => c.Values.Keys.Count(k => ids.Contains(k.FixtureId)));
    }

    private static string OrphanNote(int values) =>
        $". Note: {values} recorded cue value{(values == 1 ? "" : "s")} of {(values == 1 ? "it" : "them")} stay in the cues and do nothing until a fixture with that number is patched again";

    /// <summary>
    /// True for commands that act on what is playing right now (Go, a fader move, Oops). They cannot be tried on a copy
    /// of the show, because the copy is not playing anything.
    /// </summary>
    public static bool IsLive(string line)
    {
        try
        {
            var tokens = Tokenizer.Tokenize(line);
            if (tokens.Count > 0 && tokens[^1] is { Kind: TokenKind.Word, Text: "please" })
                tokens.RemoveAt(tokens.Count - 1);

            var head = tokens.Count > 0 && tokens[0] is { Kind: TokenKind.Word } word ? word.Text : null;
            return head is not null && (head == "oops" || LiveCommands.Contains(head) || IsLiveFaderCommand(head, tokens));
        }
        catch (CommandException)
        {
            return false;
        }
    }
}
