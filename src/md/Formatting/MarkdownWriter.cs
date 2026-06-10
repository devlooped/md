using System.Text;

namespace Devlooped.Formatting;

static class MarkdownWriter
{
    const char Tab = '\t';

    public static void WriteBuildSuccess(IReadOnlyList<string> outputs, IReadOnlyDictionary<string, IReadOnlyList<string>>? combinations = null)
        => WriteBuild(outputs, combinations);

    public static void WriteBuildSuccess(TextWriter writer, IReadOnlyList<string> outputs, IReadOnlyDictionary<string, IReadOnlyList<string>>? combinations = null)
        => WriteBuild(writer, outputs, combinations);

    public static void WriteBuildFailures(IReadOnlyList<BuildProjectErrors> projects)
        => WriteBuild([], null, projects);

    public static void WriteBuildFailures(TextWriter writer, IReadOnlyList<BuildProjectErrors> projects)
        => WriteBuild(writer, [], null, projects);

    public static void WriteBuildFallback()
        => WriteBuildFallback(Console.Out);

    public static void WriteBuildFallback(TextWriter writer)
        => writer.WriteLine("❌Build");

    public static void WriteBuild(IReadOnlyList<string> outputs, IReadOnlyDictionary<string, IReadOnlyList<string>>? combinations = null, IReadOnlyList<BuildProjectErrors>? failures = null)
        => WriteBuild(Console.Out, outputs, combinations, failures);

    public static void WriteBuild(TextWriter writer, IReadOnlyList<string> outputs, IReadOnlyDictionary<string, IReadOnlyList<string>>? combinations = null, IReadOnlyList<BuildProjectErrors>? failures = null)
    {
        var shortener = new NameShortener();
        var allNamesForShortening = outputs
            .Concat(failures?.Select(p => p.ProjectName) ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var shortened = shortener.ShortenMany(allNamesForShortening);

        var shortMap = new Dictionary<string, ShortenedName>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allNamesForShortening.Count; i++)
        {
            var name = allNamesForShortening[i];
            if (!shortMap.ContainsKey(name))
                shortMap[name] = shortened[i];
        }

        var builder = new StringBuilder();

        // Write successes
        for (var i = 0; i < outputs.Count; i++)
        {
            var orig = outputs[i];
            var sn = shortMap.TryGetValue(orig, out var s) ? s : new ShortenedName(orig, null, null);
            var line = sn.WithIndex();
            if (combinations != null && combinations.TryGetValue(orig, out var combs) && combs.Count > 0)
                line += $" ({string.Join(';', combs)})";
            builder.AppendLine($"✅{line}");
        }

        // Write failures, applying shortening throughout (including to error file paths)
        if (failures is { Count: > 0 })
        {
            if (outputs.Count > 0)
                builder.AppendLine();

            for (var i = 0; i < failures.Count; i++)
            {
                var proj = failures[i];
                var sn = shortMap.TryGetValue(proj.ProjectName, out var s) ? s : new ShortenedName(proj.ProjectName, null, null);
                var line = sn.WithIndex();
                var combs = proj.Combinations;
                if (combs is { Count: > 0 })
                    line += $" ({string.Join(';', combs)})";
                builder.AppendLine($"❌{line}");

                foreach (var error in proj.Errors)
                {
                    var errLine = ShortenError(error, shortMap);
                    builder.Append(Tab);
                    builder.AppendLine(errLine);
                }
            }
        }

        WriteFooter(builder, shortener, shortened);
        writer.Write(builder);
    }

    static string ShortenError(string error, IReadOnlyDictionary<string, ShortenedName> map)
    {
        if (string.IsNullOrEmpty(error) || map.Count == 0)
            return error;

        // Prefer longest (most specific) match first
        foreach (var kv in map.OrderByDescending(kv => kv.Key.Length))
        {
            var full = kv.Key;
            var sn = kv.Value;
            if (sn.Index is null || string.IsNullOrEmpty(sn.Prefix))
                continue;
            if (error.StartsWith(full, StringComparison.OrdinalIgnoreCase))
            {
                return sn.Display + error[full.Length..];
            }
        }

        return error;
    }

