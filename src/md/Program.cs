using System.CommandLine;
using System.CommandLine.Help;
using System.Runtime.InteropServices;
using System.Text;

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;

if (args is ["--version"])
{
    Console.WriteLine($"{ThisAssembly.Project.ToolCommandName} {ThisAssembly.Project.Version}");
    return 0;
}

if (args.Length == 0 || (args.Length == 1 && Devlooped.HelpDetector.IsHelpRequest(args)))
{
    WriteRootHelp();
    return 0;
}

if (args.Length >= 1 && Devlooped.HelpDetector.IsSubcommand(args[0]))
{
    var verb = args[0].Equals("test", StringComparison.OrdinalIgnoreCase) ? "test" : "build";
    var forwarded = args[1..];
    if (Devlooped.HelpDetector.IsHelpRequest(forwarded))
        return await Devlooped.DotnetRunner.RunPassthroughAsync(verb, forwarded);
}

var rootCommand = new RootCommand("dnx md — minimal markdown build/test output for AI");
rootCommand.TreatUnmatchedTokensAsErrors = false;

var buildCommand = new Command("build", "Run dotnet build; remaining args are forwarded to dotnet build directly.")
{
    TreatUnmatchedTokensAsErrors = false,
};
RemoveHelpOption(buildCommand);
buildCommand.SetAction(async (parseResult, cancellationToken) =>
    await Devlooped.BuildCommand.ExecuteAsync(parseResult.UnmatchedTokens, cancellationToken));

var testCommand = new Command("test", "Run dotnet test; remaining args are forwarded to dotnet test directly.")
{
    TreatUnmatchedTokensAsErrors = false,
};
RemoveHelpOption(testCommand);
testCommand.SetAction(async (parseResult, cancellationToken) =>
    await Devlooped.TestCommand.ExecuteAsync(parseResult.UnmatchedTokens, cancellationToken));

rootCommand.Subcommands.Add(buildCommand);
rootCommand.Subcommands.Add(testCommand);
RemoveHelpOption(rootCommand);

return await rootCommand.Parse(args).InvokeAsync();

static void RemoveHelpOption(Command command)
{
    var help = command.Options.OfType<HelpOption>().FirstOrDefault();
    if (help is not null)
        command.Options.Remove(help);
}

static void WriteRootHelp()
{
    Console.WriteLine(
        """
        Description:
          dnx md — minimal markdown build/test output for AI

        Usage:
          md [command] [dotnet args...]

        Commands:
          build  Run dotnet build; remaining args are forwarded to dotnet build directly.
          test   Run dotnet test; remaining args are forwarded to dotnet test directly.

        Options:
          --version  Show md tool version information
        """);
}