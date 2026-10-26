using System.Text.Json;
using NuGet.Versioning;

namespace RefTrace.Core;

public static class ReleaseCatalog
{
    public static PackageReleaseReport Read(string path, string current)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, Path.GetFullPath(path), current);
    }

    public static PackageReleaseReport Read(Stream stream, string sourcePath, string current)
    {
        if (!NuGetVersion.TryParse(current, out var installed))
            throw new InspectionException("The current package version is not a NuGet version.");
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("versions", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InspectionException("Expected a NuGet flat-container index with a versions array.");
        var versions = new List<NuGetVersion>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !NuGetVersion.TryParse(item.GetString(), out var version))
                throw new InspectionException("The package index contains an invalid NuGet version.");
            versions.Add(version);
        }
        var ordered = versions.Distinct(VersionComparer.VersionRelease)
            .OrderBy(version => version, VersionComparer.VersionRelease).ToArray();
        var stable = ordered.LastOrDefault(version => !version.IsPrerelease);
        var preview = ordered.LastOrDefault(version => version.IsPrerelease);
        var latest = ordered.LastOrDefault();
        ReleaseDecision decision = latest is not null && VersionComparer.VersionRelease.Compare(latest, installed) > 0
            ? new UpgradeRelease(latest.ToNormalizedString(), latest.IsPrerelease)
            : new KeepRelease(installed.ToNormalizedString());
        return new(sourcePath, installed.ToNormalizedString(), stable?.ToNormalizedString(),
            preview?.ToNormalizedString(), decision.Version, decision.Reason);
    }
}
