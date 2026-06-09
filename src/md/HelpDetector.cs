namespace Devlooped;

static class HelpDetector
{
    public static bool IsHelpRequest(IReadOnlyList<string> args)
        => args.Any(IsHelpToken);

    public static bool IsSubcommand(string arg)
        => arg.Equals("build", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("test", StringComparison.OrdinalIgnoreCase);

    static bool IsHelpToken(string arg)
        => arg.Equals("-?", StringComparison.Ordinal)
            || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("-help", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--help", StringComparison.OrdinalIgnoreCase);
}