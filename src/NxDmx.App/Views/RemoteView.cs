using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NxDmx.App.Services;

namespace NxDmx.App.Views;

/// <summary>
/// Remote control for Bitfocus Companion, Stream Deck and other controllers: HTTP, OSC and a plain-text port. Everything a
/// remote does is a normal command, so any key of the console can be put on a button.
/// </summary>
public sealed class RemoteView : ConsoleView
{
    private readonly CheckBox _http = new() { Content = "HTTP", Margin = new Thickness(0, 0, 0, 4) };
    private readonly CheckBox _osc = new() { Content = "OSC (UDP)", Margin = new Thickness(0, 0, 0, 4) };
    private readonly CheckBox _text = new() { Content = "Text (TCP and UDP)", Margin = new Thickness(0, 0, 0, 4) };
    private readonly TextBox _httpPort = Ui.Input(width: 80);
    private readonly TextBox _oscPort = Ui.Input(width: 80);
    private readonly TextBox _textPort = Ui.Input(width: 80);
    private readonly CheckBox _network = new() { Content = "Allow other computers (Companion on another PC, tablet, …)", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _token = Ui.Input();
    private readonly TextBlock _httpStatus = Ui.Text("", "DimBrush");
    private readonly TextBlock _oscStatus = Ui.Text("", "DimBrush");
    private readonly TextBlock _textStatus = Ui.Text("", "DimBrush");
    private readonly StackPanel _examples = new();
    private readonly ItemsControl _activity = new();
    private bool _loading;

    public RemoteView(ConsoleHost host) : base(host)
    {
        var left = new StackPanel { Margin = new Thickness(18), MaxWidth = 520 };
        left.Children.Add(Ui.Caption("Interfaces"));
        left.Children.Add(Ui.Text("Every request is an ordinary console command, so everything you can type can be put on a Stream Deck button: " +
            "selecting, values, colours, Record, Go, Blackout, Oops.", "DimBrush", margin: new Thickness(0, 0, 0, 14)));

        left.Children.Add(Interface(_http, _httpPort, _httpStatus));
        left.Children.Add(Interface(_osc, _oscPort, _oscStatus));
        left.Children.Add(Interface(_text, _textPort, _textStatus));

        left.Children.Add(_network);
        left.Children.Add(Ui.Field("HTTP token (optional, protects the HTTP interface)", _token));
        left.Children.Add(Ui.Text("Without a token, the HTTP interface still refuses requests from web pages. OSC and the text port have no password: " +
            "only allow other computers on a network you trust.", "MutedBrush", margin: new Thickness(0, 0, 0, 12)));

        var apply = Ui.Button("Apply", Apply, "GreenButton", "Restart the interfaces with these settings");
        apply.Width = 120;
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetAutomationId(apply, "ApplyRemote");
        left.Children.Add(apply);

        left.Children.Add(new Border { Height = 1, Background = Ui.Brush("LineBrush"), Margin = new Thickness(0, 18, 0, 14) });
        left.Children.Add(Ui.Caption("Set up Bitfocus Companion"));
        left.Children.Add(Ui.Text(
            "1. Add the connection \"Generic: HTTP Requests\" and set its base URL to the address below.\n" +
            "2. Give a button a \"GET\" action with the path from the list (for example /api/go/1).\n" +
            "3. For OSC use the connection \"Generic: OSC\" with host and port from above, path /nxdmx/go/1.\n" +
            "4. Buttons that need feedback can read /api/var/cuelist.1.cue, /api/var/blackout and more (see /api/vars).",
            "DimBrush"));

        var right = new StackPanel { Margin = new Thickness(18) };
        right.Children.Add(Ui.Caption("Example buttons"));
        right.Children.Add(_examples);
        right.Children.Add(new Border { Height = 1, Background = Ui.Brush("LineBrush"), Margin = new Thickness(0, 18, 0, 14) });
        right.Children.Add(Ui.Caption("Recent remote commands"));
        right.Children.Add(_activity);

        _activity.ItemTemplate = BuildActivityTemplate();
        _activity.ItemsSource = host.RemoteLog;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560), MinWidth = 400 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    private static UIElement Interface(CheckBox enabled, TextBox port, TextBlock status)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        enabled.VerticalAlignment = VerticalAlignment.Center;
        enabled.Margin = new Thickness(0);
        row.Children.Add(enabled);
        Grid.SetColumn(port, 1);
        row.Children.Add(port);
        status.Margin = new Thickness(12, 0, 0, 0);
        status.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(status, 2);
        row.Children.Add(status);
        return row;
    }

