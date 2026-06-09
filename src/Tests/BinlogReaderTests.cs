using Devlooped.Parsing;

namespace Tests;

public class BinlogReaderTests
{
    static string SuccessFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "build-success.binlog");
    static string FailureFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "build-failure.binlog");

    [Fact]
    public void When_build_succeeds_then_reads_exact_target_outputs()
    {
        var result = BinlogReader.TryReadSuccess(SuccessFixture);

        Assert.NotNull(result);
        Assert.Equal(["md.dll"], result.Outputs);
    }

    [Fact]
    public void When_binlog_missing_then_returns_null()
    {
        Assert.Null(BinlogReader.TryReadSuccess(Path.Combine(AppContext.BaseDirectory, "missing.binlog")));
        Assert.Null(BinlogReader.TryReadFailures(Path.Combine(AppContext.BaseDirectory, "missing.binlog")));
    }

    [Fact]
    public void When_build_fails_then_reads_exact_error_line()
    {
        var result = BinlogReader.TryReadFailures(FailureFixture);

        Assert.NotNull(result);
        Assert.Single(result.Projects);
        Assert.Equal("Broken", result.Projects[0].ProjectName);
        Assert.Single(result.Projects[0].Errors);
        Assert.Matches(@"Broken\.cs:\d+ CS1525: ", result.Projects[0].Errors[0]);
        Assert.DoesNotContain('(', result.Projects[0].Errors[0]);
    }

    [Fact]
    public void When_success_binlog_has_no_errors_then_returns_null()
    {
        Assert.Null(BinlogReader.TryReadFailures(SuccessFixture));
    }

    [Fact]
    public void When_base_directory_provided_then_relativizes_error_paths()
    {
        var absolute = Path.GetFullPath(FailureFixture);
        var baseDir = Path.GetDirectoryName(absolute)!;
        var result = BinlogReader.TryReadFailures(FailureFixture, baseDir);

        Assert.NotNull(result);
        Assert.DoesNotContain(baseDir, result.Projects[0].Errors[0], StringComparison.OrdinalIgnoreCase);
    }
}