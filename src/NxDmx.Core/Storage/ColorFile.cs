using System.Text.Json;
using NxDmx.Core.Programming;

namespace NxDmx.Core.Storage;

/// <summary>
/// Colour lists the operator can import and export. Two forms are read: the JSON that <see cref="Serialize"/> writes
/// (<c>.nxcolors</c>), and plain text with one colour per line, <c>Name;#RRGGBB</c> or <c>Name,#RRGGBB</c>
/// (a line starting with # or // is a comment).
/// </summary>
public static class ColorFile
{
    public const string Extension = ".nxcolors";

    private const string FormatName = "nx-dmx-colors";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    internal sealed class ColorData
    {
        public string Name { get; set; } = "";
        public string Hex { get; set; } = "";
    }

    private sealed class FileData
    {
        public string Format { get; set; } = FormatName;
        public List<ColorData> Colors { get; set; } = [];
    }

    internal static List<ColorData> ToData(IEnumerable<ColorPreset> colors) =>
        colors.Select(c => new ColorData { Name = c.Name, Hex = c.Hex }).ToList();

    public static string Serialize(IEnumerable<ColorPreset> colors) =>
        JsonSerializer.Serialize(new FileData { Colors = ToData(colors) }, Options);

    /// <summary>Reads a colour list. Throws <see cref="ShowFileException"/> with a message for the operator when it is not usable.</summary>
    public static List<(string Name, string Hex)> Parse(string text)
    {
        var result = new List<(string, string)>();
        var trimmed = text.TrimStart('﻿', ' ', '\r', '\n', '\t');

        if (trimmed.StartsWith('{'))
        {
            FileData? data;
            try
            {
                data = JsonSerializer.Deserialize<FileData>(trimmed, Options);
            }
            catch (JsonException ex)
            {
                throw new ShowFileException("This is not a readable colour file.", ex);
            }

            if (data is null || data.Format != FormatName)
                throw new ShowFileException("This is not an NX-DMX colour file.");

            foreach (var c in data.Colors ?? [])
                result.Add((c.Name ?? "", c.Hex ?? ""));
        }
        else
        {
            var number = 0;
            foreach (var raw in text.Split('\n'))
            {
                number++;
                var line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//"))
                    continue;

                var split = line.LastIndexOfAny([';', ',', '\t']);
                if (split <= 0)
                    throw new ShowFileException($"Line {number}: expected Name;#RRGGBB");

                result.Add((line[..split].Trim().Trim('"'), line[(split + 1)..].Trim()));
            }
        }

        if (result.Count == 0)
            throw new ShowFileException("The file contains no colours.");
        if (result.Count > 500)
            throw new ShowFileException("The file has more than 500 colours.");

        foreach (var (name, hex) in result)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 40 || name.Contains('"'))
                throw new ShowFileException($"\"{name}\" is not a usable colour name (1 to 40 characters, no quotation marks).");
            if (!ColorPreset.TryParseHex(hex, out _, out _, out _))
                throw new ShowFileException($"\"{name}\": '{hex}' is not a colour. Use six hex digits, e.g. #FF8800.");
        }

        return result;
    }
}
