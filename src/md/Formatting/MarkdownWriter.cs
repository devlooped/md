using System.Text;

namespace Devlooped.Formatting;

static class MarkdownWriter
{
    public static void WriteBuildSuccess(IReadOnlyList<string> outputs)
        => WriteBuildSuccess(Console.Out, outputs);

    public static void WriteBuildSuccess(TextWriter writer, IReadOnlyList<string> outputs)
    {
        var shortener = new NameShortener();
        var names = shortener.ShortenMany(outputs);
        var builder = new StringBuilder();

        foreach (var name in names)
            builder.AppendLine($"✅{name.WithIndex()}");

        WriteFooter(builder, shortener, names);
        writer.Write(builder);
    }

    public static void WriteBuildFailures(IReadOnlyList<BuildProjectErrors> projects)
        => WriteBuildFailures(Console.Out, projects);

    public static void WriteBuildFailures(TextWriter writer, IReadOnlyList<BuildProjectErrors> projects)
    {
        var shortener = new NameShortener();
        var projectNames = shortener.ShortenMany(projects.Select(p => p.ProjectName).ToArray());
        var builder = new StringBuilder();

        for (var i = 0; i < projects.Count; i++)
        {
            builder.AppendLine($"❌{projectNames[i].WithIndex()}");

            foreach (var error in projects[i].Errors)
            {
                builder.Append("> ");
                builder.AppendLine(error);
            }
        }

        WriteFooter(builder, shortener, projectNames);
        writer.Write(builder);
    }

    public static void WriteBuildFallback()
        => WriteBuildFallback(Console.Out);

    public static void WriteBuildFallback(TextWriter writer)
        => writer.WriteLine("❌Build");

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

        var language = stackTrace.Contains(".vb:line", StringComparison.Ordinal) ? "vb" : "csharp";
        builder.AppendLine($"> ```{language}");

        if (!string.IsNullOrWhiteSpace(message))
        {
            foreach (var line in message.ReplaceLineEndings().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                builder.AppendLine($"> {line}");
        }

        if (!string.IsNullOrWhiteSpace(stackTrace))
        {
            foreach (var line in stackTrace.ReplaceLineEndings().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                builder.AppendLine($"> {line}");
        }

        builder.AppendLine("> ```");
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

record BuildProjectErrors(string ProjectName, IReadOnlyList<string> Errors);

record TestAssemblyResult(string AssemblyName, int Passed, int Failed, int Skipped);

record TestFailure(string FullName, string Message, string StackTrace);