using Devlooped.Formatting;

namespace Tests;

public class NameShortenerTests
{
    [Fact]
    public void When_no_common_prefix_then_returns_full_names()
    {
        var shortener = new NameShortener();
        var names = shortener.ShortenMany(["Alpha.dll", "Beta.dll"]);

        Assert.Equal("Alpha.dll", names[0].Display);
        Assert.Equal("Beta.dll", names[1].Display);
        Assert.Null(names[0].Index);
        Assert.Empty(shortener.FormatFooter(names));
    }

    [Fact]
    public void When_single_name_then_returns_unshortened()
    {
        var shortener = new NameShortener();
        var names = shortener.ShortenMany(["Only.dll"]);

        Assert.Equal("Only.dll", names[0].Display);
        Assert.Null(names[0].Index);
        Assert.Empty(shortener.FormatFooter(names));
    }

    [Fact]
    public void When_shared_prefix_then_shortens_and_formats_footer()
    {
        var shortener = new NameShortener();
        var names = shortener.ShortenMany(
        [
            "Microsoft.Data.Ingestion.Api.dll",
            "Microsoft.Data.Ingestion.Web.dll",
            "Microsoft.Data.Ingestion.Tests.dll",
        ]);

        Assert.Equal("[1]Api.dll", names[0].WithIndex());
        Assert.Equal("[1]Web.dll", names[1].WithIndex());
        Assert.Equal("[1]Tests.dll", names[2].WithIndex());
        Assert.Equal("[1]: Microsoft.Data.Ingestion.", shortener.FormatFooter(names));
    }

    [Fact]
    public void When_empty_suffix_after_prefix_then_returns_unshortened()
    {
        var shortener = new NameShortener();
        var names = shortener.ShortenMany(["Foo.", "Foo.Bar"]);

        Assert.Equal("Foo.", names[0].Display);
        Assert.Equal("Foo.Bar", names[1].Display);
        Assert.Null(names[0].Index);
    }

    [Fact]
    public void When_multiple_prefix_groups_then_only_shortens_shared_groups()
    {
        var shortener = new NameShortener();
        var groupA = shortener.ShortenMany(["Contoso.Api.dll", "Contoso.Web.dll"]);
        var groupB = shortener.ShortenMany(["Fabrikam.Api.dll", "Fabrikam.Web.dll"]);

        Assert.Equal("[1]Api.dll", groupA[0].WithIndex());
        Assert.Equal("[1]Web.dll", groupA[1].WithIndex());
        Assert.Equal("[1]: Contoso.", shortener.FormatFooter(groupA));

        Assert.Equal("[2]Api.dll", groupB[0].WithIndex());
        Assert.Equal("[2]: Fabrikam.", shortener.FormatFooter(groupB));
    }

    [Fact]
    public void When_shortening_would_leave_suffix_without_dot_then_backs_off_to_leave_at_least_one_segment()
    {
        var shortener = new NameShortener();
        // "MyApp.dll" + "MyApp.Tests.dll" common "MyApp." would yield "dll" (no dot) -> backoff to no prefix
        var noShorten = shortener.ShortenMany(["MyApp.dll", "MyApp.Tests.dll"]);
        Assert.Equal("MyApp.dll", noShorten[0].Display);
        Assert.Equal("MyApp.Tests.dll", noShorten[1].Display);
        Assert.Null(noShorten[0].Index);

        // For FQNs, back off one segment so suffix retains a dot (e.g. "UnitTests.Foo" not "Foo")
        var shortener2 = new NameShortener();
        var names = shortener2.ShortenMany(
        [
            "MyCompany.MyApp.Tests.UnitTests.Fails",
            "MyCompany.MyApp.Tests.UnitTests.AlsoFails",
        ]);

        Assert.Equal("[1]UnitTests.Fails", names[0].WithIndex());
        Assert.Equal("[1]UnitTests.AlsoFails", names[1].WithIndex());
        Assert.Equal("[1]: MyCompany.MyApp.Tests.", shortener2.FormatFooter(names));
    }
}