using System.Text;

namespace Devlooped.Formatting;

static class MarkdownWriter
{
    const char Tab = '\t';

    // Returns a comparable key (primarily the Version) for semantic TFM ordering
    // using the official NuGet.Frameworks parser. The returned tuple is structurally
    // comparable and will put net8.0 before net9.0 before net10.0 etc.
    static (System.Version? V, string Fw, string Prof, string Plat, System.Version? PlatV)
        GetTfmKey(string tfm)
    {
        var f = NuGet.Frameworks.NuGetFramework.ParseFolder(tfm ?? string.Empty);
        return (f.Version, f.Framework ?? "", f.Profile ?? "", f.Platform ?? "", f.PlatformVersion);
    }

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
        if (allComboSets.Count > 1)
        {
            // Sort the TFMs using NuGet.Frameworks for correct semantic ordering
            // (net8.0 < net9.0 < net10.0, netstandard after, etc.).
            var sorted = allComboSets
                .OrderBy(c => GetTfmKey(c.Split('|')[0]))
                .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var joined = string.Join("|", sorted);
            tfmsAlias = aliasTable.AssignGlobal($"#TFMS={joined}");
        }

        // Detect a common output root like "artifacts/bin" (or "bin") preceding per-project dirs
        // and assign a #bin (or similar) global alias so RHS paths can use #bin/...
        string? binRootAlias = null;
        var binRootValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in outputs)
        {
            var pp = o.Replace('\\', '/');
            int b = pp.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase);
            if (b < 0) b = pp.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase);
            if (b < 0 && (pp.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || pp.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)))
                b = 0; // path starts directly under bin/ or obj/
            if (b >= 0)
            {
                // include up to and including the bin/obj segment, e.g. "artifacts/bin" or "bin"
                // find the end of that segment
                int slashAfter = pp.IndexOf('/', b + 1);
                string root;
                if (b == 0)
                {
                    root = (slashAfter > 0) ? pp.Substring(0, slashAfter) : pp;
                }
                else
                {
                    // b points at the '/' before 'bin', so root goes up to end of 'bin'
                    root = (slashAfter > 0) ? pp.Substring(0, slashAfter) : pp;
                }
                if (!string.IsNullOrEmpty(root))
                    binRootValues.Add(root);
            }
        }
        if (binRootValues.Count == 1)
        {
            var br = binRootValues.First();
            // Prefer short #bin; AssignGlobal will dedupe if already present under another name.
            binRootAlias = aliasTable.AssignGlobal($"#bin={br}");
        }

        // Write any globals we discovered (e.g. #TFMS=...) at the very top.
        foreach (var line in aliasTable.AllEmittedGlobals())
            builder.AppendLine(line);

        // Track which alias tokens have had their "alias=..." definition lines emitted in this output.
        // Used to ensure prerequisite stem aliases (e.g. #M) are defined before any child that
        // references them in its own definition RHS (e.g. #MANC=#M.AspNetCore) or outcome path.
        var emittedDefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in aliasTable.AllEmittedGlobals())
        {
            var eq = g.IndexOf('=');
            if (eq > 0)
                emittedDefs.Add(g.Substring(0, eq));
        }

        // Local helper: ensure any #Alias references present in 'text' have their definitions
        // written (to 'builder') before proceeding. Recurses for chained references.
        // Only emits for aliases this AliasTable knows about (via GetRhsForAlias).
        void EnsureAliasDefs(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"#([A-Za-z][A-Za-z0-9]*)");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var token = "#" + m.Groups[1].Value;
                if (emittedDefs.Contains(token)) continue;
                var rhs = aliasTable.GetRhsForAlias(token);
                if (rhs != null)
                {
                    // Ensure the stem(s) *this* rhs itself references first (e.g. if rhs contains #M)
                    EnsureAliasDefs(rhs);
                    if (emittedDefs.Add(token))
                        builder.AppendLine($"{token}={rhs}");
                }
            }
        }

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
            if (combs is null || combs.Count == 0)
            {
                // Fall back to sniffing TFMs from the path so we still get TFM pivots
                // for multi-TFM projects even when the binlog does not surface combo metadata.
                var sniffed = SniffTfmsFromPath(full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (sniffed.Length > 0)
                    combs = sniffed;
            }
            lst.Add((full, combs));
        }

        // Pre-assign in length order so shorter stem names (potential alias bases) are registered
        // before longer dotted family members, enabling chained display forms like #MC=#M.Core .
        foreach (var k in byKey.Keys.OrderBy(k => k.Length).ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
            _ = aliasTable.AssignForValue(k, k);

        foreach (var (key, items) in byKey)
        {
            string alias = aliasTable.AssignForValue(key, key);
            var dispKey = aliasTable.GetDisplayFor(key) ?? key;

            // Ensure any stem references in this alias's definition RHS (e.g. "#M.Core") are defined first.
            // This guarantees bases like #M appear even if their def would have been emitted late
            // (due to byKey insertion order from path sorting) or if we need to surface a stem
            // that is only used for chaining in children.
            EnsureAliasDefs(dispKey);

            // Guarded: if Ensure pulled in this alias (as a stem for an earlier sibling), don't duplicate.
            if (emittedDefs.Add(alias))
                builder.AppendLine($"{alias}={dispKey}");

            // Sub-group by TFM-invariant form so variants of the same artifact collapse.
            var subGroups = new Dictionary<string, List<(string full, IReadOnlyList<string>? combs)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                string norm = item.full;
                var tfmsForNorm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var effectiveCombs = item.combs;
                if (effectiveCombs is null || effectiveCombs.Count == 0)
                {
                    var sniffed = SniffTfmsFromPath(item.full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (sniffed.Length > 0) effectiveCombs = sniffed;
                }
                if (effectiveCombs != null)
                {
                    foreach (var c in effectiveCombs)
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
                    var ec = it.combs;
                    if (ec is null || ec.Count == 0)
                    {
                        var sniffed = SniffTfmsFromPath(it.full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        if (sniffed.Length > 0) ec = sniffed;
                    }
                    if (ec != null)
                        foreach (var c in ec)
                        {
                            var t = c.Split('|')[0];
                            if (!string.IsNullOrWhiteSpace(t)) groupTfms.Add(t);
                        }
                }

                // Representative path (any member is fine; they are equivalent modulo TFM dir).
                string rep = g[0].full;

                string disp = rep;

                if (groupTfms.Count > 1)
                {
                    // Only parenthesize/pivot when there is more than one TFM for this item.
                    // Singles are emitted with their concrete TFM directory (no parens).
                    string pivotToken = tfmsAlias != null
                        ? $"({tfmsAlias})"
                        : "(" + string.Join("|", groupTfms.OrderBy(x => GetTfmKey(x))) + ")";

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

                // Also substitute any known path prefix aliases (e.g. "artifacts/bin" -> "#bin")
                // so we can render forms like #bin/#MC/Debug/(...)/#MC.dll .
                disp = aliasTable.SubstitutePathPrefixes(disp);

                // Ensure any alias references that will appear in the outcome line (project alias in path, etc.)
                EnsureAliasDefs(disp);

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
                var dispKey = aliasTable.GetDisplayFor(key) ?? key;

                EnsureAliasDefs(dispKey);
                if (emittedDefs.Add(alias))
                    builder.AppendLine($"{alias}={dispKey}");

                var combs = proj.Combinations;
                string suffix = string.Empty;
                if (combs is { Count: > 0 })
                {
                    // For a single combo/TFM don't parenthesize; only use (list) when there are multiple.
                    // The list coming from the reader is already ordered (OrdinalIgnoreCase).
                    suffix = combs.Count > 1
                        ? $" ({string.Join(';', combs)})"
                        : " " + combs[0];
                }
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

        var p = fullPath.Replace('\\', '/');
        var segs = p.Split('/');

        // Find last "bin" or "obj" segment; look *after* it for the project directory
        // (supports artifacts/bin/<Project>/Config/TFM/<Project>.dll layouts).
        int binIdx = -1;
        for (int i = segs.Length - 1; i >= 0; i--)
        {
            var s = segs[i];
            if (s.Equals("bin", StringComparison.OrdinalIgnoreCase) || s.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                binIdx = i;
                break;
            }
        }

        if (binIdx >= 0 && binIdx + 1 < segs.Length)
        {
            for (int i = binIdx + 1; i < segs.Length; i++)
            {
                var s = segs[i];
                if (string.IsNullOrEmpty(s)) continue;
                if (IsConfigDir(s) || IsTfmDir(s)) continue;
                if (LooksLikeProjectSegment(s))
                    return s;
                if (s.Contains('.')) break; // hit a file leaf
            }
        }

        // Fallback: segment immediately preceding the bin/obj (classic <proj>/bin/... layout)
        if (binIdx > 0)
        {
            var prev = segs[binIdx - 1];
            if (!string.IsNullOrEmpty(prev) && !IsConfigDir(prev) && !IsTfmDir(prev) && LooksLikeProjectSegment(prev))
                return prev;
        }

        // Fallback: leaf without extension
        var leaf = Path.GetFileNameWithoutExtension(p);
        if (!string.IsNullOrEmpty(leaf) && !leaf.Equals("bin", StringComparison.OrdinalIgnoreCase) && !leaf.Equals("obj", StringComparison.OrdinalIgnoreCase))
            return leaf;

        return segs.LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? "Item";
    }

    static bool IsConfigDir(string s) =>
        s.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("Release", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("DebugAnyCPU", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("ReleaseAnyCPU", StringComparison.OrdinalIgnoreCase);

    static bool IsTfmDir(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var lower = s.ToLowerInvariant();
        if (lower.StartsWith("net") || lower.StartsWith("netstandard") || lower.StartsWith("netcoreapp") || lower.StartsWith("mono") || lower.StartsWith("portable"))
            return true;
        // Conservative for other TFM-ish: short, starts with digit or 'v', only alphanum/.- chars.
        // Avoid matching long dotted project names like "ModelContextProtocol.Analyzers.Tests".
        if (s.Length <= 10 && (char.IsDigit(s[0]) || s.StartsWith("v", StringComparison.OrdinalIgnoreCase) || s.StartsWith("V", StringComparison.OrdinalIgnoreCase)))
        {
            if (s.All(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-'))
                return true;
        }
        return false;
    }

    static bool LooksLikeProjectSegment(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= 1) return false;
        if (s.IndexOfAny(new[] { '<', '>', '|', '*', '?', ' ' }) >= 0) return false;
        if (!s.Any(char.IsLetter)) return false;
        if (IsConfigDir(s) || IsTfmDir(s)) return false;
        return true;
    }

    static IEnumerable<string> SniffTfmsFromPath(string full)
    {
        if (string.IsNullOrEmpty(full)) yield break;
        // Match common TFM directory segments: /net8.0/, /net9.0/, /net10.0/, /netstandard2.0/, /net472/ etc.
        var re = new System.Text.RegularExpressions.Regex(@"/(net[0-9a-zA-Z.]+|netstandard[0-9.]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in re.Matches(full.Replace('\\', '/')))
        {
            var t = m.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(t))
                yield return t;
        }
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
                .OrderBy(t => GetTfmKey(t))
                .ToArray();

            if (tfms.Length > 1)
            {
                // Only emit a parenthesized pivot when >1 TFM. For a single TFM leave the concrete value bare (no parens).
                // Replace the first occurrence of a /<tfm>/ segment with the pivot form (/#TFMS) or (netX|netY).
                string content = tfmsAlias is not null ? tfmsAlias : string.Join("|", tfms);
                string pivotToken = $"({content})";

                foreach (var tfm in tfms)
                {
                    var needle = "/" + tfm + "/";
                    var idx = p.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        p = p.Substring(0, idx + 1) + pivotToken + p.Substring(idx + needle.Length - 1);
                        break;
                    }
                }
            }
            // else: 0 or 1 => leave the original TFM segment in the path (bare).
        }

        return p;
    }

    // Lightweight alias table implementing the rules from the plan.
    sealed class AliasTable
    {
        readonly Dictionary<string, string> valueToAlias = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> displayOverrides = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> aliasToRhs = new(StringComparer.Ordinal);
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
                aliasToRhs[alias] = val;
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

            // Support chained aliases for dotted names that share a stem already aliased, e.g.
            // #M=ModelContextProtocol then #MC=#M.Core for ModelContextProtocol.Core
            foreach (var kv in valueToAlias.OrderByDescending(kv => kv.Key.Length))
            {
                var prevVal = kv.Key;
                if (prevVal.Length > 0 && value.StartsWith(prevVal + ".", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = value.Substring(prevVal.Length + 1);
                    displayVal = kv.Value + "." + rest;
                    break;
                }
            }

            displayOverrides[value] = displayVal;
            aliasToRhs[candidate] = displayVal;
            locals.Add($"{candidate}={displayVal}");
            return candidate;
        }

        public IEnumerable<string> AllEmittedGlobals() => globals;

        // Currently we emit locals inline (right before each outcome) rather than from this list,
        // so that the definition appears immediately before the use per the spec.
        public IEnumerable<string> AllEmittedLocals() => locals;

        // Substitute known path-prefix values (e.g. "artifacts/bin") with their assigned aliases (e.g. "#bin").
        // Used to compress the RHS display paths. Longest match first.
        public string SubstitutePathPrefixes(string s)
        {
            var p = s;
            foreach (var kv in valueToAlias.OrderByDescending(kv => kv.Key.Length))
            {
                var v = kv.Key;
                if (!string.IsNullOrEmpty(v) && (v.Contains('/') || v.Contains('\\')))
                {
                    p = p.Replace(v, kv.Value, StringComparison.OrdinalIgnoreCase);
                }
            }
            return p;
        }

        // Expose for diagnostics/tests if needed.
        internal IReadOnlyDictionary<string, string> ValueToAlias => valueToAlias;

        public string? GetDisplayFor(string value)
            => displayOverrides.TryGetValue(value, out var d) ? d : null;

        public string? GetRhsForAlias(string alias)
            => aliasToRhs.TryGetValue(alias, out var rhs) ? rhs : null;
    }

    static string Abbreviate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "#Item";

        // For simple *short* project names without dots or path separators (e.g. "md", "Client", "Hosting")
        // produce a readable #Name form. Long names (e.g. ModelContextProtocol) get initial-letter form (#M)
        // so that family members can chain nicely (#MANC=#M.AspNetCore).
        var normalized = value.Replace('\\', '/');
        if (!normalized.Contains('.') && !normalized.Contains('/'))
        {
            if (value.Length <= 12)
                return "#" + value;
            // fall through for long single-segment names to produce short #X form
        }

        // Semantic dotted + PascalCase aware:
        // - Root (first) segment contributes only its first significant letter (keeps stems short: ModelContextProtocol → #M).
        // - Subsequent segments contribute first letter + any internal uppercase starters (PascalCase subwords).
        //   This yields distinctive tokens without numeric suffixes, e.g.:
        //     ModelContextProtocol.AspNetCore       → #MANC
        //     ModelContextProtocol.ConformanceClient → #MCC
        //     ModelContextProtocol.ConformanceServer → #MCS
        //     ModelContextProtocol.Core              → #MC
        var segs = normalized.Split('.', '/');
        var sb = new StringBuilder("#");
        for (int i = 0; i < segs.Length; i++)
        {
            var s = segs[i];
            if (s.Length == 0) continue;

            var letters = ExtractSignificantUppers(s);
            if (letters.Length == 0) continue;

            if (i == 0)
            {
                // Stem/root segment: keep it to a single letter so family members can form #M + rest
                sb.Append(letters[0]);
            }
            else
            {
                sb.Append(letters);
            }
        }
        if (sb.Length == 1)
        {
            // Fallback if nothing contributed (very unusual): use first letter of original
            char first = value.FirstOrDefault(char.IsLetter);
            if (first == '\0') first = value[0];
            sb.Append(char.ToUpperInvariant(first));
        }
        return sb.ToString();
    }

    static string ExtractSignificantUppers(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder();
        bool tookFirst = false;
        foreach (var ch in s)
        {
            if (char.IsUpper(ch) && char.IsLetter(ch))
            {
                sb.Append(ch);
                tookFirst = true;
            }
            else if (!tookFirst && char.IsLetter(ch))
            {
                sb.Append(char.ToUpperInvariant(ch));
                tookFirst = true;
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

        // Sort individual test failures by name for deterministic "Sorting tests by name" output.
        var orderedFailures = failures.OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
        var failureNames = shortener.ShortenMany(orderedFailures.Select(f => f.FullName).ToArray());
        for (var i = 0; i < orderedFailures.Length; i++)
        {
            builder.AppendLine($"❌{failureNames[i].WithIndex()}");
            WriteFailureDetails(builder, orderedFailures[i].Message, orderedFailures[i].StackTrace);
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