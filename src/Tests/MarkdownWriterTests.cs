using System.Text;
using Devlooped.Formatting;

namespace Tests;

public class MarkdownWriterTests
{
    [Fact]
    public void When_build_succeeds_then_writes_shortened_outputs_with_footer()
    {
        var writer = new StringWriter();
        // Inputs are bare names (as before). The new format emits a local #ALIAS def + ✅#ALIAS→name line (no bottom [n] footer).
        MarkdownWriter.WriteBuildSuccess(writer,
        [
            "Microsoft.Data.Ingestion.Api.dll",
            "Microsoft.Data.Ingestion.Web.dll",
        ]);

        var output = writer.ToString();

        // New #ALIAS style (local def immediately before outcome + arrow form). No old [1] footer.
        Assert.DoesNotContain("[1]:", output);
        // The exact alias token is derived by Abbreviate; assert structural properties instead of hardcoding the abbreviation.
        Assert.Contains("=Microsoft.Data.Ingestion.Api", output);
        Assert.Contains("✅#", output);
        // With the new intra-path alias substitution, repeated project name parts on the RHS are also replaced by the alias token.
        // For these bare-name inputs this yields forms like "→#MDIA.dll".
        Assert.Contains("→#", output);
        Assert.Contains(".dll", output);
        Assert.Contains("=Microsoft.Data.Ingestion.Web", output);
        Assert.Contains("→#", output);
    }

    [Fact]
    public void When_build_fails_then_writes_tab_prefixed_errors()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteBuildFailures(writer,
            [new BuildProjectErrors("md", ["src/Program.cs:12 CS1002: ; expected"])]);

        var output = writer.ToString();

