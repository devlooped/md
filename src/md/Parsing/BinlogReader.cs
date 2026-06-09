using Microsoft.Build.Logging.StructuredLogger;

namespace Devlooped.Parsing;

static class BinlogReader
{
    public static BuildSuccessResult? TryReadSuccess(string binlogPath)
    {
        if (!File.Exists(binlogPath))
            return null;

        var build = Serialization.Read(binlogPath);
        var outputs = new List<string>();

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

                        outputs.Add(Path.GetFileName(path));
                    });
                }
            });
        });

        return new BuildSuccessResult(outputs.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public static BuildFailureResult? TryReadFailures(string binlogPath, string? baseDirectory = null)
    {
        if (!File.Exists(binlogPath))
            return null;

        baseDirectory ??= Directory.GetCurrentDirectory();
        var build = Serialization.Read(binlogPath);
        var errorsByProject = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        build.VisitAllChildren<Error>(error =>
        {
            var project = FindProjectName(error) ?? "Build";
            if (!errorsByProject.TryGetValue(project, out var errors))
            {
                errors = [];
                errorsByProject[project] = errors;
            }

            errors.Add(FormatError(error, baseDirectory));
        });

        if (errorsByProject.Count == 0)
            return null;

        var projects = errorsByProject
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new BuildProjectFailure(x.Key, x.Value))
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
        if (!string.IsNullOrWhiteSpace(file) && Path.IsPathRooted(file) && file.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase))
            file = file[baseDirectory.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var line = error.LineNumber > 0 ? $":{error.LineNumber}" : string.Empty;
        var code = string.IsNullOrWhiteSpace(error.Code) ? string.Empty : $"{error.Code}: ";
        var text = string.IsNullOrWhiteSpace(error.Text) ? error.Title : error.Text;

        return $"{file}{line} {code}{text}".Trim();
    }
}

record BuildSuccessResult(IReadOnlyList<string> Outputs);

record BuildFailureResult(IReadOnlyList<BuildProjectFailure> Projects);

record BuildProjectFailure(string ProjectName, IReadOnlyList<string> Errors);