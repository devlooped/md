using Devlooped.Parsing;

namespace Tests;

public class TrxReaderTests
{
    static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "trx");

    [Fact]
    public void When_trx_has_results_then_reads_exact_counts_and_failure_details()
    {
        var summary = new TrxReader().Read(FixtureDirectory);

        Assert.Single(summary.Assemblies);
        Assert.Equal("MyCompany.MyApp.Tests.dll", summary.Assemblies[0].AssemblyName);
        Assert.Equal(2, summary.Assemblies[0].Passed);
        Assert.Equal(1, summary.Assemblies[0].Failed);
        Assert.Equal(1, summary.Assemblies[0].Skipped);

        Assert.Single(summary.Failures);
        Assert.Equal("MyCompany.MyApp.Tests.UnitTests.Fails", summary.Failures[0].FullName);
        Assert.Equal("Assert.True() Failure", summary.Failures[0].Message);
        Assert.Contains("UnitTests.Fails()", summary.Failures[0].StackTrace);
    }

    [Fact]
    public void When_directory_missing_then_returns_empty_summary()
    {
        var summary = new TrxReader().Read(Path.Combine(AppContext.BaseDirectory, "missing-results"));

        Assert.Empty(summary.Assemblies);
        Assert.Empty(summary.Failures);
    }

    [Fact]
    public void When_directory_empty_then_returns_empty_summary()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"md-trx-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var summary = new TrxReader().Read(dir);
            Assert.Empty(summary.Assemblies);
            Assert.Empty(summary.Failures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void When_duplicate_test_ids_then_keeps_first_seen_outcome()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"md-trx-dedup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var trxPath = Path.Combine(dir, "dup.trx");
        File.Copy(Path.Combine(FixtureDirectory, "sample.trx"), trxPath);
        File.Copy(trxPath, Path.Combine(dir, "dup2.trx"), overwrite: true);

        try
        {
            var summary = new TrxReader().Read(dir);
            Assert.Equal(2, summary.Assemblies[0].Passed);
            Assert.Equal(1, summary.Assemblies[0].Failed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void When_trx_in_subdirectory_then_finds_results()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"md-trx-subdir-{Guid.NewGuid():N}");
        var sub = Path.Combine(dir, "nested");
        Directory.CreateDirectory(sub);
        File.Copy(Path.Combine(FixtureDirectory, "sample.trx"), Path.Combine(sub, "sample.trx"));

        try
        {
            var summary = new TrxReader().Read(dir);
            Assert.Single(summary.Assemblies);
            Assert.Equal(2, summary.Assemblies[0].Passed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}