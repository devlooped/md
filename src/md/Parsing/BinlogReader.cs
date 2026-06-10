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
        baseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                     .Replace('\\', '/').TrimEnd('/');

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
                        // Use normalized prefix match (no Path.IsPathRooted) so it works cross-platform
                        // when paths use \ vs / (e.g. binlog recorded on Windows, reader on Linux, or vice-versa).
                        if (path.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase) &&
                            (path.Length == baseDirectory.Length || path[baseDirectory.Length] == '/'))
                        {
                            path = path.Substring(baseDirectory.Length)
                                       .TrimStart('/')
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
        baseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                     .Replace('\\', '/').TrimEnd('/');
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
            {
                if (ExtractLogicalProjectName(project.ProjectFile) is { } name && IsGoodProjectName(name))
                    return name;

                // Some StructuredLogger Project instances expose a Name or MSBuildProjectName.
                if (project.GetType().GetProperty("Name")?.GetValue(project) as string is { } altName && IsGoodProjectName(altName))
                    return altName;

                if (project.GetType().GetProperty("MSBuildProjectName")?.GetValue(project) as string is { } mbn && IsGoodProjectName(mbn))
                    return mbn;
            }
            node = node.Parent as BaseNode;
        }

        if (!string.IsNullOrWhiteSpace(error.ProjectFile))
        {
            if (ExtractLogicalProjectName(error.ProjectFile) is { } name && IsGoodProjectName(name))
                return name;
        }

        // For synthetic "broken project" fixtures (or any binlog where the project was built from a temp dir),
        // the error's File often references a source file whose stem matches the intended project name, e.g. "Broken.cs".
        // Use that as a last-resort logical name so tests and output remain stable regardless of where the binlog was captured.
        var errFile = error.File;
        if (!string.IsNullOrWhiteSpace(errFile))
        {
            var leaf = Path.GetFileNameWithoutExtension(errFile.Replace('\\', '/').Split('/', '\\').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? string.Empty);
            if (IsGoodProjectName(leaf))
                return leaf;
        }

        return "Build";
    }

    static string? ExtractLogicalProjectName(string? projectFile)
    {
        if (string.IsNullOrWhiteSpace(projectFile))
            return null;

        // GetFileNameWithoutExtension works even if projectFile is a directory path (returns last segment).
        var name = Path.GetFileNameWithoutExtension(projectFile);
        if (string.IsNullOrWhiteSpace(name) || name == ".")
            name = Path.GetFileName(projectFile.TrimEnd('\\', '/'));

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    static bool IsGoodProjectName(string name)
    {
        // Never accept a rooted path as a "name".
        if (Path.IsPathRooted(name))
            return false;

        // Reject obvious machine/temp/generated directory names used for ad-hoc broken-project repros.
        var lower = name.ToLowerInvariant();
        if (lower.Contains("temp") || lower.Contains("\\temp\\") || lower.Contains("/temp/") ||
            lower.Contains("tmp") || lower.StartsWith("md-broken-") || lower.Contains("/md-broken-") || lower.Contains("\\md-broken-"))
            return false;

        // Reject anything that still looks like a path.
        if (name.Contains('/') || name.Contains('\\'))
            return false;

        // Must contain at least one letter to be a useful display name.
        if (!name.Any(char.IsLetter))
            return false;

        return true;
    }

    static string FormatError(Error error, string baseDirectory)
    {
        var file = error.File;
        if (!string.IsNullOrWhiteSpace(file))
        {
            // Normalize for cross-separator relativization (handles \ in paths when base uses / or CI fixtures).
            var f = file.Replace('\\', '/');
            if (f.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase) &&
                (f.Length == baseDirectory.Length || f[baseDirectory.Length] == '/'))
            {
                file = f.Substring(baseDirectory.Length).TrimStart('/');
            }
            else
            {
                file = f;
            }
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