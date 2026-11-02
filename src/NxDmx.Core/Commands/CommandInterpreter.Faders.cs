using System.Globalization;
using NxDmx.Core.Engine;
using NxDmx.Core.Playback;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Commands;

public sealed partial class CommandInterpreter
{
    // ---- colours ---------------------------------------------------------------------------------------------

    /// <summary>A colour by name (custom ones first), or typed as hex: <c>Color #FF8800</c>.</summary>
    private ColorPreset? FindColorOrHex(string name) =>
        name.StartsWith('#') ? ColorPreset.FromHex(name, name) : show.FindColor(name);

    /// <summary>Adds or replaces a colour of the show. The name may not be empty, have quotes or be longer than 40 characters.</summary>
    public CommandResult SaveColor(string name, string hex) =>
        Mutate($"Save color \"{name}\"", () =>
        {
            ValidateColorName(name);
            var color = ColorPreset.FromHex(name, hex)
                ?? throw new CommandException($"'{hex}' is not a colour. Use six hex digits, e.g. #FF8800");

            show.CustomColors[color.Name] = color;
            return $"Saved color \"{color.Name}\" {color.Hex}";
        });

    /// <summary>Adds many colours in one step (an import), so that Oops takes the whole import back.</summary>
    public CommandResult ImportColors(IReadOnlyList<(string Name, string Hex)> colors) =>
        Mutate("Import colors", () =>
        {
            var count = 0;
            foreach (var (name, hex) in colors)
            {
                ValidateColorName(name);
                var color = ColorPreset.FromHex(name, hex)
                    ?? throw new CommandException($"'{hex}' ({name}) is not a colour. Use six hex digits, e.g. #FF8800");

                show.CustomColors[color.Name] = color;
                count++;
            }

            return $"Imported {count} color{(count == 1 ? "" : "s")}";
        });

    public CommandResult DeleteColor(string name) =>
        Mutate($"Delete color \"{name}\"", () =>
        {
            if (!show.CustomColors.Remove(name.Trim()))
                throw new CommandException($"\"{name}\" is not one of your own colours");

            return $"Deleted color \"{name}\"";
        });

