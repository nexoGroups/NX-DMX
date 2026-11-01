using Velopack;

namespace NxDmx.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: when the installer or updater starts this exe with its hook arguments, it handles them and exits.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
