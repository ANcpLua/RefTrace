namespace RefTrace.Core;

public sealed record PackageReleaseReport(string SourcePath, string Current,
    string? LatestStable, string? LatestPrerelease, string Selected, string Selection);
