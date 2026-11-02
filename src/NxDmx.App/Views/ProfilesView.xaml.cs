using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NxDmx.App.Services;
using NxDmx.Core.Fixtures;
using NxDmx.Core.Storage;

namespace NxDmx.App.Views;

/// <summary>
/// The Fixture Manager: an editor for fixture profiles. A profile says how many DMX channels a fixture uses, which
/// attribute each channel controls (with an optional fine channel for 16 bit), the default of each channel, and the
/// named functions with their DMX ranges. Profiles are saved inside the show and can be exported to share them.
/// </summary>
public partial class ProfilesView : ConsoleView
{
    private sealed record ProfileItem(string Name, string Info, FixtureType Type)
    {
        /// <summary>The name in the list: built-in profiles carry the mark (PreBuild).</summary>
        public string Display => Type.DisplayName;
    }

    private const string FileFilter = "NX-DMX fixture profile (*.nxfixture)|*.nxfixture|All files (*.*)|*.*";

    private ProfileDraft? _draft;
    private bool _dirty;
    private bool _loading;
    private string _signature = "";
    private DraftChannel? _channel;

    public ProfilesView(ConsoleHost host) : base(host)
    {
        InitializeComponent();
    }

    public override void Refresh()
    {
        var snapshot = Host.Snapshot;
        var items = snapshot.Profiles
            .Select(p => new ProfileItem(
                p.Type.Name,
                $"{(p.Type.IsBuiltIn ? "built-in" : p.Type.Manufacturer.Length > 0 ? p.Type.Manufacturer : "custom")} · {p.Type.Footprint} ch{(p.UsedBy > 0 ? $" · {p.UsedBy} patched" : "")}",
                p.Type))
            .ToList();

        var signature = string.Join('|', items.Select(i => $"{i.Name}:{i.Info}:{i.Type.Channels.Count}"));
        if (signature == _signature)
            return;

        _signature = signature;
        var keep = _draft?.OriginalName;
        _loading = true;
        ProfileList.ItemsSource = items;
        ProfileList.SelectedItem = items.FirstOrDefault(i => i.Name == keep);
        _loading = false;
        UpdateButtons();
    }