    private DataTemplate BuildActivityTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Display"));
        factory.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
        factory.SetValue(TextBlock.FontSizeProperty, 11.5);
        factory.SetValue(TextBlock.ForegroundProperty, Ui.Brush("DimBrush"));
        factory.SetValue(TextBlock.MarginProperty, new Thickness(0, 1, 0, 1));
        factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        return new DataTemplate { VisualTree = factory };
    }

    public override void Activated()
    {
        _loading = true;
        var s = Host.Settings;
        _http.IsChecked = s.HttpEnabled;
        _osc.IsChecked = s.OscEnabled;
        _text.IsChecked = s.TextEnabled;
        _httpPort.Text = s.HttpPort.ToString();
        _oscPort.Text = s.OscPort.ToString();
        _textPort.Text = s.TextPort.ToString();
        _network.IsChecked = s.RemoteAllowNetwork;
        _token.Text = s.RemoteToken;
        _loading = false;
        Refresh();
    }

    private void Apply()
    {
        if (_loading)
            return;

        var s = Host.Settings;
        s.HttpEnabled = _http.IsChecked == true;
        s.OscEnabled = _osc.IsChecked == true;
        s.TextEnabled = _text.IsChecked == true;
        s.HttpPort = Ui.ToInt(_httpPort.Text) ?? s.HttpPort;
        s.OscPort = Ui.ToInt(_oscPort.Text) ?? s.OscPort;
        s.TextPort = Ui.ToInt(_textPort.Text) ?? s.TextPort;
        s.RemoteAllowNetwork = _network.IsChecked == true;
        s.RemoteToken = _token.Text.Trim();
        Host.ApplyRemote();
        foreach (var status in Host.RemoteStatus)
            Host.AddLog($"Remote {status.Name}: {status.Detail}", error: !status.Running && status.Detail != "Off");

        Refresh();
    }

    public override void Refresh()
    {
        ShowStatus(_httpStatus, "HTTP");
        ShowStatus(_oscStatus, "OSC");
        ShowStatus(_textStatus, "Text");
        BuildExamples();
    }

    private void ShowStatus(TextBlock block, string name)
    {
        var status = Host.RemoteStatus.FirstOrDefault(s => s.Name == name);
        block.Text = status is null ? "" : status.Detail;
        block.Foreground = Ui.Brush(status is null || status.Detail == "Off" ? "MutedBrush" : status.Running ? "GreenBrush" : "RedBrush");
    }

    private void BuildExamples()
    {
        var s = Host.Settings;
        var addresses = new List<string> { "127.0.0.1" };
        if (s.RemoteAllowNetwork)
        {
            addresses.AddRange(ConsoleHost.NetworkAdapters().Where(n => n.Address.Length > 0).Select(n => n.Address));
        }

        _examples.Children.Clear();
        _examples.Children.Add(Ui.Text(
            "Base URL" + (addresses.Count > 1 ? "s" : "") + ": " + string.Join("   ", addresses.Select(a => $"http://{a}:{s.HttpPort}")),
            "AmberBrush", margin: new Thickness(0, 0, 0, 8)));

        (string Action, string Path)[] examples =
        [
            ("GO cuelist 1", "/api/go/1"),
            ("BACK cuelist 1", "/api/back/1"),
            ("OFF cuelist 1", "/api/off/1"),
            ("TOGGLE cuelist 2", "/api/toggle/2"),
            ("Select group 1", "/api/group/1"),
            ("Select fixtures 1 Thru 8", "/api/cmd/Fixture 1 Thru 8"),
            ("Dimmer 100 %", "/api/at/100"),
            ("Color Red", "/api/color/Red"),
            ("Color Warm White", "/api/color/Warm White"),
            ("Gobo 2 on movers", "/api/cmd/Gobo Function \"Gobo 2\""),
            ("Several commands", "/api/cmd/Group 2; Color Blue; At 80"),
            ("Blackout toggle", "/api/blackout"),
            ("Master 70 %", "/api/master/70"),
            ("Clear programmer", "/api/clearall"),
            ("Oops", "/api/oops"),
            ("State as JSON", "/api/state"),
            ("All variables", "/api/vars"),
        ];

        foreach (var (action, path) in examples)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var copy = Ui.Button("Copy", () =>
            {
                try
                {
                    Clipboard.SetText($"http://{addresses[^1]}:{s.HttpPort}{path}");
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    Host.AddLog("The clipboard is in use by another program. Try again.", error: true);
                }
            }, tooltip: "Copy the full URL");
            copy.Padding = new Thickness(8, 2, 8, 2);
            copy.MinHeight = 22;
            DockPanel.SetDock(copy, Dock.Right);
            row.Children.Add(copy);
            row.Children.Add(new TextBlock { Text = action, Width = 190, Foreground = Ui.Brush("TextBrush"), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock
            {
                Text = path, FontFamily = new FontFamily("Consolas"), Foreground = Ui.Brush("DimBrush"), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _examples.Children.Add(row);
        }

        _examples.Children.Add(Ui.Text("OSC: /nxdmx/go 1 · /nxdmx/group 3 · /nxdmx/color Red · /nxdmx/red 100 · /nxdmx/blackout · /nxdmx/master 80 · /nxdmx/cmd \"…\"\n" +
            $"Text port {s.TextPort} (TCP or UDP): one command per line, answered with OK or ERR.", "MutedBrush", margin: new Thickness(0, 8, 0, 0)));
    }
}
