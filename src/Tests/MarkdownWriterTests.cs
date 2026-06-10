using System.Text;
using Devlooped.Formatting;

namespace Tests;

public class MarkdownWriterTests
{
    [Fact]
    public void When_build_succeeds_then_writes_shortened_outputs_with_footer()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteBuildSuccess(writer,
        [
            "Microsoft.Data.Ingestion.Api.dll",
            "Microsoft.Data.Ingestion.Web.dll",
        ]);

        var output = writer.ToString();
        Assert.Contains($"{Environment.NewLine}{Environment.NewLine}[1]: Microsoft.Data.Ingestion.", output);
        Assert.Equal(
            """
            ✅[1]Api.dll
            ✅[1]Web.dll

            [1]: Microsoft.Data.Ingestion.
            """.ReplaceLineEndings() + Environment.NewLine,
            output);
    }

    [Fact]
    public void When_build_fails_then_writes_tab_prefixed_errors()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteBuildFailures(writer,
            [new BuildProjectErrors("md", ["src/Program.cs:12 CS1002: ; expected"])]);

        var output = writer.ToString();
        Assert.Equal(
            "❌md" + Environment.NewLine + "\tsrc/Program.cs:12 CS1002: ; expected" + Environment.NewLine,
            output);
        // Explicitly assert the indent char for build error details is a single tab (char 9), never spaces.
        Assert.Equal('\t', output[output.IndexOf('\n') + 1]);
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

        // Shortening applied to successes (note: combined set with 'Mcp' causes backoff to keep '.' in all suffixes, per NameShortener rules)
        Assert.Contains("✅[1]AI.Core.dll", output);
        Assert.Contains("✅[1]AI.Core.UnitTests.dll", output);

        // Shortening applied to failure project header (using shared shortener across successes+failures)
        Assert.Contains("❌[1]AI.Mcp", output);

        // Shortening performed throughout, including inside build error details (prefix removed from path)
        Assert.Contains("\tAI.Mcp\\Class1.cs:5 CS1002: ; expected", output);

        // Single footer with refs at the very bottom, after all errors
        Assert.Contains("[1]: Microsoft.Agents.", output);
        var footerIdx = output.IndexOf("[1]: Microsoft.Agents.", StringComparison.Ordinal);
        var errorDetailIdx = output.IndexOf("AI.Mcp\\Class1.cs", StringComparison.Ordinal);
        Assert.True(footerIdx > errorDetailIdx, "refs footer must appear after the error details");

        // Confirm tab-indented shortened error detail is present (shortening throughout)
        Assert.Contains("\tAI.Mcp\\Class1.cs:5 CS1002: ; expected", output);

        // Explicit guard: the indent preceding the (shortened) build error detail line must be a single tab char.
        var detailPos = output.IndexOf("AI.Mcp\\Class1.cs", StringComparison.Ordinal);
        Assert.True(detailPos > 0 && output[detailPos - 1] == '\t', "Build error detail must be prefixed by single tab char, not spaces or other whitespace.");

        // There is a separating blank line between success block and failure block (consistent with test failures style)
        Assert.Contains("AI.Core.UnitTests.dll" + Environment.NewLine + Environment.NewLine + "❌[1]AI.Mcp", output);
    }
}