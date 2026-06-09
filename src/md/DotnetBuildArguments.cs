namespace Devlooped;

public static class DotnetBuildArguments
{
    public sealed record BinlogPlan(string Path, bool ToolCreated);

    public static bool HasBinlogArgument(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (IsBinlogToken(args[i]) || IsBareBinlogSwitch(args[i]))
                return true;
        }

        return false;
    }

    public static string? ResolveBinlogPath(IReadOnlyList<string> args, string? workingDirectory = null)
    {
        workingDirectory ??= Directory.GetCurrentDirectory();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            if (TryGetBinlogPathFromToken(arg, out var path))
                return Path.GetFullPath(path, workingDirectory);

            if (IsBareBinlogSwitch(arg))
            {
                if (i + 1 < args.Count && !IsSwitch(args[i + 1]))
                    return Path.GetFullPath(args[i + 1], workingDirectory);

                return Path.Combine(workingDirectory, "msbuild.binlog");
            }
        }

        return null;
    }

    public static BinlogPlan Prepare(List<string> args, string? workingDirectory = null)
    {
        var existing = ResolveBinlogPath(args, workingDirectory);
        if (existing is not null)
            return new BinlogPlan(existing, ToolCreated: false);

        var temp = Path.Combine(Path.GetTempPath(), $"md-{Guid.NewGuid():N}.binlog");
        args.Add($"-bl:{temp}");
        return new BinlogPlan(temp, ToolCreated: true);
    }

    static bool IsBinlogToken(string arg)
        => arg.StartsWith("-bl:", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("/bl:", StringComparison.OrdinalIgnoreCase);

    static bool IsBareBinlogSwitch(string arg)
        => arg.Equals("-bl", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("/bl", StringComparison.OrdinalIgnoreCase);

    static bool TryGetBinlogPathFromToken(string arg, out string path)
    {
        if (arg.StartsWith("-bl:", StringComparison.OrdinalIgnoreCase))
        {
            path = arg["-bl:".Length..];
            return true;
        }

        if (arg.StartsWith("/bl:", StringComparison.OrdinalIgnoreCase))
        {
            path = arg["/bl:".Length..];
            return true;
        }

        path = string.Empty;
        return false;
    }

    static bool IsSwitch(string arg) => arg.StartsWith('-') || arg.StartsWith('/');
}