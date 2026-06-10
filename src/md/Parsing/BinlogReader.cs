using Microsoft.Build.Logging.StructuredLogger;

namespace Devlooped.Parsing;

static class BinlogReader
{
    public static BuildSuccessResult? TryReadSuccess(string binlogPath)
    {
        if (!File.Exists(binlogPath))
            return null;

        var build = Serialization.Read(binlogPath);

        // Prefer SolutionDir recorded in the binlog (set when building from a .sln).
        // Fall back to the process current directory so that output paths are always relative
        // (shorter, and what an LLM expects when it later wants to open files).
        var baseDirectory = GetSolutionDirFromBinlog(build) ?? Directory.GetCurrentDirectory();
        baseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var outputs = new List<string>();
        var combosByOutput = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        build.VisitAllChildren<Project>(project =>
        {
            project.VisitAllChildren<Target>(target =>
            {
                if (target.Name != "GetTargetPath" || !target.Succeeded)
                    return;

                foreach (var child in target.Children)
                {
                    if (child is not Folder { Name: "TargetOutputs" } outputsFolder)
                        continue;

                    outputsFolder.VisitAllChildren<Item>(item =>
                    {
                        var path = item.Name;
                        if (string.IsNullOrWhiteSpace(path))
                            return;

                        // Normalize separators.
                        path = path.Replace('\\', '/');

                        // Make relative to SolutionDir (preferred) or cwd. This is the key
                        // improvement requested: success paths in the markdown should not be
                        // absolute machine-specific paths.
                        if (Path.IsPathRooted(path) &&
                            path.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            path = path.Substring(baseDirectory.Length)
                                       .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                       .Replace('\\', '/');
                        }

                        outputs.Add(path);

                        var combo = GetCombo(project);
                        if (combo is not null)
                        {
                            if (!combosByOutput.TryGetValue(path, out var set))
                            {
                                set = new HashSet<string>(StringComparer.Ordinal);
                                combosByOutput[path] = set;
                            }
                            set.Add(combo);
                        }
                    });
                }
            });
        });

        var distinct = outputs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        IReadOnlyDictionary<string, IReadOnlyList<string>>? combos = null;
        if (combosByOutput.Count > 0)
        {
            combos = combosByOutput.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
        return new BuildSuccessResult(distinct, combos);
    }

    static string? GetSolutionDirFromBinlog(Build build)
    {
        string? solutionDir = null;

        build.VisitAllChildren<Project>(project =>
        {
            if (solutionDir is not null)
                return;

            // Some versions of the StructuredLogger model expose SolutionDir directly.
            var sd = project.GetType().GetProperty("SolutionDir")?.GetValue(project) as string;
            if (!string.IsNullOrWhiteSpace(sd))
            {
                solutionDir = sd;
                return;
            }

            // Most common: SolutionDir lives in GlobalProperties when the build was driven by a .sln.
            if (project.GetType().GetProperty("GlobalProperties")?.GetValue(project) is System.Collections.IDictionary gp &&
                gp.Contains("SolutionDir"))
            {
                var val = gp["SolutionDir"]?.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                    solutionDir = val;
            }
        });

        return solutionDir;
    }

    public static BuildFailureResult? TryReadFailures(string binlogPath, string? baseDirectory = null)
    {
        if (!File.Exists(binlogPath))
            return null;

        baseDirectory ??= Directory.GetCurrentDirectory();
        var build = Serialization.Read(binlogPath);
        var dataByProject = new Dictionary<string, (HashSet<string> Errors, HashSet<string> Combos)>(StringComparer.OrdinalIgnoreCase);

        build.VisitAllChildren<Error>(error =>
        {
            var project = FindProjectName(error) ?? "Build";
            if (!dataByProject.TryGetValue(project, out var data))
            {
                data = (new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
                dataByProject[project] = data;
            }

            data.Errors.Add(FormatError(error, baseDirectory));

            // Find closest Project ancestor to extract TFM/RID combo for this error
            var node = error.Parent as BaseNode;
            Project? proj = null;
            while (node is not null && proj is null)
            {
                if (node is Project p) proj = p;
                node = node.Parent as BaseNode;
            }
            var combo = proj is not null ? GetCombo(proj) : null;
            if (combo is not null)
                data.Combos.Add(combo);
        });

        if (dataByProject.Count == 0)
            return null;

        var projects = dataByProject
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new BuildProjectFailure(
                x.Key,
                x.Value.Errors.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray(),
                x.Value.Combos.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();

        return new BuildFailureResult(projects);
    }

    static string FindProjectName(Error error)
    {
        var node = error.Parent as BaseNode;
        while (node is not null)
        {
            if (node is Project project)
                return Path.GetFileNameWithoutExtension(project.ProjectFile);

            node = node.Parent as BaseNode;
        }

        if (!string.IsNullOrWhiteSpace(error.ProjectFile))
            return Path.GetFileNameWithoutExtension(error.ProjectFile);

        return "Build";
    }

    static string FormatError(Error error, string baseDirectory)
    {
        var file = error.File;
        if (!string.IsNullOrWhiteSpace(file))
        {
            if (Path.IsPathRooted(file) && file.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
                file = file[baseDirectory.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            file = file.Replace('\\', '/');
        }

        var line = error.LineNumber > 0 ? $":{error.LineNumber}" : string.Empty;
        var code = string.IsNullOrWhiteSpace(error.Code) ? string.Empty : $"{error.Code}: ";
        var text = string.IsNullOrWhiteSpace(error.Text) ? error.Title : error.Text;

        return $"{file}{line} {code}{text}".Trim();
    }

    static string? GetCombo(Project project)
    {
        var tfm = project.TargetFramework?.Trim();
        if (string.IsNullOrEmpty(tfm))
            return null;

        // Prefer RuntimeIdentifier (direct property or in GlobalProperties)
        string? rid = project.GetType().GetProperty("RuntimeIdentifier")?.GetValue(project) as string;
        if (string.IsNullOrWhiteSpace(rid))
        {
            try
            {
                if (project.GetType().GetProperty("GlobalProperties")?.GetValue(project) is System.Collections.IDictionary gp && gp.Contains("RuntimeIdentifier"))
                    rid = gp["RuntimeIdentifier"]?.ToString();
            }
            catch { }
        }
        rid = rid?.Trim();
        if (!string.IsNullOrEmpty(rid))
            return $"{tfm}|{rid}";

        var plat = project.Platform?.Trim();
        if (!string.IsNullOrEmpty(plat) && !string.Equals(plat, "AnyCPU", StringComparison.OrdinalIgnoreCase))
            return $"{tfm}|{plat}";

        return tfm;
    }
}

// Outputs contain the TargetOutputs paths from the binlog, relativized against SolutionDir
// (if present in the binlog) or the current directory at read time. Separators are normalized to '/'.
// This keeps success markdown short and independent of the machine's absolute paths.
// Combinations (when present) are keyed by the same relative paths.
record BuildSuccessResult(IReadOnlyList<string> Outputs, IReadOnlyDictionary<string, IReadOnlyList<string>>? Combinations = null);

record BuildFailureResult(IReadOnlyList<BuildProjectFailure> Projects);

record BuildProjectFailure(string ProjectName, IReadOnlyList<string> Errors, IReadOnlyList<string> Combinations);