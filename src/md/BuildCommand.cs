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
                var success = BinlogReader.TryReadSuccess(plan.Path);
                var failures = BinlogReader.TryReadFailures(plan.Path);
                bool hasSuccess = success is { Outputs.Count: > 0 };
                bool hasFailures = failures is { Projects.Count: > 0 };

                if (hasSuccess || hasFailures)
                {
                    IReadOnlyDictionary<string, IReadOnlyList<string>>? combosToPass = null;
                    if (hasFailures && hasSuccess && success!.Combinations is { Count: > 0 })
                    {
                        static string Logical(string s) => (s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ? s[..^4] : s;
                        var failBases = failures!.Projects
                            .Select(p => Logical(p.ProjectName))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var filtered = success.Combinations
                            .Where(kv => failBases.Contains(Logical(kv.Key)))
                            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                        if (filtered.Count > 0)
                            combosToPass = filtered;
                    }

                    IReadOnlyList<BuildProjectErrors> projDtos = Array.Empty<BuildProjectErrors>();
                    if (hasFailures)
                    {
                        IReadOnlyList<string>? GetFailCombs(BuildProjectFailure p)
                        {
                            if (!hasSuccess || success!.Combinations is not { Count: > 0 } || p.Combinations.Count == 0)
                                return null;
                            static string Logical(string s) => (s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ? s[..^4] : s;
                            var successBases = success.Outputs
                                .Select(o => Logical(o))
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
                            if (successBases.Contains(Logical(p.ProjectName)))
                                return p.Combinations;
                            return null;
                        }

                        projDtos = failures!.Projects
                            .Select(p => new BuildProjectErrors(p.ProjectName, p.Errors, GetFailCombs(p)))
                            .ToArray();
                    }

                    MarkdownWriter.WriteBuild(
                        hasSuccess ? success!.Outputs : Array.Empty<string>(),
                        combosToPass,
                        projDtos);
                    wroteMarkdown = true;
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