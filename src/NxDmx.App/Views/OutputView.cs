using System.Windows;
using System.Windows.Controls;
using NxDmx.App.Services;
using NxDmx.Core.Dmx;

namespace NxDmx.App.Views;

/// <summary>Art-Net output: the network adapter, the default destination and what is currently being sent where.</summary>
public sealed class OutputView : ConsoleView
{
    private readonly ComboBox _adapter = new();
    private readonly TextBox _destination = Ui.Input();
    private readonly CheckBox _enabled = new() { Content = "Send DMX (Art-Net output on)", Margin = new Thickness(0, 0, 0, 14) };
    private readonly TextBlock _status = Ui.Text("", "DimBrush");
    private readonly TextBlock _details = Ui.Text("", "DimBrush");
    private readonly StackPanel _routes = new();
    private bool _loading;
    private string _routeSignature = "";

    public OutputView(ConsoleHost host) : base(host)
    {
        var form = new StackPanel { Margin = new Thickness(18), MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        form.Children.Add(Ui.Caption("Art-Net output"));
        form.Children.Add(Ui.Text("NX-DMX sends every universe as Art-Net to the nodes on your network. Choose the network adapter the nodes are connected to; " +
            "each universe can then go out by broadcast or straight to one node (see Universes).", "DimBrush", margin: new Thickness(0, 0, 0, 14)));

        _enabled.Click += (_, _) => Host.SetOutputEnabled(_enabled.IsChecked == true);
        System.Windows.Automation.AutomationProperties.SetAutomationId(_enabled, "OutputEnabled");
        form.Children.Add(_enabled);

        form.Children.Add(Ui.Field("Network adapter to send from", _adapter));
        _adapter.ItemsSource = ConsoleHost.NetworkAdapters();
        _adapter.DisplayMemberPath = nameof(NetworkChoice.Text);

        form.Children.Add(Ui.Field("Default destination (universes set to Broadcast go here)", _destination));
        var presets = new WrapPanel { Margin = new Thickness(0, -4, 0, 14) };
        presets.Children.Add(Preset("255.255.255.255", "Limited broadcast: every node on the local network"));
        presets.Children.Add(Preset("2.255.255.255", "Art-Net 2.x.x.x network"));
        presets.Children.Add(Preset("10.255.255.255", "Art-Net 10.x.x.x network"));
        var subnet = Ui.Button("Subnet of adapter", () =>
        {
            if (_adapter.SelectedItem is NetworkChoice { Broadcast.Length: > 0 } choice)
                _destination.Text = choice.Broadcast;
            else
                Host.AddLog("Choose a network adapter first to use its subnet broadcast address.", error: true);
        }, tooltip: "The broadcast address of the chosen adapter's subnet, e.g. 192.168.1.255");
        subnet.Margin = new Thickness(0, 0, 4, 4);
        presets.Children.Add(subnet);
        form.Children.Add(presets);

        var apply = Ui.Button("Apply", Apply, "GreenButton", "Open the socket with these settings");
        apply.Width = 120;
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        System.Windows.Automation.AutomationProperties.SetAutomationId(apply, "ApplyOutput");
        form.Children.Add(apply);

        form.Children.Add(new Border { Height = 1, Background = Ui.Brush("LineBrush"), Margin = new Thickness(0, 18, 0, 14) });
        form.Children.Add(Ui.Caption("Status"));
        _status.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        _status.FontSize = 14;
        form.Children.Add(_status);
        form.Children.Add(_details);

        var routes = new StackPanel { Margin = new Thickness(18) };
        routes.Children.Add(Ui.Caption("What is sent where"));
        _routes.Margin = new Thickness(0, 4, 0, 0);
        routes.Children.Add(_routes);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 600 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(form);
        Grid.SetColumn(routes, 1);
        grid.Children.Add(routes);
        Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    private Button Preset(string address, string tooltip)
    {
        var button = Ui.Button(address, () => _destination.Text = address, tooltip: tooltip);
        button.Margin = new Thickness(0, 0, 4, 4);
        return button;
    }

    public override void Activated()
    {
        _loading = true;
        _adapter.ItemsSource = ConsoleHost.NetworkAdapters();
        _adapter.SelectedItem = ((IEnumerable<NetworkChoice>)_adapter.ItemsSource).FirstOrDefault(c => c.Address == Host.Settings.LocalAddress)
            ?? ((IEnumerable<NetworkChoice>)_adapter.ItemsSource).First();
        _destination.Text = Host.Settings.BroadcastAddress;
        _enabled.IsChecked = Host.Settings.OutputEnabled;
        _loading = false;
        Refresh();
        Tick();
    }

    private void Apply()
    {
        if (_loading)
            return;

        Host.Settings.LocalAddress = (_adapter.SelectedItem as NetworkChoice)?.Address ?? "";
        Host.Settings.BroadcastAddress = _destination.Text.Trim();
        Host.ApplyOutput();
        Host.AddLog(Host.OutputSetupError is null
            ? $"Art-Net output set: {(Host.Settings.LocalAddress.Length == 0 ? "automatic adapter" : Host.Settings.LocalAddress)} → {Host.Settings.BroadcastAddress}"
            : Host.OutputSetupError, error: Host.OutputSetupError is not null);
        Tick();
    }

    public override void Refresh()
    {
        var universes = Host.Snapshot.Universes;
        var signature = string.Join('|', universes.Select(u => $"{u.Number}:{u.Label}:{u.Enabled}:{u.Destination}:{u.UnicastAddress}:{u.FixtureCount}"))
            + "#" + Host.Settings.BroadcastAddress;
        if (signature == _routeSignature)
            return;

        _routeSignature = signature;
        _routes.Children.Clear();
        if (universes.Count == 0)
            _routes.Children.Add(Ui.Text("No universes yet. Patch a fixture or create a universe.", "MutedBrush"));

        foreach (var universe in universes)
        {
            var target = universe.Destination == UniverseDestination.Unicast ? $"node {universe.UnicastAddress}" : $"broadcast {Host.Settings.BroadcastAddress}";
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            row.Children.Add(new TextBlock
            {
                Text = universe.Number.ToString(), Width = 44, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontWeight = FontWeights.Bold,
                Foreground = Ui.Brush("TextBrush"),
            });
            row.Children.Add(new TextBlock
            {
                Text = $"{universe.Label} → {(universe.Enabled ? target : "off")}",
                Foreground = Ui.Brush(universe.Enabled ? "TextBrush" : "MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            _routes.Children.Add(row);
        }
    }

    public override void Tick()
    {
        _status.Text = Host.OutputStatus;
        _status.Foreground = Ui.Brush(Host.Health switch
        {
            OutputHealth.Sending => "GreenBrush",
            OutputHealth.Error => "RedBrush",
            _ => "MutedBrush",
        });

        var sent = Host.Engine.FramesSent;
        _details.Text = $"{sent:N0} frames sent · 40 Hz · UDP port 6454";
    }
}
