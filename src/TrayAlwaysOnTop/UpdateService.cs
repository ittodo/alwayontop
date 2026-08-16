using Velopack;
using Velopack.Sources;

namespace TrayAlwaysOnTop;

internal sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/ittodo/alwayontop";

    public async Task<UpdateCheckResult> CheckAndDownloadAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manager = CreateManager();
        if (!manager.IsInstalled)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NotInstalled, null);
        }

        var pendingUpdate = manager.UpdatePendingRestart;
        if (pendingUpdate is not null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.ReadyToRestart, pendingUpdate.Version.ToString());
        }

        var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate, manager.CurrentVersion?.ToString());
        }

        await manager.DownloadUpdatesAsync(update, progress, cancellationToken).ConfigureAwait(false);
        return new UpdateCheckResult(UpdateCheckStatus.ReadyToRestart, update.TargetFullRelease.Version.ToString());
    }

    public bool ApplyPendingUpdateAndRestart()
    {
        var manager = CreateManager();
        var update = manager.UpdatePendingRestart;
        if (!manager.IsInstalled || update is null)
        {
            return false;
        }

        manager.ApplyUpdatesAndRestart(update);
        return true;
    }

    private static UpdateManager CreateManager() => new(
        new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
}

internal enum UpdateCheckStatus
{
    NotInstalled,
    UpToDate,
    ReadyToRestart
}

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, string? Version);
