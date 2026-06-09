using CliWrap;
using CliWrap.Buffered;

namespace Devlooped;

static class DotnetRunner
{
    public static async Task<int> RunPassthroughAsync(string dotnetVerb, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var dotnet = DotnetMuxer.Path?.FullName ?? "dotnet";
        var finalArgs = new List<string> { dotnetVerb };
        finalArgs.AddRange(args);

        var result = await Cli.Wrap(dotnet)
            .WithArguments(finalArgs)
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToStream(Console.OpenStandardOutput()))
            .WithStandardErrorPipe(PipeTarget.ToStream(Console.OpenStandardError()))
            .ExecuteAsync(cancellationToken);

        return result.ExitCode;
    }

    public static async Task<BufferedCommandResult> RunCapturedAsync(string dotnetVerb, IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        var dotnet = DotnetMuxer.Path?.FullName ?? "dotnet";
        var finalArgs = new List<string> { dotnetVerb };
        finalArgs.AddRange(args);

        return await Cli.Wrap(dotnet)
            .WithArguments(finalArgs)
            .WithWorkingDirectory(Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);
    }

    public static void ReplayCapturedOutput(BufferedCommandResult result)
    {
        if (!string.IsNullOrEmpty(result.StandardOutput))
            Console.Out.Write(result.StandardOutput);

        if (!string.IsNullOrEmpty(result.StandardError))
            Console.Error.Write(result.StandardError);
    }
}