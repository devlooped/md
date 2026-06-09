using Devlooped.Formatting;
using Devlooped.Parsing;

namespace Devlooped;

static class TestCommand
{
    public static async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        if (HelpDetector.IsHelpRequest(args))
            return await DotnetRunner.RunPassthroughAsync("test", args, cancellationToken);

        var dotnetArgs = new List<string>(args);
        var plan = DotnetTestArguments.Prepare(dotnetArgs);

        if (plan.ErrorMessage is not null)
        {
            Console.Error.WriteLine(plan.ErrorMessage);
            return 1;
        }

        try
        {
            var result = await DotnetRunner.RunCapturedAsync("test", dotnetArgs, cancellationToken);
            var summary = new TrxReader().Read(plan.ResultsDirectory);
            var wroteMarkdown = false;

            if (result.ExitCode == 0)
            {
                if (summary.Assemblies.Count > 0)
                {
                    MarkdownWriter.WriteTestSuccess(summary.Assemblies
                        .Select(a => new TestAssemblyResult(a.AssemblyName, a.Passed, a.Failed, a.Skipped))
                        .ToArray());
                    wroteMarkdown = true;
                }
            }
            else if (summary.Assemblies.Count > 0 || summary.Failures.Count > 0)
            {
                MarkdownWriter.WriteTestFailures(
                    summary.Assemblies.Select(a => new TestAssemblyResult(a.AssemblyName, a.Passed, a.Failed, a.Skipped)).ToArray(),
                    summary.Failures.Select(f => new TestFailure(f.FullName, f.Message, f.StackTrace)).ToArray());
                wroteMarkdown = true;
            }

            CommandOutput.Finish(wroteMarkdown, result.ExitCode, result, MarkdownWriter.WriteTestFallback);

            return result.ExitCode;
        }
        finally
        {
            if (plan.ToolCreatedResultsDirectory)
                TryDeleteDirectory(plan.ResultsDirectory);
        }
    }

    static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}