namespace NxDmx.Core.Commands;

public sealed record CommandResult(bool Success, string Message)
{
    public static CommandResult Ok(string message = "") => new(true, message);

    public static CommandResult Error(string message) => new(false, message);
}

/// <summary>A problem with what the operator typed. Its message is shown in the command feedback.</summary>
public sealed class CommandException(string message) : Exception(message);
