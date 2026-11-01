using System.Globalization;
using NxDmx.Core.Engine;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Commands;

public sealed partial class CommandInterpreter
{
    // ---- values ----------------------------------------------------------------------------------------------

    /// <summary>One piece of a value command: a percentage, or a named function of the channel.</summary>
    private sealed record ValueTerm(FixtureAttribute Attribute, double Percent, string? Function);

    /// <summary>
    /// <c>At 50</c>, <c>Red At 100</c>, <c>Gobo Function "Gobo 2"</c>, or several in a row:
    /// <c>Pan At 25 Tilt At 70</c>. Values go to every selected fixture that has the attribute.
    /// </summary>
    private CommandResult ApplyValue(TokenStream t)
    {
        var terms = new List<ValueTerm> { ParseValueTerm(t) };
        while (t.Peek is { Kind: TokenKind.Word } next && FixtureAttributeExtensions.TryParse(next.Text, out _))
            terms.Add(ParseValueTerm(t));

        t.ExpectEnd();

        if (programmer.Selection.Count == 0)
            throw new CommandException("Nothing selected. Select fixtures first, e.g. Fixture 1 Thru 5");

        var parts = new List<string>();
        foreach (var term in terms)
        {
            parts.Add(term.Function is null ? SetPercent(term) : SetFunction(term));
        }

        return CommandResult.Ok($"Fixture {SelectionFormatter.Format(programmer.Selection)}: {string.Join(", ", parts)}");
    }

    private static ValueTerm ParseValueTerm(TokenStream t)
    {
        var attribute = FixtureAttribute.Dimmer;
        var named = false;
        if (t.Peek is { Kind: TokenKind.Word } word && FixtureAttributeExtensions.TryParse(word.Text, out var parsed))
        {
            attribute = parsed;
            named = true;
            t.Next();
        }

        if (t.AcceptWord("function"))
        {
            if (!named)
                throw new CommandException("Name the attribute first, e.g. Gobo Function \"Gobo 2\"");

            var name = t.AcceptText() ?? t.ExpectWordValue("Function name");
            return new ValueTerm(attribute, 0, name);
        }

        double percent;
        if (t.AcceptWord("full"))
        {
            percent = 100;
        }
        else
        {
            t.ExpectWord("at");
            percent = t.AcceptWord("full") ? 100 : (double)t.ExpectNumber("Value");
        }

        if (percent is < 0 or > 100)
            throw new CommandException("Value must be between 0 and 100");

        return new ValueTerm(attribute, percent, null);
    }

    private string SetPercent(ValueTerm term)
    {
        var affected = 0;
        foreach (var id in programmer.Selection)
        {
            if (show.Patch.Find(id) is { } fixture && fixture.Type.Supports(term.Attribute))
            {
                programmer.Set(id, term.Attribute, term.Percent / 100);
                affected++;
            }
        }

        if (affected == 0)
            throw new CommandException($"The selected fixtures have no {term.Attribute} attribute");

        return $"{term.Attribute} {term.Percent.ToString("0.##", CultureInfo.InvariantCulture)} %";
    }

    private string SetFunction(ValueTerm term)
    {
        var affected = 0;
        foreach (var id in programmer.Selection)
        {
            var function = show.Patch.Find(id)?.Type.Find(term.Attribute)?.FunctionList
                .FirstOrDefault(f => f.Name.Equals(term.Function, StringComparison.OrdinalIgnoreCase));
            if (function is null)
                continue;

            programmer.Set(id, term.Attribute, function.StartValue);
            affected++;
        }

        if (affected == 0)
            throw new CommandException($"No selected fixture has the function \"{term.Function}\" on {term.Attribute}");

        return $"{term.Attribute} \"{term.Function}\"";
    }

    /// <summary><c>Color Red</c> / <c>Color "Warm White"</c>: every colour emitter of the selected fixtures at once.</summary>
    private CommandResult ApplyColor(TokenStream t)
    {
        var name = t.AcceptName() ?? throw new CommandException("Color name expected, e.g. Color Red");
        t.ExpectEnd();

        var preset = ColorPresets.Find(name)
            ?? throw new CommandException($"Unknown color '{name}'. Available: {string.Join(", ", ColorPresets.All.Select(c => c.Name))}");
        if (programmer.Selection.Count == 0)
            throw new CommandException("Nothing selected. Select fixtures first, e.g. Fixture 1 Thru 5");

        var affected = 0;
        foreach (var id in programmer.Selection)
        {
            if (show.Patch.Find(id) is not { } fixture)
                continue;

            var values = preset.ValuesFor(fixture.Type);
            foreach (var (attribute, value) in values)
                programmer.Set(id, attribute, value);

            if (values.Count > 0)
                affected++;
        }

        if (affected == 0)
            throw new CommandException("The selected fixtures have no color attributes");

        return CommandResult.Ok($"Fixture {SelectionFormatter.Format(programmer.Selection)}: Color {preset.Name}");
    }

    // ---- output ----------------------------------------------------------------------------------------------

    /// <summary><c>Blackout</c> toggles, <c>Blackout On</c> / <c>Blackout Off</c> set it.</summary>
    private CommandResult Blackout(TokenStream t)
    {
        bool state;
        if (t.AcceptWord("on"))
            state = true;
        else if (t.AcceptWord("off"))
            state = false;
        else
            state = !_output.Blackout;

        t.ExpectEnd();
        _output.Blackout = state;
        return CommandResult.Ok(state ? "Blackout on" : "Blackout off");
    }

    /// <summary><c>Master At 80</c> or <c>Master 80</c>: the grand master in percent.</summary>
    private CommandResult Master(TokenStream t)
    {
        t.AcceptWord("at");
        var percent = t.AcceptWord("full") ? 100m : t.ExpectNumber("Value");
        t.ExpectEnd();

        if (percent is < 0 or > 100)
            throw new CommandException("Value must be between 0 and 100");

        _output.GrandMaster = (double)percent / 100;
        return CommandResult.Ok($"Grand master {percent.ToString("0.##", CultureInfo.InvariantCulture)} %");
    }

    /// <summary>Starts the cuelist when it is off, otherwise releases it. One key for a Stream Deck button.</summary>
    private CommandResult Toggle(TokenStream t)
    {
        var cuelist = ParseCuelistSuffix(t);
        if (cuelist.IsActive)
        {
            cuelist.Release();
            return CommandResult.Ok($"Cuelist {cuelist.Number} is off");
        }

        return StepForward(cuelist);
    }
}
