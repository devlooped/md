namespace Devlooped.Formatting;

sealed class NameShortener
{
    readonly Dictionary<string, int> prefixIndexes = new(StringComparer.Ordinal);
    int nextIndex = 1;

    public ShortenedName Shorten(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new ShortenedName(name, null, null);
    }

    public IReadOnlyList<ShortenedName> ShortenMany(IReadOnlyList<string> names)
    {
        if (names.Count < 2)
            return names.Select(n => new ShortenedName(n, null, null)).ToArray();

        var prefix = FindCommonPrefix(names);
        if (prefix.Length == 0 || names.Any(name => name.Length <= prefix.Length))
            return names.Select(n => new ShortenedName(n, null, null)).ToArray();

        var index = GetOrCreateIndex(prefix);
        return names
            .Select(name => new ShortenedName(name[prefix.Length..], index, prefix))
            .ToArray();
    }

    public string FormatFooter(IEnumerable<ShortenedName> names)
    {
        var groups = names
            .Where(n => n.Index is not null && n.Prefix is not null)
            .GroupBy(n => n.Index!.Value)
            .OrderBy(g => g.Key)
            .Select(g => $"[{g.Key}]: {g.First().Prefix}");

        return string.Join(Environment.NewLine, groups);
    }

    static string FindCommonPrefix(IReadOnlyList<string> names)
    {
        var ordered = names.OrderBy(n => n.Length).ToArray();
        var shortest = ordered[0];

        var length = 0;
        for (var i = 0; i < shortest.Length; i++)
        {
            if (ordered.All(name => name.Length > i && name[i] == shortest[i]))
                length++;
            else
                break;
        }

        var prefix = shortest[..length];
        var lastDot = prefix.LastIndexOf('.');
        if (lastDot >= 0)
            prefix = prefix[..(lastDot + 1)];

        // Never remove all dot-separated segments: ensure at least one '.' remains in every rendered suffix.
        // Back off to previous dot if any suffix after prefix would have no dot (e.g. avoid turning "MyApp.dll" into "[1]dll").
        while (prefix.Length > 0 && names.Any(name => !name[prefix.Length..].Contains('.')))
        {
            var prev = prefix[..^1].LastIndexOf('.');
            if (prev >= 0)
                prefix = prefix[..(prev + 1)];
            else
                prefix = string.Empty;
        }

        return prefix;
    }

    int GetOrCreateIndex(string prefix)
    {
        if (!prefixIndexes.TryGetValue(prefix, out var index))
        {
            index = nextIndex++;
            prefixIndexes[prefix] = index;
        }

        return index;
    }
}

readonly record struct ShortenedName(string Display, int? Index, string? Prefix)
{
    public string WithIndex() => Index is null ? Display : $"[{Index}]{Display}";
}