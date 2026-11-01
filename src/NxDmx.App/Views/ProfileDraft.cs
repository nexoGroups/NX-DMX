using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using NxDmx.Core.Fixtures;

namespace NxDmx.App.Views;

/// <summary>Base for the editable models: raises PropertyChanged and a common Edited event for the validation.</summary>
public abstract class Editable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? Edited;

    protected void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        Edited?.Invoke();
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void RaiseEdited() => Edited?.Invoke();
}

/// <summary>A named stretch of a channel's DMX range, as typed.</summary>
public sealed class DraftFunction : Editable
{
    private string _name = "";
    private string _from = "0";
    private string _to = "255";

    public string Name { get => _name; set => Set(ref _name, value, nameof(Name)); }

    public string From
    {
        get => _from;
        set
        {
            Set(ref _from, value, nameof(From));
            Raise(nameof(Percent));
        }
    }

    public string To
    {
        get => _to;
        set
        {
            Set(ref _to, value, nameof(To));
            Raise(nameof(Percent));
        }
    }

    /// <summary>The range as percent, to relate it to what the faders show.</summary>
    public string Percent =>
        int.TryParse(_from, out var from) && int.TryParse(_to, out var to) && from is >= 0 and <= 255 && to is >= 0 and <= 255
            ? $"{Math.Round(from / 2.55):0}–{Math.Round(to / 2.55):0} %"
            : "";
}

/// <summary>One DMX channel row of the profile being edited.</summary>
public sealed class DraftChannel : Editable
{
    public const string Unused = "—";

    private string _attribute = Unused;
    private string _fine = Unused;
    private string _default = "0";
    private string _fineOf = "";

    public DraftChannel(int offset) => Offset = offset;

    public int Offset { get; }

    public string Attribute
    {
        get => _attribute;
        set
        {
            Set(ref _attribute, value, nameof(Attribute));
            Raise(nameof(IsUsed));
            Raise(nameof(FunctionSummary));
        }
    }

    public string Fine { get => _fine; set => Set(ref _fine, value, nameof(Fine)); }

    /// <summary>Default value in percent: what the channel outputs while nothing sets it.</summary>
    public string DefaultText { get => _default; set => Set(ref _default, value, nameof(DefaultText)); }

    public string FineOf
    {
        get => _fineOf;
        set
        {
            _fineOf = value;
            Raise(nameof(FineOf));
        }
    }

    public bool IsUsed => _attribute != Unused;

    public ObservableCollection<DraftFunction> Functions { get; } = [];

    public string FunctionSummary => !IsUsed ? "" : Functions.Count == 0 ? "add…" : $"{Functions.Count} function{(Functions.Count == 1 ? "" : "s")}";

    public void FunctionsChanged()
    {
        Raise(nameof(FunctionSummary));
        RaiseEdited();
    }
}

/// <summary>The profile being edited in the Fixture Manager. Turns into a <see cref="FixtureType"/> when it is valid.</summary>
public sealed class ProfileDraft : Editable
{
    private string _name = "";
    private string _manufacturer = "";
    private string _channelCount = "1";

    public string? OriginalName { get; init; }

    public bool IsBuiltIn { get; init; }

    public bool CanEdit => !IsBuiltIn;

    public string Name { get => _name; set => Set(ref _name, value, nameof(Name)); }

    public string Manufacturer { get => _manufacturer; set => Set(ref _manufacturer, value, nameof(Manufacturer)); }

    public string ChannelCountText
    {
        get => _channelCount;
        set
        {
            Set(ref _channelCount, value, nameof(ChannelCountText));
            if (int.TryParse(value, out var count) && count is >= 1 and <= FixtureType.MaxChannels)
                Resize(count);
        }
    }

    public ObservableCollection<DraftChannel> Channels { get; } = [];

    /// <summary>"—" and the numbers of all channels, for choosing the fine channel of a 16-bit attribute.</summary>
    public ObservableCollection<string> FineChoices { get; } = [DraftChannel.Unused];

    public static IReadOnlyList<string> AttributeChoices { get; } =
        [DraftChannel.Unused, .. Enum.GetValues<FixtureAttribute>().Select(a => a.ToString())];

    public static ProfileDraft Blank()
    {
        var draft = new ProfileDraft { Name = "New fixture" };
        draft.Resize(1);
        return draft;
    }

