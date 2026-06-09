using Devlooped;

namespace Tests;

public class HelpDetectorTests
{
    [Theory]
    [InlineData(new[] { "--help" }, true)]
    [InlineData(new[] { "--HELP" }, true)]
    [InlineData(new[] { "-help" }, true)]
    [InlineData(new[] { "-h" }, true)]
    [InlineData(new[] { "-H" }, true)]
    [InlineData(new[] { "-?" }, true)]
    [InlineData(new[] { "--configuration", "Release" }, false)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--configuration", "Release", "--help" }, true)]
    public void Detects_help_requests(string[] args, bool expected)
    {
        Assert.Equal(expected, HelpDetector.IsHelpRequest(args));
    }

    [Theory]
    [InlineData("build", true)]
    [InlineData("BUILD", true)]
    [InlineData("test", true)]
    [InlineData("TEST", true)]
    [InlineData("publish", false)]
    public void IsSubcommand_is_case_insensitive(string arg, bool expected)
    {
        Assert.Equal(expected, HelpDetector.IsSubcommand(arg));
    }
}