using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace NxDmx.Core.Remote;

/// <summary>A decoded OSC message: an address and its arguments (int, long, float, double, string, bool or null).</summary>
public sealed record OscMessage(string Address, IReadOnlyList<object?> Arguments)
{
    /// <summary>Decodes one message, or the messages inside a bundle. Returns an empty list for anything malformed.</summary>
    public static IReadOnlyList<OscMessage> Parse(ReadOnlySpan<byte> data)
    {
        var messages = new List<OscMessage>();
        try
        {
            ParseInto(data, messages, depth: 0);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or FormatException)
        {
            // A damaged packet is dropped; the sender gets no answer in OSC anyway.
            messages.Clear();
        }

        return messages;
    }

    private static void ParseInto(ReadOnlySpan<byte> data, List<OscMessage> messages, int depth)
    {
        if (data.Length < 4)
            return;

        if (data[0] == (byte)'#')
        {
            if (depth > 4 || data.Length < 16 || Encoding.ASCII.GetString(data[..8]) != "#bundle\0")
                return;

            var position = 16;
            while (position + 4 <= data.Length)
            {
                var size = BinaryPrimitives.ReadInt32BigEndian(data[position..]);
                position += 4;
                if (size < 0 || position + size > data.Length)
                    return;

                ParseInto(data.Slice(position, size), messages, depth + 1);
                position += size;
            }

            return;
        }

        var cursor = 0;
        var address = ReadString(data, ref cursor);
        if (!address.StartsWith('/'))
            return;

        var args = new List<object?>();
        if (cursor < data.Length)
        {
            var tags = ReadString(data, ref cursor);
            if (tags.StartsWith(','))
            {
                foreach (var tag in tags.AsSpan(1))
                {
                    switch (tag)
                    {
                        case 'i':
                            args.Add(BinaryPrimitives.ReadInt32BigEndian(data[cursor..]));
                            cursor += 4;
                            break;
                        case 'h':
                            args.Add(BinaryPrimitives.ReadInt64BigEndian(data[cursor..]));
                            cursor += 8;
                            break;
                        case 'f':
                            args.Add(BinaryPrimitives.ReadSingleBigEndian(data[cursor..]));
                            cursor += 4;
                            break;
                        case 'd':
                            args.Add(BinaryPrimitives.ReadDoubleBigEndian(data[cursor..]));
                            cursor += 8;
                            break;
                        case 's' or 'S':
                            args.Add(ReadString(data, ref cursor));
                            break;
                        case 'T':
                            args.Add(true);
                            break;
                        case 'F':
                            args.Add(false);
                            break;
                        case 'N' or 'I':
                            args.Add(null);
                            break;
                        default:
                            throw new FormatException("Unsupported OSC argument type");
                    }
                }
            }
        }

        messages.Add(new OscMessage(address, args));
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int cursor)
    {
        var end = data[cursor..].IndexOf((byte)0);
        if (end < 0)
            throw new FormatException("Unterminated OSC string");

        var text = Encoding.UTF8.GetString(data.Slice(cursor, end));
        cursor += (end + 4) & ~3;
        return text;
    }

    /// <summary>An argument as text, with whole numbers written without decimals.</summary>
    public string? Text(int index)
    {
        if (index >= Arguments.Count)
            return null;

        return Arguments[index] switch
        {
            null => null,
            bool b => b ? "1" : "0",
            float f => Format(f),
            double d => Format(d),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
    }

    /// <summary>An argument as a number, or null when it is missing or not numeric.</summary>
    public double? Number(int index)
    {
        if (index >= Arguments.Count)
            return null;

        return Arguments[index] switch
        {
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            bool b => b ? 1 : 0,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static string Format(double value) =>
        double.IsFinite(value) ? Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture) : "0";

    /// <summary>Builds a message for tests and for answering. Strings, ints and floats are supported.</summary>
    public static byte[] Encode(string address, params object[] arguments)
    {
        var bytes = new List<byte>();
        AddString(bytes, address);
        AddString(bytes, "," + string.Concat(arguments.Select(a => a switch { int => 'i', float => 'f', string => 's', _ => throw new ArgumentException("Unsupported type") })));
        foreach (var argument in arguments)
        {
            switch (argument)
            {
                case int i:
                    var four = new byte[4];
                    BinaryPrimitives.WriteInt32BigEndian(four, i);
                    bytes.AddRange(four);
                    break;
                case float f:
                    var single = new byte[4];
                    BinaryPrimitives.WriteSingleBigEndian(single, f);
                    bytes.AddRange(single);
                    break;
                case string s:
                    AddString(bytes, s);
                    break;
            }
        }

        return [.. bytes];
    }

    private static void AddString(List<byte> bytes, string text)
    {
        bytes.AddRange(Encoding.UTF8.GetBytes(text));
        bytes.Add(0);
        while (bytes.Count % 4 != 0)
            bytes.Add(0);
    }
}