    // ---- list ------------------------------------------------------------------------------------------------

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileList.SelectedItem is not ProfileItem item)
        {
            UpdateButtons();
            return;
        }

        if (!ConfirmDiscard())
        {
            _loading = true;
            ProfileList.SelectedItem = (ProfileList.ItemsSource as IEnumerable<ProfileItem>)?.FirstOrDefault(i => i.Name == _draft?.OriginalName);
            _loading = false;
            return;
        }

        Load(ProfileDraft.From(item.Type, asCopy: false), dirty: false);
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty || _draft is not { CanEdit: true })
            return true;

        return MessageBox.Show(Window.GetWindow(this), $"Discard the changes to \"{_draft.Name}\"?", "Fixture Manager",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void UpdateButtons()
    {
        var item = ProfileList.SelectedItem as ProfileItem;
        DeleteButton.IsEnabled = item is { Type.IsBuiltIn: false };
        DeleteButton.ToolTip = item is { Type.IsBuiltIn: true } ? "Built-in profiles cannot be deleted" : "Delete the selected profile";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard())
            return;

        _loading = true;
        ProfileList.SelectedItem = null;
        _loading = false;
        Load(ProfileDraft.Blank(), dirty: true);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var source = ProfileList.SelectedItem as ProfileItem;
        if (source is null && _draft is null)
            return;

        if (!ConfirmDiscard())
            return;

        var copy = source is not null ? ProfileDraft.From(source.Type, asCopy: true) : ProfileDraft.Blank();
        _loading = true;
        ProfileList.SelectedItem = null;
        _loading = false;
        Load(copy, dirty: true);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProfileItem { Type.IsBuiltIn: false } item)
            return;

        if (!Services.ShowDialogs.ConfirmDelete(Host, Window.GetWindow(this)!, $"Delete the profile \"{item.Name}\"?\n\nOops can take this back.", "Fixture Manager"))
            return;

        var result = Host.Console.DeleteProfile(item.Name);
        Host.AddLog(result.Message, error: !result.Success);
        if (result.Success)
        {
            _draft = null;
            Editor.Visibility = Visibility.Collapsed;
            EmptyNote.Visibility = Visibility.Visible;
            _dirty = false;
        }
        else
        {
            Problems.Text = result.Message;
            Problems.Foreground = Ui.Brush("RedBrush");
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = FileFilter, Title = "Import fixture profile" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            var type = ProfileFile.Deserialize(ShowStorage.Read(dialog.FileName));
            var names = Host.Snapshot.Profiles.Select(p => p.Type.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = type.Name;
            for (var n = 2; names.Contains(name); n++)
                name = $"{type.Name} ({n})";

            if (name != type.Name)
                type = new FixtureType(name, type.Channels, type.Manufacturer, type.Footprint);

            var result = Host.Console.SaveProfile(null, type);
            Host.AddLog(result.Success ? $"Imported profile \"{name}\"" : result.Message, error: !result.Success);
            if (result.Success)
                SelectByName(name);
        }
        catch (Exception ex) when (ex is ShowFileException or IOException or UnauthorizedAccessException)
        {
            Host.AddLog($"Could not import {Path.GetFileName(dialog.FileName)}: {ex.Message}", error: true);
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Import profile", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ProfileItem item)
        {
            Host.AddLog("Export: choose a saved profile first.", error: true);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = FileFilter,
            DefaultExt = ProfileFile.Extension,
            AddExtension = true,
            FileName = item.Name,
            Title = "Export fixture profile",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            ShowStorage.Write(dialog.FileName, ProfileFile.Serialize(item.Type));
            Host.AddLog($"Exported \"{item.Name}\" to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Host.AddLog($"Could not export: {ex.Message}", error: true);
        }
    }

    private void SelectByName(string name)
    {
        _signature = "";
        Refresh();
        if ((ProfileList.ItemsSource as IEnumerable<ProfileItem>)?.FirstOrDefault(i => i.Name == name) is { } item)
            ProfileList.SelectedItem = item;
    }

    // ---- editor ----------------------------------------------------------------------------------------------

    private void Load(ProfileDraft draft, bool dirty)
    {
        _draft = draft;
        _dirty = false;
        Editor.DataContext = draft;
        Editor.Visibility = Visibility.Visible;
        EmptyNote.Visibility = Visibility.Collapsed;
        ReadOnlyNote.Visibility = draft.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
        draft.Edited += () =>
        {
            _dirty = true;
            Validate();
        };

        ChannelGrid.SelectedItem = draft.Channels.FirstOrDefault(c => c.IsUsed) ?? draft.Channels.FirstOrDefault();
        ShowFunctions();
        _dirty = dirty;
        Validate();
    }

    private void Validate()
    {
        if (_draft is null)
            return;

        var (type, problems) = _draft.Build();
        if (type is null)
        {
            Problems.Text = string.Join("\n", problems.Take(4).Select(p => "• " + p)) + (problems.Count > 4 ? $"\n• … and {problems.Count - 4} more" : "");
            Problems.Foreground = Ui.Brush("RedBrush");
        }
        else
        {
            var functions = type.Channels.Sum(c => c.FunctionList.Count);
            Problems.Text = $"Valid · {type.Footprint} channels, {type.Channels.Count} attributes{(functions > 0 ? $", {functions} functions" : "")}"
                + (_draft.IsBuiltIn ? "" : _dirty ? "   (not saved yet)" : "");
            Problems.Foreground = Ui.Brush(_dirty ? "AmberBrush" : "GreenBrush");
        }

        SaveButton.IsEnabled = type is not null && _dirty && _draft.CanEdit;
        RevertButton.IsEnabled = _dirty && _draft.CanEdit;
        AddFunctionButton.IsEnabled = _draft.CanEdit && _channel is { IsUsed: true };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null)
            return;

        var (type, problems) = _draft.Build();
        if (type is null)
            return;

        var result = Host.Console.SaveProfile(_draft.OriginalName, type);
        Host.AddLog(result.Message, error: !result.Success);
        if (!result.Success)
        {
            Problems.Text = "• " + result.Message;
            Problems.Foreground = Ui.Brush("RedBrush");
            return;
        }

        Load(ProfileDraft.From(type, asCopy: false), dirty: false);
        SelectByName(type.Name);
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is null)
            return;

        var original = Host.Snapshot.Profiles.FirstOrDefault(p => p.Type.Name == _draft.OriginalName)?.Type;
        if (original is not null)
            Load(ProfileDraft.From(original, asCopy: false), dirty: false);
        else
            Load(ProfileDraft.Blank(), dirty: true);
    }

    private void MoreChannels_Click(object sender, RoutedEventArgs e) => StepChannels(+1);

    private void FewerChannels_Click(object sender, RoutedEventArgs e) => StepChannels(-1);

    private void StepChannels(int delta)
    {
        if (_draft is not { CanEdit: true })
            return;

        var count = int.TryParse(_draft.ChannelCountText, out var current) ? current : _draft.Channels.Count;
        _draft.ChannelCountText = Math.Clamp(count + delta, 1, FixtureType.MaxChannels).ToString();
    }

    // ---- functions -------------------------------------------------------------------------------------------

    private void ChannelGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_channel is not null)
            _channel.PropertyChanged -= OnChannelChanged;

        _channel = ChannelGrid.SelectedItem as DraftChannel;
        if (_channel is not null)
            _channel.PropertyChanged += OnChannelChanged;

        ShowFunctions();
        Validate();
    }

    private void OnChannelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DraftChannel.Attribute))
        {
            ShowFunctions();
            Validate();
        }
    }

    private void ShowFunctions()
    {
        FunctionList.ItemsSource = _channel?.Functions;
        if (_channel is null)
        {
            FunctionTitle.Text = "Select a channel";
            return;
        }

        FunctionTitle.Text = _channel.IsUsed
            ? $"Channel {_channel.Offset} · {_channel.Attribute}"
            : $"Channel {_channel.Offset} is not used";
    }

    private void AddFunction_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is not { CanEdit: true } || _channel is not { IsUsed: true })
            return;

        _draft.NewFunction(_channel);
    }

    private void RemoveFunction_Click(object sender, RoutedEventArgs e)
    {
        if (_draft is not { CanEdit: true } || _channel is null || ((FrameworkElement)sender).Tag is not DraftFunction function)
            return;

        _channel.Functions.Remove(function);
        _channel.FunctionsChanged();
    }
}