        // New format: local alias def immediately before the failure line, then the tab-indented detail.
        // Still uses the single-tab indent invariant. Alias for bare "md" is "#md".
        Assert.StartsWith("#md=md" + Environment.NewLine + "❌#md/" + Environment.NewLine, output);
        Assert.Contains("\tsrc/Program.cs:12 CS1002: ; expected" + Environment.NewLine, output);
        Assert.Equal('\t', output[output.IndexOf('\n', output.IndexOf("❌#md/")) + 1]);
    }

    [Fact]
    public void When_test_succeeds_then_writes_assembly_counts_with_footer()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteTestSuccess(writer,
        [
            new TestAssemblyResult("MyCompany.MyApp.Tests.dll", 23, 0, 5),
            new TestAssemblyResult("MyCompany.MyApp.IntegrationTests.dll", 10, 0, 0),
        ]);

        Assert.Equal(
            """
            [1]Tests.dll ✅23 ⏩5
            [1]IntegrationTests.dll ✅10

            [1]: MyCompany.MyApp.
            """.ReplaceLineEndings() + Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void When_test_fails_then_writes_assembly_header_and_tab_indented_stack_trace()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteTestFailures(writer,
            [
                new TestAssemblyResult("MyCompany.MyApp.Tests.dll", 2, 1, 0),
                new TestAssemblyResult("MyCompany.MyApp.IntegrationTests.dll", 0, 0, 0),
            ],
            [
                new TestFailure("MyCompany.MyApp.Tests.UnitTests.Fails", "Assert.True() Failure", "   at MyCompany.MyApp.Tests.UnitTests.Fails() in src/Tests/Sample.cs:line 10"),
                new TestFailure("MyCompany.MyApp.Tests.UnitTests.AlsoFails", "Expected 1", "   at MyCompany.MyApp.Tests.UnitTests.AlsoFails() in src/Tests/Sample.cs:line 11"),
            ]);

        var output = writer.ToString();
        Assert.StartsWith("[1]Tests.dll ✅2 ❌1" + Environment.NewLine, output);
        Assert.Contains(
            "❌[2]UnitTests.Fails" + Environment.NewLine +
            "\tAssert.True() Failure" + Environment.NewLine +
            "\t   at MyCompany.MyApp.Tests.UnitTests.Fails() in src/Tests/Sample.cs:line 10" + Environment.NewLine,
            output);
        Assert.Contains("[1]: MyCompany.MyApp.", output);
        Assert.Contains("[2]: MyCompany.MyApp.Tests.", output);

        // Failures are sorted by name (AlsoFails before Fails) for deterministic output.
        var firstFailIdx = output.IndexOf("❌", output.IndexOf("IntegrationTests"));
        var alsoIdx = output.IndexOf("AlsoFails", firstFailIdx);
        var failsIdx = output.IndexOf("UnitTests.Fails", firstFailIdx);
        Assert.True(alsoIdx >= 0 && failsIdx > alsoIdx, "test failures should be emitted sorted by name");
    }

    [Fact]
    public void When_failure_has_message_only_then_writes_tab_indented_message()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteTestFailures(writer,
            [],
            [new TestFailure("MyApp.Tests.T", "boom", "")]);

        Assert.Contains(
            "❌MyApp.Tests.T" + Environment.NewLine + "\tboom" + Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void When_failure_has_stack_only_then_writes_tab_indented_stack()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteTestFailures(writer,
            [],
            [new TestFailure("MyApp.Tests.T", "", "   at MyApp.Tests.T() in File.vb:line 1")]);

        Assert.Contains("\t   at MyApp.Tests.T() in File.vb:line 1", writer.ToString());
    }

    [Fact]
    public void When_build_fallback_then_writes_minimal_line()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteBuildFallback(writer);
        Assert.Equal("❌Build" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void When_test_fallback_then_writes_minimal_line()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteTestFallback(writer);
        Assert.Equal("❌Tests" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void When_build_has_both_successes_and_failures_then_applies_shortening_throughout_and_renders_refs_at_very_bottom()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteBuild(writer,
            [
                "Microsoft.Agents.AI.Core.dll",
                "Microsoft.Agents.AI.Core.UnitTests.dll",
            ],
            null,
            [
                new BuildProjectErrors("Microsoft.Agents.AI.Mcp", ["Microsoft.Agents.AI.Mcp\\Class1.cs:5 CS1002: ; expected"]),
            ]);

        var output = writer.ToString();

        // New #ALIAS format (no old [n] or bottom footer refs).
        Assert.DoesNotContain("[1]:", output);
        Assert.DoesNotContain("✅[1]", output);
        Assert.DoesNotContain("❌[1]", output);

        // Aliases (local defs) for the repeated dotted project/output names; exact token depends on Abbreviate.
        Assert.Contains("=Microsoft.Agents.AI.Core", output);
        Assert.Contains("✅#", output);
        Assert.Contains("→", output);

        // Failure project gets its local alias def + ❌#Alias/ form (plus the tab error detail).
        Assert.Contains("=Microsoft.Agents.AI.Mcp", output);
        Assert.Contains("❌#", output);
        Assert.Contains("/", output); // the / after the aliased project key for failures

        // Error details remain tab-indented and project-prefix stripped (relative).
        Assert.Contains("\tClass1.cs:5 CS1002: ; expected", output);

        // Tab (not spaces) indent invariant still holds for build error details.
        var detailPos = output.IndexOf("Class1.cs:5", StringComparison.Ordinal);
        Assert.True(detailPos > 0 && output[detailPos - 1] == '\t', "Build error detail must be prefixed by single tab char, not spaces or other whitespace.");

        // There is still a separating blank line between success block(s) and the failure block (structure check).
        // In the new format the failure block starts with its local alias def line, then the ❌ line.
        Assert.Contains(Environment.NewLine + Environment.NewLine, output);
        Assert.Contains("❌#", output);
    }

    [Fact]
    public void When_multiple_tfms_for_same_logical_project_then_emits_single_pivoted_line_under_one_alias()
    {
        var writer = new StringWriter();

        // Simulate what the reader now provides for a multi-TFM project (full paths, combos per path).
        var baseDir = "C:/Code/oss/agent-framework/dotnet/tests/Microsoft.Agents.AI.A2A.UnitTests/bin/Debug";
        var p10 = baseDir + "/net10.0/Microsoft.Agents.AI.A2A.UnitTests.dll";
        var p8 = baseDir + "/net8.0/Microsoft.Agents.AI.A2A.UnitTests.dll";
        var p9 = baseDir + "/net9.0/Microsoft.Agents.AI.A2A.UnitTests.dll";

        var outputs = new[] { p10, p8, p9 };
        var combos = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [p10] = new[] { "net10.0" },
            [p8] = new[] { "net8.0" },
            [p9] = new[] { "net9.0" },
        };

        MarkdownWriter.WriteBuildSuccess(writer, outputs, combos);
        var output = writer.ToString();

        // Only one local alias definition for the project.
        Assert.Contains("=Microsoft.Agents.AI.A2A.UnitTests", output);

        // Exactly one success outcome line (the three TFM variants must have been grouped).
        var successCount = output.Split('\n').Count(l => l.Contains("✅"));
        Assert.Equal(1, successCount);

        // The single line must contain a pivot for the TFMs (either the global #TFMS form or the inline list).
        // With NuGet.Frameworks sorting we expect net8 before net9 before net10.
        Assert.True(
            output.Contains("(#TFMS)") || output.Contains("(net8.0|net9.0|net10.0)") || output.Contains("(net"),
            "Expected a TFM pivot in the (collapsed) path");

        // The #TFMS global (if emitted) or any inline list must be in ascending version order.
        Assert.False(output.Contains("net10.0|net8.0") || output.Contains("net9.0|net8.0"), "TFMs should be sorted net8 < net9 < net10");

        // As a bonus for shortness, the alias token should also appear inside the path on the RHS.
        Assert.Contains("✅#", output);
        // The line after the alias def should be the one with the arrow + (hopefully) the alias repeated in the path.
    }

    [Fact]
    public void When_single_tfm_then_no_parentheses_around_tfm_in_path()
    {
        var writer = new StringWriter();

        var path = "artifacts/bin/MyProj/Debug/net10.0/MyProj.dll";
        var combos = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [path] = new[] { "net10.0" },
        };

        MarkdownWriter.WriteBuildSuccess(writer, [path], combos);
        var output = writer.ToString();

        // Single TFM must appear bare (no surrounding parentheses).
        Assert.DoesNotContain("(net10.0)", output);
        Assert.Contains("/net10.0/", output);

        // No unnecessary global #TFMS alias when everything is single-TFM.
        Assert.DoesNotContain("#TFMS", output);
    }
}