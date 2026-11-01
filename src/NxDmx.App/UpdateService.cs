using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace NxDmx.App;

/// <summary>
/// Finds, downloads and installs new releases from GitHub through Velopack. Updating only works in a copy that was
/// installed with the Setup.exe of a release; a build started from the source tree reports <see cref="IsInstalled"/> false.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/nexoGroups/NX-DMX";

    /// <summary>Points the updater at another feed (a folder or URL), for testing a release before publishing it.</summary>
    public const string SourceOverrideVariable = "NXDMX_UPDATE_SOURCE";

    private readonly UpdateManager _manager;

    public UpdateService()
    {
        var source = Environment.GetEnvironmentVariable(SourceOverrideVariable);
        _manager = string.IsNullOrWhiteSpace(source)
            ? new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false))
            : new UpdateManager(source);
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? BuildVersion();

    /// <summary>Null when this version is the newest one.</summary>
    public Task<UpdateInfo?> CheckAsync() => _manager.CheckForUpdatesAsync();

    public Task DownloadAsync(UpdateInfo update, Action<int> progress) => _manager.DownloadUpdatesAsync(update, progress);

    /// <summary>Closes the app, installs the downloaded update and starts the new version.</summary>
    public void ApplyAndRestart(UpdateInfo update) => _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);

    /// <summary>Installs the downloaded update after this process has ended, without starting it again.</summary>
    public void ApplyOnExit(UpdateInfo update) =>
        _manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);

    private static string BuildVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Strip the "+<commit>" suffix the SDK appends.
        return informational?.Split('+')[0] ?? "dev";
    }
}