    public static void WriteTestSuccess(IReadOnlyList<TestAssemblyResult> assemblies)
        => WriteTestSuccess(Console.Out, assemblies);

    public static void WriteTestSuccess(TextWriter writer, IReadOnlyList<TestAssemblyResult> assemblies)
    {
        var shortener = new NameShortener();
        var names = shortener.ShortenMany(assemblies.Select(a => a.AssemblyName).ToArray());
        var builder = new StringBuilder();

        for (var i = 0; i < assemblies.Count; i++)
        {
            var assembly = assemblies[i];
            builder.Append(names[i].WithIndex());

            if (assembly.Passed > 0)
                builder.Append($" ✅{assembly.Passed}");

            if (assembly.Failed > 0)
                builder.Append($" ❌{assembly.Failed}");

            if (assembly.Skipped > 0)
                builder.Append($" ⏩{assembly.Skipped}");

            builder.AppendLine();
        }

        WriteFooter(builder, shortener, names);
        writer.Write(builder);
    }

    public static void WriteTestFailures(IReadOnlyList<TestAssemblyResult> assemblies, IReadOnlyList<TestFailure> failures)
        => WriteTestFailures(Console.Out, assemblies, failures);

    public static void WriteTestFailures(TextWriter writer, IReadOnlyList<TestAssemblyResult> assemblies, IReadOnlyList<TestFailure> failures)
    {
        var shortener = new NameShortener();
        var assemblyNames = shortener.ShortenMany(assemblies.Select(a => a.AssemblyName).ToArray());
        var builder = new StringBuilder();

        for (var i = 0; i < assemblies.Count; i++)
        {
            var assembly = assemblies[i];
            builder.Append(assemblyNames[i].WithIndex());

            if (assembly.Passed > 0)
                builder.Append($" ✅{assembly.Passed}");

            if (assembly.Failed > 0)
                builder.Append($" ❌{assembly.Failed}");

            if (assembly.Skipped > 0)
                builder.Append($" ⏩{assembly.Skipped}");

            builder.AppendLine();
        }

        if (failures.Count > 0)
            builder.AppendLine();

        var failureNames = shortener.ShortenMany(failures.Select(f => f.FullName).ToArray());
        for (var i = 0; i < failures.Count; i++)
        {
            builder.AppendLine($"❌{failureNames[i].WithIndex()}");
            WriteFailureDetails(builder, failures[i].Message, failures[i].StackTrace);
        }

        var footerNames = assemblyNames.Concat(failureNames).ToArray();
        WriteFooter(builder, shortener, footerNames);
        writer.Write(builder);
    }

    public static void WriteTestFallback()
        => WriteTestFallback(Console.Out);

    public static void WriteTestFallback(TextWriter writer)
        => writer.WriteLine("❌Tests");

    static void WriteFailureDetails(StringBuilder builder, string message, string stackTrace)
    {
        if (string.IsNullOrWhiteSpace(message) && string.IsNullOrWhiteSpace(stackTrace))
            return;

        if (!string.IsNullOrWhiteSpace(message))
        {
            foreach (var line in message.ReplaceLineEndings().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Append(Tab);
                builder.AppendLine(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(stackTrace))
        {
            foreach (var line in stackTrace.ReplaceLineEndings().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Append(Tab);
                builder.AppendLine(line);
            }
        }
    }

    static void WriteFooter(StringBuilder builder, NameShortener shortener, IReadOnlyList<ShortenedName> names)
    {
        var footer = shortener.FormatFooter(names);
        if (string.IsNullOrEmpty(footer))
            return;

        builder.AppendLine();
        builder.AppendLine(footer);
    }
}

record BuildProjectErrors(string ProjectName, IReadOnlyList<string> Errors, IReadOnlyList<string>? Combinations = null);

record TestAssemblyResult(string AssemblyName, int Passed, int Failed, int Skipped);

record TestFailure(string FullName, string Message, string StackTrace);