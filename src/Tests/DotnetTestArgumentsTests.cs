using Devlooped;

namespace Tests;

public class DotnetTestArgumentsTests
{
    [Fact]
    public void When_no_results_directory_then_injects_temp_and_trx()
    {
        var args = new List<string> { "--no-build" };
        var plan = DotnetTestArguments.Prepare(args);

        Assert.True(plan.ToolCreatedResultsDirectory);
        Assert.Equal("trx", args[1]);
        Assert.Equal("--logger", args[0]);
        Assert.Equal("--results-directory", args[2]);
        Assert.Equal(plan.ResultsDirectory, args[3]);
    }

    [Fact]
    public void When_user_results_directory_then_does_not_mark_tool_created()
    {
        var args = new List<string> { "--results-directory", @"C:\results" };
        var plan = DotnetTestArguments.Prepare(args);

        Assert.False(plan.ToolCreatedResultsDirectory);
        Assert.Equal(@"C:\results", plan.ResultsDirectory);
    }

    [Fact]
    public void When_colon_form_results_directory_then_preserves_user_path()
    {
        var args = new List<string> { "--results-directory:C:\\results" };
        var plan = DotnetTestArguments.Prepare(args);

        Assert.False(plan.ToolCreatedResultsDirectory);
        Assert.Equal(@"C:\results", plan.ResultsDirectory);
        Assert.Equal("trx", args[1]);
    }

    [Fact]
    public void When_only_results_directory_provided_then_injects_trx_logger()
    {
        var args = new List<string> { "--results-directory", @"C:\results" };
        DotnetTestArguments.Prepare(args);

        Assert.Equal("trx", args[1]);
        Assert.Equal("--logger", args[0]);
    }

    [Fact]
    public void When_combined_console_and_trx_loggers_then_allows()
    {
        var args = new List<string> { "--logger", "console;verbosity=normal", "--logger", "trx" };
        var plan = DotnetTestArguments.Prepare(args);

        Assert.Null(plan.ErrorMessage);
    }

    [Fact]
    public void When_non_trx_logger_then_returns_error()
    {
        var args = new List<string> { "--logger", "console;verbosity=normal" };
        var plan = DotnetTestArguments.Prepare(args);

        Assert.NotNull(plan.ErrorMessage);
        Assert.Contains("TRX", plan.ErrorMessage);
    }

    [Theory]
    [InlineData(new[] { "--logger", "trx" }, true)]
    [InlineData(new[] { "--logger:trx" }, true)]
    [InlineData(new[] { "/logger:trx" }, true)]
    [InlineData(new[] { "-l", "trx" }, true)]
    [InlineData(new[] { "--logger", "console" }, false)]
    public void HasTrxLogger_detects_logger_forms(string[] args, bool expected)
    {
        Assert.Equal(expected, DotnetTestArguments.HasTrxLogger(args));
    }

    [Theory]
    [InlineData("trx", true)]
    [InlineData("trx;LogFileName=test.trx", true)]
    [InlineData("trxite", false)]
    [InlineData("console", false)]
    public void IsTrxLoggerValue_matches_trx_only(string value, bool expected)
    {
        Assert.Equal(expected, DotnetTestArguments.IsTrxLoggerValue(value));
    }
}