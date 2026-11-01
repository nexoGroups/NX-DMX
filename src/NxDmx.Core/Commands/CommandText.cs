using System.Text;

namespace NxDmx.Core.Commands;

public static class CommandText
{
    /// <summary>Splits <c>Group 1; Color Red</c> at semicolons that are not inside quotes. Empty parts are dropped.</summary>
    public static IReadOnlyList<string> Split(string lines)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in lines)
        {
            if (c == '"')
                inQuotes = !inQuotes;

            if (c == ';' && !inQuotes)
            {
                Flush();
                continue;
            }

            current.Append(c is '\r' or '\n' && !inQuotes ? ' ' : c);
        }

        Flush();
        return parts;

        void Flush()
        {
            var part = current.ToString().Trim();
            if (part.Length > 0)
                parts.Add(part);

            current.Clear();
        }
    }

    /// <summary>Quotes a name for use in a command: <c>Spot Mover</c> becomes <c>"Spot Mover"</c>.</summary>
    public static string Quote(string name) => $"\"{name.Replace("\"", "")}\"";
}
