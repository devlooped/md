using Devlooped.Formatting;
using Devlooped.Parsing;

namespace Devlooped;

static class BuildCommand
{
    public static async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        if (HelpDetector.IsHelpRequest(args))
            return await DotnetRunner.RunPassthroughAsync("build", args, cancellationToken);

        var dotnetArgs = new List<string>(args);
        var plan = DotnetBuildArguments.Prepare(dotnetArgs);

        try
        {
            var result = await DotnetRunner.RunCapturedAsync("build", dotnetArgs, cancellationToken);
            var wroteMarkdown = false;

            if (File.Exists(plan.Path))
            {
                if (result.ExitCode == 0)
                {
                    var success = BinlogReader.TryReadSuccess(plan.Path);
                    if (success is { Outputs.Count: > 0 })
                    {
                        MarkdownWriter.WriteBuildSuccess(success.Outputs);
                        wroteMarkdown = true;
                    }
                }
                else
                {
                    var failures = BinlogReader.TryReadFailures(plan.Path);
                    if (failures is { Projects.Count: > 0 })
                    {
                        MarkdownWriter.WriteBuildFailures(failures.Projects
                            .Select(p => new BuildProjectErrors(p.ProjectName, p.Errors))
                            .ToArray());
                        wroteMarkdown = true;
                    }
                }
            }

            CommandOutput.Finish(wroteMarkdown, result.ExitCode, result, MarkdownWriter.WriteBuildFallback);

            return result.ExitCode;
        }
        finally
        {
            if (plan.ToolCreated)
                TryDelete(plan.Path);
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}