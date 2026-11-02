namespace NxDmx.Core.Commands;

/// <summary>
/// The answer to a command. <paramref name="Refused"/> separates "this cannot be done" (the check found a rule that forbids it,
/// like deleting a cuelist that is running) from a mistake in what was typed. Either way nothing was changed.
/// </summary>
public sealed record CommandResult(bool Success, string Message, bool Refused = false)
{
    public const string RefusedPrefix = "Not allowed: ";

    public static CommandResult Ok(string message = "") => new(true, message);

    public static CommandResult Error(string message) => new(false, message);

    public static CommandResult Refuse(string reason) => new(false, RefusedPrefix + reason, true);
}

/// <summary>A problem with what the operator typed. Its message is shown in the command feedback.</summary>
public class CommandException(string message) : Exception(message);

/// <summary>What was asked is well formed, but the check says it must not be done now. The message is the reason, without the "Not allowed" prefix.</summary>
public sealed class CommandRefusedException(string reason) : CommandException(reason);
