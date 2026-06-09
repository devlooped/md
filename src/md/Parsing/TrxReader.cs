using System.Text.RegularExpressions;
using System.Xml.Linq;
using Devlooped.Web;

namespace Devlooped.Parsing;

partial class TrxReader
{
    public TrxSummary Read(string resultsDirectory)
    {
        if (!Directory.Exists(resultsDirectory))
            return new TrxSummary([], []);

        var assemblies = new Dictionary<string, AssemblyCounts>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<TrxFailure>();
        var testIds = new HashSet<string>();

        foreach (var trx in Directory.EnumerateFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTime))
        {
            using var file = File.OpenRead(trx);
            var doc = HtmlDocument.Load(file, new HtmlReaderSettings { CaseFolding = Sgml.CaseFolding.None });

            foreach (var result in doc.CssSelectElements("UnitTestResult"))
            {
                var id = result.Attribute("testId")!.Value;
                if (!testIds.Add(id))
                    continue;

                var outcome = result.Attribute("outcome")?.Value;
                var assembly = ResolveAssemblyName(doc, result, trx);

                if (!assemblies.TryGetValue(assembly, out var counts))
                {
                    counts = new AssemblyCounts();
                    assemblies[assembly] = counts;
                }

                switch (outcome)
                {
                    case "Passed":
                        counts.Passed++;
                        break;
                    case "Failed":
                        counts.Failed++;
                        AddFailure(failures, doc, result);
                        break;
                    case "NotExecuted":
                        counts.Skipped++;
                        break;
                }
            }
        }

        return new TrxSummary(
            assemblies
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new TrxAssemblyResult(x.Key, x.Value.Passed, x.Value.Failed, x.Value.Skipped))
                .ToArray(),
            failures);
    }

    static string ResolveAssemblyName(XDocument doc, XElement result, string trxPath)
    {
        var testId = result.Attribute("testId")!.Value;
        var definition = doc.CssSelectElement($"UnitTest[id={testId}]");
        var storage = definition?.Attribute("storage")?.Value;
        if (!string.IsNullOrWhiteSpace(storage))
            return Path.GetFileName(storage);

        var className = definition?.CssSelectElement("TestMethod")?.Attribute("className")?.Value;
        if (!string.IsNullOrWhiteSpace(className))
            return $"{className.Split('.')[^1]}.dll";

        return Path.GetFileNameWithoutExtension(trxPath) + ".dll";
    }

    static void AddFailure(List<TrxFailure> failures, XDocument doc, XElement result)
    {
        var message = result.CssSelectElement("Output > ErrorInfo > Message")?.Value
            ?? result.CssSelectElement("Message")?.Value
            ?? string.Empty;

        var stackTrace = result.CssSelectElement("Output > ErrorInfo > StackTrace")?.Value
            ?? result.CssSelectElement("StackTrace")?.Value
            ?? string.Empty;

        var testName = result.Attribute("testName")!.Value;
        stackTrace = TrimStackTrace(doc, result, stackTrace);

        failures.Add(new TrxFailure(testName, message.Trim(), stackTrace.Trim()));
    }

    static string TrimStackTrace(XDocument doc, XElement result, string stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
            return stackTrace;

        var testId = result.Attribute("testId")!.Value;
        var method = doc.CssSelectElement($"UnitTest[id={testId}] TestMethod");
        var lines = stackTrace.ReplaceLineEndings().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        if (method is not null)
        {
            var fullName = $"{method.Attribute("className")?.Value}.{method.Attribute("name")?.Value}";
            var last = Array.FindLastIndex(lines, x => x.Contains(fullName, StringComparison.Ordinal));
            if (last != -1)
                lines = lines[..(last + 1)];
        }

        return string.Join(Environment.NewLine, lines.Select(line => TrimStackLine(line)));
    }

    static string TrimStackLine(string line)
    {
        var match = ParseFile().Match(line);
        if (!match.Success)
            return line;

        var file = match.Groups["file"].Value;
        var baseDir = Directory.GetCurrentDirectory();

        if (Path.IsPathRooted(file) && file.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
            file = file[baseDir.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return line.Replace(match.Groups["file"].Value, file);
    }

    [GeneratedRegex(@" in (?<file>.+):line (?<line>\d+)", RegexOptions.Compiled)]
    private static partial Regex ParseFile();

    sealed class AssemblyCounts
    {
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
    }
}

record TrxSummary(IReadOnlyList<TrxAssemblyResult> Assemblies, IReadOnlyList<TrxFailure> Failures);

record TrxAssemblyResult(string AssemblyName, int Passed, int Failed, int Skipped);

record TrxFailure(string FullName, string Message, string StackTrace);