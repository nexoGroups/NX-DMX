namespace NxDmx.Core.Commands;

internal enum TokenKind
{
    Word,
    Number,
    Text,
    Plus,
    Minus,
}

internal readonly record struct Token(TokenKind Kind, string Text);

internal static class Tokenizer
{
    /// <summary>Short forms an operator can type instead of the full keyword.</summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["fix"] = "fixture",
        ["grp"] = "group",
        ["rec"] = "record",
        ["upd"] = "update",
        ["del"] = "delete",
        ["lab"] = "label",
    };

    /// <summary>
    /// Splits a command line into tokens. Words are lower-cased, '@' is the word "at",
    /// numbers accept '.' or ',' as decimal separator, and text in double quotes keeps its case.
    /// </summary>
    public static List<Token> Tokenize(string line)
    {
        var tokens = new List<Token>();
        var i = 0;

        while (i < line.Length)
        {
            var c = line[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '+')
            {
                tokens.Add(new Token(TokenKind.Plus, "+"));
                i++;
            }
            else if (c == '-')
            {
                tokens.Add(new Token(TokenKind.Minus, "-"));
                i++;
            }
            else if (c == '@')
            {
                tokens.Add(new Token(TokenKind.Word, "at"));
                i++;
            }
            else if (c == '"')
            {
                var end = line.IndexOf('"', i + 1);
                if (end < 0)
                    throw new CommandException("Text in quotes is missing its closing quote");

                tokens.Add(new Token(TokenKind.Text, line[(i + 1)..end]));
                i = end + 1;
            }
            else if (char.IsAsciiDigit(c))
            {
                var start = i;
                var seenSeparator = false;
                while (i < line.Length)
                {
                    if (char.IsAsciiDigit(line[i]))
                    {
                        i++;
                    }
                    else if (!seenSeparator && (line[i] is '.' or ',') && i + 1 < line.Length && char.IsAsciiDigit(line[i + 1]))
                    {
                        seenSeparator = true;
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                // 10.0.0.5: further dotted groups make it one token, which is how an IP address is typed.
                while (!line[start..i].Contains(',') && i + 1 < line.Length && line[i] == '.' && char.IsAsciiDigit(line[i + 1]))
                {
                    i++;
                    while (i < line.Length && char.IsAsciiDigit(line[i]))
                        i++;
                }

                tokens.Add(new Token(TokenKind.Number, line[start..i].Replace(',', '.')));
            }
            else if (char.IsLetter(c))
            {
                var start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                    i++;

                var word = line[start..i].ToLowerInvariant();
                tokens.Add(new Token(TokenKind.Word, Aliases.GetValueOrDefault(word, word)));
            }
            else
            {
                throw new CommandException($"Unexpected character '{c}'");
            }
        }

        return tokens;
    }
}
