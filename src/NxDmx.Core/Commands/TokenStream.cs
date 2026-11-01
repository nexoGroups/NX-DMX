using System.Globalization;

namespace NxDmx.Core.Commands;

internal sealed class TokenStream(List<Token> tokens)
{
    private int _position;

    public bool AtEnd => _position >= tokens.Count;

    public Token? Peek => AtEnd ? null : tokens[_position];

    public Token Next()
    {
        if (AtEnd)
            throw new CommandException("Command is incomplete");

        return tokens[_position++];
    }

    public bool PeekWord(string word) => Peek is { Kind: TokenKind.Word } token && token.Text == word;

    public bool AcceptWord(string word)
    {
        if (!PeekWord(word))
            return false;

        _position++;
        return true;
    }

    public bool Accept(TokenKind kind)
    {
        if (Peek?.Kind != kind)
            return false;

        _position++;
        return true;
    }

    public void ExpectWord(string word)
    {
        if (!AcceptWord(word))
            throw new CommandException($"'{word}' expected");
    }

    public void ExpectEnd()
    {
        if (!AtEnd)
            throw new CommandException($"Unexpected '{Peek!.Value.Text}'");
    }

    public string ExpectWordValue(string what) =>
        Peek is { Kind: TokenKind.Word } ? Next().Text : throw new CommandException($"{what} expected");

    /// <summary>The raw text of a number token, for values like the address 1.15.</summary>
    public string ExpectNumberText(string what) =>
        Peek is { Kind: TokenKind.Number } ? Next().Text : throw new CommandException($"{what} expected");

    public int ExpectInteger(string what)
    {
        var text = ExpectNumberText(what);
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
            throw new CommandException($"{what} must be a whole number, 1 or higher");

        return value;
    }

    public decimal ExpectNumber(string what) =>
        decimal.Parse(ExpectNumberText(what), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    public string? AcceptText() => Peek is { Kind: TokenKind.Text } ? Next().Text : null;
}
