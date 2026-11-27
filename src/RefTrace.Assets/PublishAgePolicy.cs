using System.Globalization;
using NuGet.Configuration;

namespace RefTrace.Core;

// NuGet 8.0 minimum publish age for one package source, read through NuGet.Configuration.
// NuGet.Configuration supplies the configured hours and exception patterns. The eligibility rule
// itself (a version qualifies once the minimum has elapsed since its publish date) lives in NuGet's
// package-selection commands, which RefTrace does not reference; this class applies that documented
// rule to the one source the review queries and to nothing else.
public sealed class PublishAgePolicy
{
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    private readonly MinPublishAgeExceptions _exceptions;

    private PublishAgePolicy(PublishAgeSetting setting, MinPublishAgeExceptions exceptions)
    {
        Setting = setting;
        _exceptions = exceptions;
    }

    public PublishAgeSetting Setting { get; }

    public static PublishAgePolicy Load(string root, string source = NuGetOrg)
    {
        try
        {
            return Load(Settings.LoadDefaultSettings(root), source);
        }
        catch (NuGetConfigurationException error)
        {
            throw new InspectionException("invalid-artifact", "The NuGet configuration could not be read: " + error.Message, root);
        }
    }

    public static PublishAgePolicy Load(ISettings settings, string source = NuGetOrg)
    {
        // NuGet evaluates each source on its own, so the smallest minimum among enabled entries for
        // the same feed URL is the one that can make a version eligible.
        var configured = PackageSourceProvider.LoadPackageSources(settings)
            .Where(item => item.IsEnabled && string.Equals(item.Source.TrimEnd('/'), source.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            .MinBy(item => item.MinPublishAge);
        var items = new MinPublishAgeExceptionsProvider(settings).GetMinPublishAgeExceptionItems();
        // NuGet accepts whole hours up to TimeSpan.MaxValue (256204778), which fits an int.
        return new(new PublishAgeSetting(source, configured?.Name, (int)(configured?.MinPublishAge.TotalHours ?? 0),
                items.Select(item => item.Pattern).ToArray(), settings.GetConfigFilePaths().ToArray()),
            new MinPublishAgeExceptions(items));
    }

    public PublishAgeCheck Check(string packageId, string? published, DateTimeOffset collectedAt)
    {
        if (Setting.SourceName is null) return new("source-not-configured", published, null, 0, null);
        if (_exceptions.FindException(packageId) is { } exception)
            return new("excepted", published, null, Setting.MinimumHours, exception.Pattern);
        if (Setting.MinimumHours == 0) return new("no-minimum", published, null, 0, null);
        if (!DateTimeOffset.TryParse(published, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return new("publish-date-missing", published, null, Setting.MinimumHours, null);
        var age = (collectedAt - date).TotalHours;
        return new(age < Setting.MinimumHours ? "younger-than-minimum" : "old-enough", published,
            Math.Round(age, 1, MidpointRounding.ToZero), Setting.MinimumHours, null);
    }
}
