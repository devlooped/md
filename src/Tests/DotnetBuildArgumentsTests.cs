using Devlooped;

namespace Tests;

public class DotnetBuildArgumentsTests
{
    [Fact]
    public void When_no_binlog_then_injects_temp_path()
    {
        var args = new List<string> { "--no-restore" };
        var plan = DotnetBuildArguments.Prepare(args);

        Assert.True(plan.ToolCreated);
        Assert.Single(args, a => a.StartsWith("-bl:", StringComparison.Ordinal));
        Assert.Equal(plan.Path, args.Single(a => a.StartsWith("-bl:", StringComparison.Ordinal))["-bl:".Length..]);
    }

    [Theory]
    [InlineData(new[] { "-bl:custom.binlog" }, "custom.binlog")]
    [InlineData(new[] { "/bl:custom.binlog" }, "custom.binlog")]
    [InlineData(new[] { "-bl", "custom.binlog" }, "custom.binlog")]
    [InlineData(new[] { "-bl" }, "msbuild.binlog")]
    public void When_user_binlog_then_resolves_without_injection(string[] input, string expectedLeaf)
    {
        var args = new List<string>(input);
        var workDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var plan = DotnetBuildArguments.Prepare(args, workDir);

        Assert.False(plan.ToolCreated);
        Assert.EndsWith(expectedLeaf, plan.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(input.Length, args.Count);
    }

    [Theory]
    [InlineData(new[] { "-bl:foo.binlog" }, true)]
    [InlineData(new[] { "-bl" }, true)]
    [InlineData(new[] { "--configuration", "Release" }, false)]
    public void HasBinlogArgument_detects_binlog_switches(string[] args, bool expected)
    {
        Assert.Equal(expected, DotnetBuildArguments.HasBinlogArgument(args));
    }
}