namespace Devlooped;

public static class DotnetTestArguments
{
    public sealed record TestPlan(string ResultsDirectory, bool ToolCreatedResultsDirectory, string? ErrorMessage = null);

    public static TestPlan Prepare(List<string> args)
    {
        if (TryGetExplicitNonTrxLogger(args, out var logger))
            return new TestPlan(string.Empty, ToolCreatedResultsDirectory: false,
                ErrorMessage: $"md only supports TRX test output; remove logger '{logger}' or use '--logger trx'.");

        var path = FindResultsDirectory(args);
        var toolCreated = false;

        if (path is null)
        {
            path = Path.Combine(Path.GetTempPath(), $"md-{Guid.NewGuid():N}");
            args.Insert(0, path);
            args.Insert(0, "--results-directory");
            toolCreated = true;
        }

        if (!HasTrxLogger(args))
        {
            args.Insert(0, "trx");
            args.Insert(0, "--logger");
        }

        return new TestPlan(path, toolCreated);
    }

    public static string? FindResultsDirectory(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (TryGetValue(arg, "--results-directory:", out var inline))
                return inline;

            if (TryGetValue(arg, "-results-directory:", out inline))
                return inline;

            if (TryGetValue(arg, "/results-directory:", out inline))
                return inline;

            if (IsResultsDirectorySwitch(arg) && i + 1 < args.Count)
                return args[i + 1];
        }

        return null;
    }

    public static bool HasTrxLogger(IReadOnlyList<string> args)
        => TryGetLoggerValues(args).Any(IsTrxLoggerValue);

    public static bool TryGetExplicitNonTrxLogger(IReadOnlyList<string> args, out string logger)
    {
        var values = TryGetLoggerValues(args).ToList();
        if (values.Any(IsTrxLoggerValue))
        {
            logger = string.Empty;
            return false;
        }

        var nonTrx = values.FirstOrDefault(value => !IsTrxLoggerValue(value));
        if (nonTrx is not null)
        {
            logger = nonTrx;
            return true;
        }

        logger = string.Empty;
        return false;
    }

    static IEnumerable<string> TryGetLoggerValues(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (TryGetValue(arg, "--logger:", out var inline))
            {
                yield return inline;
                continue;
            }

            if (TryGetValue(arg, "-logger:", out inline))
            {
                yield return inline;
                continue;
            }

            if (TryGetValue(arg, "/logger:", out inline))
            {
                yield return inline;
                continue;
            }

            if (IsLoggerSwitch(arg) && i + 1 < args.Count)
            {
                yield return args[i + 1];
                i++;
            }
        }
    }

    public static bool IsTrxLoggerValue(string value)
        => value.Equals("trx", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("trx;", StringComparison.OrdinalIgnoreCase);

    static bool IsLoggerSwitch(string arg)
        => arg.Equals("--logger", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("-l", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("/logger", StringComparison.OrdinalIgnoreCase);

    static bool IsResultsDirectorySwitch(string arg)
        => arg.Equals("--results-directory", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("-results-directory", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("/results-directory", StringComparison.OrdinalIgnoreCase);

    static bool TryGetValue(string arg, string prefix, out string value)
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..];
            return true;
        }

        value = string.Empty;
        return false;
    }
}