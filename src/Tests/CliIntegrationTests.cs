using System.Diagnostics;
using System.Text;

namespace Tests;

public class CliIntegrationTests
{
    static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    static string MdExe => Path.Combine(RepoRoot, "src", "md", "bin", "Debug", "net10.0", "md.exe");
    static string SolutionPath => Path.Combine(RepoRoot, "md.slnx");
    static string TestsProjectPath => Path.Combine(RepoRoot, "src", "Tests", "Tests.csproj");

    [Fact]
    public async Task When_root_version_then_prints_tool_version()
    {
        var result = await RunMdAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("md", result.Stdout);
    }

    [Fact]
    public async Task When_build_version_then_forwards_to_dotnet()
    {
        var result = await RunMdAsync("build", "--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\d+\.\d+", result.Stdout);
        Assert.DoesNotContain("42.42.42", result.Stdout);
    }

    [Fact]
    public async Task When_test_help_case_insensitive_then_passthrough()
    {
        var result = await RunMdAsync("TEST", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("test", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task When_build_no_build_then_emits_markdown_success()
    {
        var build = await RunDotnetAsync("build", SolutionPath, "-v:q");
        Assert.Equal(0, build.ExitCode);

        var result = await RunMdAsync("build", SolutionPath, "--no-restore", "--no-build");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✅", result.Stdout);
        Assert.Contains(".dll", result.Stdout);
    }

    [Fact]
    public async Task When_test_no_build_then_emits_markdown_counts()
    {
        var result = await RunMdAsync("test", TestsProjectPath, "--no-build");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("✅", result.Stdout);
    }

    static async Task<(int ExitCode, string Stdout, string Stderr)> RunDotnetAsync(params string[] args)
    {
        var quoted = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = quoted,
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    static async Task<(int ExitCode, string Stdout, string Stderr)> RunMdAsync(params string[] args)
    {
        Assert.True(File.Exists(MdExe), $"md executable not found at {MdExe}; run dotnet build first.");

        var quoted = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        var psi = new ProcessStartInfo
        {
            FileName = MdExe,
            Arguments = quoted,
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}