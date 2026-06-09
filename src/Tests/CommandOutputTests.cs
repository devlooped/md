using CliWrap.Buffered;
using Devlooped;
using System.Text;

namespace Tests;

public class CommandOutputTests
{
    static BufferedCommandResult Result(int exitCode, string stdout, string stderr)
    {
        var now = DateTimeOffset.UtcNow;
        return new BufferedCommandResult(exitCode, now, now, stdout, stderr);
    }

    [Fact]
    public void When_exit_nonzero_and_no_markdown_then_writes_fallback()
    {
        var writer = new StringBuilder();
        CommandOutput.Finish(
            wroteMarkdown: false,
            exitCode: 1,
            result: Result(1, "stdout", "stderr"),
            () => writer.AppendLine("fallback"));

        Assert.Equal("fallback" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void When_exit_zero_and_no_markdown_then_replays_captured_output()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            CommandOutput.Finish(
                wroteMarkdown: false,
                exitCode: 0,
                result: Result(0, "hello", "warn"),
                () => throw new InvalidOperationException("fallback should not run"));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        Assert.Equal("hello", stdout.ToString());
        Assert.Equal("warn", stderr.ToString());
    }

    [Fact]
    public void When_markdown_written_then_skips_replay_and_fallback()
    {
        var writer = new StringBuilder();
        CommandOutput.Finish(
            wroteMarkdown: true,
            exitCode: 0,
            result: Result(0, "hello", ""),
            () => writer.AppendLine("fallback"));

        Assert.Empty(writer.ToString());
    }
}