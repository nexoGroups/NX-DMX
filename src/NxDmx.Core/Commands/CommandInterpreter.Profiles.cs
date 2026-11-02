using NxDmx.Core.Fixtures;

namespace NxDmx.Core.Commands;

public sealed partial class CommandInterpreter
{
    // ---- edits made by the interface rather than typed ------------------------------------------------------

    /// <summary>
    /// Runs an edit that does not come from the command line (the Fixture Manager, say) with the same safety net as a
    /// command: it can be taken back with Oops, and when it fails nothing is changed.
    /// </summary>
    public CommandResult Mutate(string description, Func<string> edit)
    {
        var before = ShowSnapshot.Capture(show, programmer);
        try
        {
            var baseline = ShowValidator.Check(show, programmer);
            var message = edit();
            Seal(baseline);
            _undo.Add((before, description));
            if (_undo.Count > MaxUndoSteps)
                _undo.RemoveAt(0);

            return CommandResult.Ok(message);
        }
        catch (CommandRefusedException ex)
        {
            before.Restore(show, programmer);
            return CommandResult.Refuse(ex.Message);
        }
        catch (CommandException ex)
        {
            before.Restore(show, programmer);
            return CommandResult.Error(ex.Message);
        }
    }

    /// <summary>
    /// Stores a profile in the show. <paramref name="originalName"/> is the name it had before (null for a new one);
    /// when it differs, the profile is renamed. Fixtures that use the profile follow the change at once.
    /// </summary>
    public CommandResult SaveProfile(string? originalName, FixtureType type) =>
        Mutate($"Save profile \"{type.Name}\"", () =>
        {
            type.EnsureValid();
            var name = type.Name.Trim();

            if (FixtureTypeLibrary.Find(name) is not null)
                throw new CommandException($"\"{name}\" is the name of a built-in profile. Choose another name");
            if (show.CustomTypes.TryGetValue(name, out var sameName) && !string.Equals(sameName.Name, originalName, StringComparison.OrdinalIgnoreCase))
                throw new CommandException($"There is already a profile called \"{name}\"");

            show.CustomTypes.TryGetValue(originalName ?? "", out var old);
            if (originalName is not null && old is null)
                throw new CommandException($"The profile \"{originalName}\" no longer exists");

            if (old is not null)
                show.CustomTypes.Remove(old.Name);
            show.CustomTypes[name] = type;

            var followers = 0;
            if (old is not null)
            {
                foreach (var fixture in show.Patch.Fixtures.Where(f => ReferenceEquals(f.Type, old)))
                {
                    if (fixture.Address + type.Footprint - 1 > 512)
                    {
                        throw new CommandException(
                            $"Fixture {fixture.Id} at {fixture.Universe}.{fixture.Address} would no longer fit into its universe with {type.Footprint} channels");
                    }

                    if (fixture.Label == $"{old.Name} {fixture.Id}")
                        fixture.Label = $"{name} {fixture.Id}";

                    fixture.Type = type;
                    followers++;
                }
            }

            return old is null
                ? $"Saved profile \"{name}\""
                : $"Saved profile \"{name}\"{(followers > 0 ? $" ({followers} fixture{(followers == 1 ? "" : "s")} updated)" : "")}";
        });

    /// <summary>Removes one of the show's own profiles. Refused while fixtures still use it.</summary>
    public CommandResult DeleteProfile(string name) =>
        Mutate($"Delete profile \"{name}\"", () =>
        {
            var type = show.CustomTypes.GetValueOrDefault(name)
                ?? throw new CommandException(FixtureTypeLibrary.Find(name) is null
                    ? $"The profile \"{name}\" does not exist"
                    : $"\"{name}\" is a built-in profile and cannot be deleted");

            var users = show.Patch.Fixtures.Count(f => ReferenceEquals(f.Type, type));
            if (users > 0)
                throw new CommandRefusedException($"\"{name}\" is used by {users} patched fixture{(users == 1 ? "" : "s")}. Delete or re-patch them first");

            show.CustomTypes.Remove(type.Name);
            return $"Deleted profile \"{name}\"";
        });
}
