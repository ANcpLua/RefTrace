using System.Text.Json;
using NuGet.Configuration;
using RefTrace.Cli;
using RefTrace.Core;

namespace RefTrace.Tests;

[TestClass]
public sealed class PublishAgePolicyTests(TestContext context)
{
    private static readonly DateTimeOffset CollectedAt = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private PublishAgePolicy Load(string configuration)
    {
        var directory = Path.Combine(context.TestTempDirectory!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "nuget.config"), configuration);
        return PublishAgePolicy.Load(Settings.LoadSpecificSettings(directory, "nuget.config"));
    }

    private const string Configured = """
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" minPublishAgeHours="72" />
            <add key="Contoso" value="https://contoso.test/nuget/v3/index.json" />
          </packageSources>
          <minPublishAgeExceptions>
            <package pattern="Contoso.*" />
          </minPublishAgeExceptions>
        </configuration>
        """;

    [TestMethod]
    public void Load_ReadsTheQueriedSourcesMinimumAndExceptions()
    {
        var setting = Load(Configured).Setting;
        Assert.AreEqual("nuget.org", setting.SourceName);
        Assert.AreEqual(72, setting.MinimumHours);
        CollectionAssert.AreEqual(new[] { "Contoso.*" }, setting.ExceptionPatterns.ToArray());
        Assert.HasCount(1, setting.ConfigFiles);
    }

    [TestMethod]
    [DataRow("2026-11-22T12:00:00+00:00", "younger-than-minimum", 24.0)]
    [DataRow("2026-11-20T12:00:00+00:00", "old-enough", 72.0)]
    [DataRow("2026-11-10T22:44:01.41+00:00", "old-enough", 301.2)]
    public void Check_ComparesTheRegistrationPublishDateWithTheMinimum(string published, string result, double age)
    {
        var check = Load(Configured).Check("Example", published, CollectedAt);
        Assert.AreEqual(result, check.Result);
        Assert.AreEqual(age, check.AgeHours);
        Assert.AreEqual(72, check.MinimumHours);
    }

    [TestMethod]
    public void Check_ExceptionsAndMissingDatesAreNamedNotGuessed()
    {
        var policy = Load(Configured);
        var excepted = policy.Check("contoso.library", "2026-11-23T11:00:00+00:00", CollectedAt);
        Assert.AreEqual("excepted", excepted.Result);
        Assert.AreEqual("Contoso.*", excepted.ExceptionPattern);
        Assert.IsNull(excepted.AgeHours);
        Assert.AreEqual("publish-date-missing", policy.Check("Example", null, CollectedAt).Result);
    }

    [TestMethod]
    public void Check_WithoutAMinimumOrWithoutTheSource_SaysSo()
    {
        var unset = Load("""
            <configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
            """);
        Assert.AreEqual("no-minimum", unset.Check("Example", "2026-11-23T11:00:00+00:00", CollectedAt).Result);
        var absent = Load("""
            <configuration><packageSources><clear /><add key="Contoso" value="https://contoso.test/nuget/v3/index.json" minPublishAgeHours="24" /></packageSources></configuration>
            """);
        Assert.IsNull(absent.Setting.SourceName);
        Assert.AreEqual("source-not-configured", absent.Check("Example", "2026-11-23T11:00:00+00:00", CollectedAt).Result);
        StringAssert.Contains(CliApplication.DescribeMinimumPublishAge(absent.Setting), "not an enabled package source");
    }

    [TestMethod]
    public void Summarize_ChecksOnlyNewerCandidatesAndCountsYoungOnes()
    {
        using var report = JsonDocument.Parse("""
            {
              "collectedAt": "2026-11-23T12:00:00+00:00",
              "packages": [
                { "package": "Young", "current": "1.0.0", "direct": true, "status": "catalogued",
                  "catalog": { "selected": "2.0.0", "selection": "stable" }, "selectedPublished": "2026-11-23T00:00:00+00:00" },
                { "package": "Settled", "current": "1.0.0", "direct": false, "status": "catalogued",
                  "catalog": { "selected": "1.0.0", "selection": "current" } }
              ]
            }
            """);
        var summary = CliApplication.Summarize("/repo", "/repo/report.json", "/repo/review", report.RootElement, Load(Configured));
        Assert.AreEqual(1, summary.YoungerThanMinimumCount);
        Assert.AreEqual(12.0, summary.Packages.Single(p => p.Package == "Young").PublishAge!.AgeHours);
        Assert.IsNull(summary.Packages.Single(p => p.Package == "Settled").PublishAge);
        Assert.AreEqual(72, summary.MinimumPublishAge.MinimumHours);
    }
}
