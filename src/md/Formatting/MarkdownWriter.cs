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
        var builder = new StringBuilder();

        // New #ALIAS emission (replaces prior NameShortener + bottom [n] footer refs for build outcomes).
        // We receive full (/-normalized) output paths from BinlogReader (or basenames for legacy callers / tests).
        // We derive logical keys (project-ish segment or leaf), assign short semantic #ALIASes,
        // emit globals (incl. combo sets as #TFMS) at top, then a local alias def + outcome line per item.

        var aliasTable = new AliasTable();

        // Discover combo sets (TFMS etc.) across all provided combos (keyed by full path now).
        var allComboSets = new HashSet<string>(StringComparer.Ordinal);
        if (combinations != null)
        {
            foreach (var kv in combinations)
                foreach (var c in kv.Value)
                    allComboSets.Add(c);
        }
        if (failures != null)
        {
            foreach (var f in failures)
                foreach (var c in f.Combinations ?? Array.Empty<string>())
                    allComboSets.Add(c);
        }

        string? tfmsAlias = null;
        if (allComboSets.Count > 0)
        {
            var sorted = allComboSets.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var joined = string.Join("|", sorted);
            tfmsAlias = aliasTable.AssignGlobal($"#TFMS={joined}");
        }

        // Write any globals we discovered (e.g. #TFMS=...) at the very top.
        foreach (var line in aliasTable.AllEmittedGlobals())
            builder.AppendLine(line);

        // --- Successes ---
        // Group by the alias key (project segment or leaf from DeriveProjectKey).
        // Within each alias key, further group TFM variants of the *same* logical output
        // (paths that only differ in the TFM directory under bin/Debug etc.).
        // This ensures we emit *one* compact line with a pivot (e.g. (net10.0|net8.0|net9.0) or (#TFMS))
        // instead of repeating the full path once per TFM.
        var byKey = new Dictionary<string, List<(string full, IReadOnlyList<string>? combs)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var full in outputs)
        {
            string key = DeriveProjectKey(full);
            if (!byKey.TryGetValue(key, out var lst))
            {
                lst = new List<(string, IReadOnlyList<string>?)>();
                byKey[key] = lst;
            }
            IReadOnlyList<string>? combs = null;
            combinations?.TryGetValue(full, out combs);
            lst.Add((full, combs));
        }

        foreach (var (key, items) in byKey)
        {
            string alias = aliasTable.AssignForValue(key, key);
            builder.AppendLine($"{alias}={key}");

            // Sub-group by TFM-invariant form so variants of the same artifact collapse.
            var subGroups = new Dictionary<string, List<(string full, IReadOnlyList<string>? combs)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                string norm = item.full;
                var tfmsForNorm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (item.combs != null)
                {
                    foreach (var c in item.combs)
                    {
                        var t = c.Split('|')[0];
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            tfmsForNorm.Add(t);
                            // Blank this specific TFM dir for grouping purposes.
                            var needle = "/" + t + "/";
                            int pos = norm.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                            if (pos >= 0)
                                norm = norm.Substring(0, pos + 1) + "__TFM__" + norm.Substring(pos + needle.Length - 1);
                            else
                            {
                                // also handle the case where the tfm is the last path segment (uncommon here)
                                var lastNeedle = "/" + t;
                                if (norm.EndsWith(lastNeedle, StringComparison.OrdinalIgnoreCase))
                                    norm = norm.Substring(0, norm.Length - lastNeedle.Length) + "/__TFM__";
                            }
                        }
                    }
                }
                if (!subGroups.TryGetValue(norm, out var g))
                {
                    g = new List<(string, IReadOnlyList<string>?)>();
                    subGroups[norm] = g;
                }
                g.Add(item);
            }

            foreach (var g in subGroups.Values)
            {
                // Union of all TFMs for the items in this logical sub-group.
                var groupTfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in g)
                {
                    if (it.combs != null)
                        foreach (var c in it.combs)
                        {
                            var t = c.Split('|')[0];
                            if (!string.IsNullOrWhiteSpace(t)) groupTfms.Add(t);
                        }
                }

                // Representative path (any member is fine; they are equivalent modulo TFM dir).
                string rep = g[0].full;

                string disp = rep;

                if (groupTfms.Count > 0)
                {
                    string pivotToken = tfmsAlias != null
                        ? $"({tfmsAlias})"
                        : "(" + string.Join("|", groupTfms.OrderBy(x => x, StringComparer.Ordinal)) + ")";

                    // Replace the first known TFM dir occurrence with the pivot token.
                    foreach (var t in groupTfms)
                    {
                        var needle = "/" + t + "/";
                        int idx = disp.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            disp = disp.Substring(0, idx + 1) + pivotToken + disp.Substring(idx + needle.Length - 1);
                            break;
                        }
                    }
                }

                // Extra compactness: substitute the alias token into the path on the RHS
                // where the original project key (folder or output name) repeats.
                // This produces forms like: .../tests/#MAAAU/bin/Debug/(#TFMS)/#MAAAU.dll
                // which is still trivial for an LLM to expand back to full paths by substituting the alias value.
                if (!string.IsNullOrEmpty(key) && key.Length > 1)
                {
                    // Safe because the key (e.g. Microsoft.Agents.AI.Foo) is long and distinctive.
                    disp = disp.Replace(key, alias, StringComparison.OrdinalIgnoreCase);
                }

                builder.AppendLine($"✅{alias}→{disp}");
            }
        }

        // --- Failures ---
        if (failures is { Count: > 0 })
        {
            if (outputs.Count > 0)
                builder.AppendLine();

            foreach (var proj in failures)
            {
                string key = proj.ProjectName;
                string alias = aliasTable.AssignForValue(key, key);

                builder.AppendLine($"{alias}={key}");

                var combs = proj.Combinations;
                string suffix = (combs is { Count: > 0 }) ? $" ({string.Join(';', combs)})" : string.Empty;
                builder.AppendLine($"❌{alias}/{suffix}");

                foreach (var error in proj.Errors)
                {
                    // Keep existing relative stripping for errors under the project; we can later teach it #ALIAS too (task 3).
                    var relative = StripProjectDirectoryPrefix(error, proj.ProjectName);
                    builder.Append(Tab);
                    builder.AppendLine(relative); // for now raw relative; ShortenError can be adapted later
                }
            }
        }

        writer.Write(builder);
    }

    // --- Helpers for the new #ALIAS format (build outcomes) ---

    static string DeriveProjectKey(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return "Item";

        // Look for the segment immediately preceding /bin/ or /obj/ (typical project root for the output).
        var idx = fullPath.LastIndexOf("/bin/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = fullPath.LastIndexOf("/obj/", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            var start = fullPath.LastIndexOf('/', idx - 1);
            if (start >= 0 && start + 1 < idx)
            {
                var seg = fullPath.Substring(start + 1, idx - start - 1);
                if (!string.IsNullOrEmpty(seg))
                    return seg;
            }
        }

        // Fallback: leaf without extension (e.g. the dll/exe name, or last dir).
        var leaf = Path.GetFileNameWithoutExtension(fullPath.Replace('\\', '/'));
        if (!string.IsNullOrEmpty(leaf))
            return leaf;

        return fullPath.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? "Item";
    }

    static string BuildDisplayPath(string fullPath, IReadOnlyList<string>? combsForThis, string? tfmsAlias)
    {
        var p = fullPath; // already /-normalized by reader in the common case

        if (combsForThis is { Count: > 0 })
        {
            // Collect the tfm parts (before any |rid/platform).
            var tfms = combsForThis
                .Select(c => c.Split('|')[0])
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (tfms.Length > 0)
            {
                // Replace the first occurrence of a /<tfm>/ segment with the pivot form (/#TFMS) or (netX|netY).
                // We scan a copy of the list so we can try each until one matches.
                foreach (var tfm in tfms)
                {
                    var needle = "/" + tfm + "/";
                    var idx = p.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string pivotToken;
                        if (tfmsAlias is not null)
                            pivotToken = $"({tfmsAlias})";
                        else
                            pivotToken = "(" + string.Join("|", tfms) + ")";

                        p = p.Substring(0, idx + 1) + pivotToken + p.Substring(idx + needle.Length - 1);
                        break;
                    }
                }
            }
        }

        return p;
    }

    // Lightweight alias table implementing the rules from the plan.
    sealed class AliasTable
    {
        readonly Dictionary<string, string> valueToAlias = new(StringComparer.OrdinalIgnoreCase);
        readonly List<string> globals = new();
        readonly List<string> locals = new();

        public string AssignGlobal(string aliasEqualsValue)
        {
            // aliasEqualsValue is of the form "#TFMS=net8.0|net10.0" or "#MEC=..."
            var eq = aliasEqualsValue.IndexOf('=');
            if (eq <= 0) return aliasEqualsValue;

            var alias = aliasEqualsValue.Substring(0, eq);
            var val = aliasEqualsValue.Substring(eq + 1);

            if (!valueToAlias.ContainsKey(val))
            {
                valueToAlias[val] = alias;
                globals.Add(aliasEqualsValue);
            }
            return alias;
        }

        public string AssignForValue(string value, string? preferredDisplayValue = null)
        {
            if (valueToAlias.TryGetValue(value, out var existing))
                return existing;

            var alias = Abbreviate(value);
            var candidate = alias;
            int suffix = 2;
            while (valueToAlias.Values.Contains(candidate, StringComparer.Ordinal))
                candidate = alias + suffix++;

            valueToAlias[value] = candidate;
            var displayVal = string.IsNullOrEmpty(preferredDisplayValue) ? value : preferredDisplayValue;
            locals.Add($"{candidate}={displayVal}");
            return candidate;
        }

        public IEnumerable<string> AllEmittedGlobals() => globals;

        // Currently we emit locals inline (right before each outcome) rather than from this list,
        // so that the definition appears immediately before the use per the spec.
        public IEnumerable<string> AllEmittedLocals() => locals;
    }

    static string Abbreviate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#Item";

        // For simple short project names without dots or path separators (e.g. "md", "Client", "Hosting")
        // produce a readable #Name form. This keeps the token after ✅/❌ familiar (matches user examples like #Hosting).
        var normalized = value.Replace('\\', '/');
        if (!normalized.Contains('.') && !normalized.Contains('/'))
        {
            return "#" + value;
        }

        // Semantic dotted: first upper-case-starting letter(s) of each dot segment.
        // Matches examples: Microsoft.Agents.AI → #MAAI, Microsoft.Extensions.Configuration → #MEC
        var segs = normalized.Split('.', '/');
        var sb = new StringBuilder("#");
        for (int i = 0; i < segs.Length; i++)
        {
            var s = segs[i];
            if (s.Length == 0) continue;

            bool isLast = (i == segs.Length - 1);
            // Only swallow the whole last segment for short all-caps acronyms (e.g. "AI").
            if (isLast && s.Length <= 2 && s.All(char.IsUpper))
            {
                sb.Append(s);
            }
            else
            {
                char first = s.FirstOrDefault(char.IsLetter);
                if (first == '\0') first = s[0];
                sb.Append(char.ToUpperInvariant(first));
            }
        }
        return sb.ToString();
    }

    static string ShortenError(string error, IReadOnlyDictionary<string, ShortenedName> map)
    {
        if (string.IsNullOrEmpty(error))
            return error;

        // Normalize path separators for consistent display (forward slashes) in markdown output
        error = error.Replace('\\', '/');

        if (map.Count == 0)
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

    static string StripProjectDirectoryPrefix(string error, string projectName)
    {
        if (string.IsNullOrEmpty(error) || string.IsNullOrEmpty(projectName))
            return error;

        // Normalize for matching (incoming may have \ from direct test data or pre-norm)
        error = error.Replace('\\', '/');
        var prefix = projectName + "/";
        if (error.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return error[prefix.Length..];
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