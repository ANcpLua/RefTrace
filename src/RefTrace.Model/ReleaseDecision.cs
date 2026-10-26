namespace RefTrace.Core;

public sealed record KeepRelease(string Version);
public sealed record UpgradeRelease(string Version, bool IsPrerelease);

public readonly union ReleaseDecision(KeepRelease, UpgradeRelease)
{
    public string Version => this switch
    {
        KeepRelease current => current.Version,
        UpgradeRelease upgrade => upgrade.Version,
        null => throw new InvalidOperationException("Release decision was not initialized.")
    };

    public string Reason => this switch
    {
        KeepRelease => "current",
        UpgradeRelease { IsPrerelease: true } => "newer-prerelease",
        UpgradeRelease => "newer-stable",
        null => throw new InvalidOperationException("Release decision was not initialized.")
    };
}