    public static ProfileDraft From(FixtureType type, bool asCopy)
    {
        var draft = new ProfileDraft
        {
            OriginalName = asCopy ? null : type.Name,
            IsBuiltIn = type.IsBuiltIn && !asCopy,
        };
        draft._name = asCopy ? type.Name + " copy" : type.Name;
        draft._manufacturer = type.Manufacturer;
        draft._channelCount = type.Footprint.ToString();
        draft.Resize(type.Footprint);

        foreach (var channel in type.Channels)
        {
            var row = draft.Channels[channel.Offset - 1];
            row.Attribute = channel.Attribute.ToString();
            row.Fine = channel.FineOffset?.ToString() ?? DraftChannel.Unused;
            row.DefaultText = Math.Round(channel.Default * 100, 2).ToString(CultureInfo.InvariantCulture);
            foreach (var function in channel.FunctionList)
                row.Functions.Add(Track(draft, new DraftFunction { Name = function.Name, From = function.From.ToString(), To = function.To.ToString() }));
        }

        draft.Recompute();
        return draft;
    }

    private static DraftFunction Track(ProfileDraft draft, DraftFunction function)
    {
        function.Edited += draft.RaiseEdited;
        return function;
    }

    public DraftFunction NewFunction(DraftChannel channel)
    {
        var last = channel.Functions.Select(f => int.TryParse(f.To, out var to) ? to : -1).DefaultIfEmpty(-1).Max();
        var from = Math.Min(255, last + 1);
        var function = Track(this, new DraftFunction { Name = $"Function {channel.Functions.Count + 1}", From = from.ToString(), To = Math.Min(255, from + 9).ToString() });
        channel.Functions.Add(function);
        channel.FunctionsChanged();
        return function;
    }

    private void Resize(int count)
    {
        while (Channels.Count > count)
            Channels.RemoveAt(Channels.Count - 1);

        while (Channels.Count < count)
        {
            var row = new DraftChannel(Channels.Count + 1);
            row.Edited += () =>
            {
                Recompute();
                RaiseEdited();
            };
            Channels.Add(row);
        }

        FineChoices.Clear();
        FineChoices.Add(DraftChannel.Unused);
        for (var i = 1; i <= count; i++)
            FineChoices.Add(i.ToString());

        Recompute();
        RaiseEdited();
    }

    /// <summary>Marks the channels that are the fine half of another attribute.</summary>
    private void Recompute()
    {
        foreach (var row in Channels)
            row.FineOf = "";

        foreach (var row in Channels.Where(r => r.IsUsed))
        {
            if (int.TryParse(row.Fine, out var fine) && fine >= 1 && fine <= Channels.Count && fine != row.Offset)
                Channels[fine - 1].FineOf = $"fine of {row.Attribute}";
        }
    }

    /// <summary>Builds the profile. Returns the problems when the input is not usable; the profile is then null.</summary>
    public (FixtureType? Type, List<string> Problems) Build()
    {
        var problems = new List<string>();
        if (!int.TryParse(_channelCount, out var count))
        {
            problems.Add("The channel count must be a whole number.");
            return (null, problems);
        }

        var channels = new List<ChannelDefinition>();
        foreach (var row in Channels.Where(r => r.IsUsed))
        {
            if (!Enum.TryParse<FixtureAttribute>(row.Attribute, out var attribute))
            {
                problems.Add($"Channel {row.Offset}: unknown attribute.");
                continue;
            }

            int? fine = null;
            if (row.Fine != DraftChannel.Unused)
            {
                if (int.TryParse(row.Fine, out var parsedFine))
                    fine = parsedFine;
                else
                    problems.Add($"Channel {row.Offset}: the fine channel is not a number.");
            }

            if (!double.TryParse(row.DefaultText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                problems.Add($"Channel {row.Offset} ({attribute}): the default must be a number from 0 to 100.");
                percent = 0;
            }

            var functions = new List<ChannelFunction>();
            foreach (var function in row.Functions)
            {
                if (int.TryParse(function.From, out var from) && int.TryParse(function.To, out var to))
                    functions.Add(new ChannelFunction(function.Name.Trim(), from, to));
                else
                    problems.Add($"Channel {row.Offset} ({attribute}): the range of \"{function.Name}\" needs two whole numbers.");
            }

            channels.Add(new ChannelDefinition(attribute, row.Offset, fine, percent / 100, functions));
        }

        problems.AddRange(FixtureType.Validate(_name, channels, count));
        if (problems.Count > 0)
            return (null, problems.Distinct().ToList());

        return (new FixtureType(_name.Trim(), channels, _manufacturer.Trim(), count), problems);
    }
}

/// <summary>Shows an element only while the bound value is true.</summary>
public sealed class BoolToVisibility : System.Windows.Data.IValueConverter
{
    public static BoolToVisibility Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Hidden;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
