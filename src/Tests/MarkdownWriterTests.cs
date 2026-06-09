using Devlooped.Formatting;
using System.Text;

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
    public void When_build_fails_then_writes_blockquoted_errors()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteBuildFailures(writer,
            [new BuildProjectErrors("md", ["src/Program.cs:12 CS1002: ; expected"])]);

        Assert.Equal(
            """
            ❌md
            > src/Program.cs:12 CS1002: ; expected

            """.ReplaceLineEndings(),
            writer.ToString());
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
    public void When_test_fails_then_writes_assembly_header_and_fenced_stack_trace()
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
            """
            ❌[2]Fails
            > ```csharp
            > Assert.True() Failure
            >    at MyCompany.MyApp.Tests.UnitTests.Fails() in src/Tests/Sample.cs:line 10
            > ```
            """.ReplaceLineEndings(),
            output);
        Assert.Contains("[1]: MyCompany.MyApp.", output);
        Assert.Contains("[2]: MyCompany.MyApp.Tests.UnitTests.", output);
    }

    [Fact]
    public void When_failure_has_message_only_then_writes_fenced_message()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteTestFailures(writer,
            [],
            [new TestFailure("MyApp.Tests.T", "boom", "")]);

        Assert.Contains(
            """
            ❌MyApp.Tests.T
            > ```csharp
            > boom
            > ```
            """.ReplaceLineEndings(),
            writer.ToString());
    }

    [Fact]
    public void When_failure_has_stack_only_then_writes_fenced_stack()
    {
        var writer = new StringWriter();
        MarkdownWriter.WriteTestFailures(writer,
            [],
            [new TestFailure("MyApp.Tests.T", "", "   at MyApp.Tests.T() in File.vb:line 1")]);

        Assert.Contains("> ```vb" + Environment.NewLine, writer.ToString());
        Assert.Contains(">    at MyApp.Tests.T() in File.vb:line 1", writer.ToString());
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
}