    private static void ValidateColorName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 40 || name.Contains('"') || name.Any(char.IsControl))
            throw new CommandException("A colour needs a name of at most 40 characters without quotation marks");
    }

    // ---- faders ----------------------------------------------------------------------------------------------

    /// <summary>Moving a fader or pressing its button is live (like Go); changing what is assigned is a programming step.</summary>
    private static bool IsLiveFaderCommand(string head, List<Token> tokens) =>
        head == "fader" && tokens.Count >= 3 && tokens[2] is { Kind: TokenKind.Word, Text: "at" or "button" };

    /// <summary>
    /// <c>Fader 3 At 60</c> moves a fader, <c>Fader 3 Button</c> presses its button, <c>Fader 3 Cuelist 2</c>,
    /// <c>Group 1</c> or <c>Master</c> assigns it, <c>Clear</c> empties it, <c>Label</c> and <c>Color</c> style it.
    /// </summary>
    private CommandResult FaderCommand(TokenStream t)
    {
        var number = t.ExpectInteger("Fader number");
        if (number > FaderConfig.MaxFaders)
            throw new CommandException($"Fader must be 1..{FaderConfig.MaxFaders}");

        if (t.AcceptWord("at"))
        {
            var percent = t.AcceptWord("full") ? 100m : t.ExpectNumber("Value");
            t.ExpectEnd();
            if (percent is < 0 or > 100)
                throw new CommandException("Value must be between 0 and 100");

            return MoveFader(number, (double)percent / 100);
        }

        if (t.AcceptWord("button"))
        {
            t.ExpectEnd();
            return PressFaderButton(number);
        }

        var config = show.Faders.GetValueOrDefault(number) ?? new FaderConfig(number);
        string message;

        if (t.AcceptWord("cuelist"))
        {
            var target = t.ExpectInteger("Cuelist number");
            if (!show.Cuelists.ContainsKey(target))
                throw new CommandException($"Cuelist {target} does not exist");

            (config.Kind, config.Target) = (FaderKind.Cuelist, target);
            message = $"Fader {number} now controls Cuelist {target}";
        }
        else if (t.AcceptWord("group"))
        {
            var target = t.ExpectInteger("Group number");
            if (!show.Groups.ContainsKey(target))
                throw new CommandException($"Group {target} does not exist");

            (config.Kind, config.Target) = (FaderKind.Group, target);
            message = $"Fader {number} now controls the intensity of Group {target}";
        }
        else if (t.AcceptWord("master"))
        {
            (config.Kind, config.Target) = (FaderKind.Master, 0);
            message = $"Fader {number} now controls the grand master";
        }
        else if (t.AcceptWord("clear"))
        {
            t.ExpectEnd();
            show.Faders.Remove(number);
            _output.ForgetFader(number);
            return CommandResult.Ok($"Fader {number} cleared");
        }
        else if (t.AcceptWord("label"))
        {
            config.Label = t.AcceptName() ?? throw new CommandException("Label text expected");
            message = $"Fader {number} labelled \"{config.Label}\"";
        }
        else if (t.AcceptWord("color"))
        {
            var name = t.AcceptName() ?? throw new CommandException("Colour expected: a name, #RRGGBB or Default");
            if (name.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                config.Color = "";
                message = $"Fader {number} has its default colour";
            }
            else
            {
                var color = FindColorOrHex(name) ?? throw new CommandException($"Unknown color '{name}'");
                config.Color = color.Hex;
                message = $"Fader {number} colour {color.Hex}";
            }
        }
        else
        {
            throw new CommandException("Fader <n> At <value> | Button | Cuelist <n> | Group <n> | Master | Clear | Label \"…\" | Color <name or #hex>");
        }

        t.ExpectEnd();
        show.Faders[number] = config;
        if (config.Kind == FaderKind.None)
            show.Faders.Remove(number);

        return CommandResult.Ok(message);
    }

    private CommandResult MoveFader(int number, double level)
    {
        var fader = show.Faders.GetValueOrDefault(number);
        if (fader is null || fader.Kind == FaderKind.None)
            throw new CommandException($"Fader {number} has nothing assigned");

        var percent = Math.Round(level * 100).ToString(CultureInfo.InvariantCulture);
        switch (fader.Kind)
        {
            case FaderKind.Master:
                _output.GrandMaster = level;
                return CommandResult.Ok($"Grand master {percent} %");

            case FaderKind.Group:
                _output.SetFaderLevel(number, level);
                return CommandResult.Ok($"Fader {number}: Group {fader.Target} {percent} %");

            default:
                var cuelist = show.Cuelists.GetValueOrDefault(fader.Target)
                    ?? throw new CommandException($"Cuelist {fader.Target} does not exist");
                _output.SetFaderLevel(number, level);

                // Up starts the cuelist, all the way down releases it, like a playback fader.
                if (level > 0 && !cuelist.IsActive)
                    cuelist.Go();
                else if (level <= 0 && cuelist.IsActive)
                    cuelist.Release();

                return CommandResult.Ok($"Fader {number}: Cuelist {cuelist.Number} {percent} %");
        }
    }

    /// <summary>The button above a fader: next cue for a cuelist, blackout for the master, a group toggles between full and out.</summary>
    private CommandResult PressFaderButton(int number)
    {
        var fader = show.Faders.GetValueOrDefault(number);
        if (fader is null || fader.Kind == FaderKind.None)
            throw new CommandException($"Fader {number} has nothing assigned");

        switch (fader.Kind)
        {
            case FaderKind.Master:
                _output.Blackout = !_output.Blackout;
                return CommandResult.Ok(_output.Blackout ? "Blackout on" : "Blackout off");

            case FaderKind.Group:
                var now = _output.FaderLevel(number, FrameRenderer.DefaultLevel(FaderKind.Group));
                _output.SetFaderLevel(number, now > 0 ? 0 : 1);
                return CommandResult.Ok($"Fader {number}: Group {fader.Target} {(now > 0 ? "out" : "full")}");

            default:
                var cuelist = show.Cuelists.GetValueOrDefault(fader.Target)
                    ?? throw new CommandException($"Cuelist {fader.Target} does not exist");
                if (cuelist.Go() is null)
                    throw new CommandException($"Cuelist {cuelist.Number} is already on its last cue");

                if (_output.FaderLevel(number, 0) <= 0)
                    _output.SetFaderLevel(number, 1);

                return CommandResult.Ok($"Cuelist {cuelist.Number}: Cue {cuelist.CurrentNumber:0.###}");
        }
    }
}
