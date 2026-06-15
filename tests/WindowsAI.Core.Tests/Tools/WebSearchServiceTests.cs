using WindowsAI.Core.Tools;
using Xunit;

namespace WindowsAI.Core.Tests.Tools;

public class WebSearchServiceTests
{
    [Fact]
    public void Parse_maps_title_link_and_snippet_for_each_item()
    {
        const string json = """
        {
          "items": [
            { "title": "First result", "link": "https://example.com/a", "snippet": "Alpha snippet" },
            { "title": "Second result", "link": "https://example.com/b", "snippet": "Beta snippet" }
          ]
        }
        """;

        var results = WebSearchService.Parse(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("First result", results[0].Title);
        Assert.Equal("https://example.com/a", results[0].Url);
        Assert.Equal("Alpha snippet", results[0].Snippet);
        Assert.Equal("https://example.com/b", results[1].Url);
    }

    [Fact]
    public void Parse_returns_empty_when_there_are_no_items()
    {
        Assert.Empty(WebSearchService.Parse("{}"));
        Assert.Empty(WebSearchService.Parse("""{ "items": [] }"""));
    }

    [Fact]
    public void Parse_skips_items_without_a_link()
    {
        const string json = """
        {
          "items": [
            { "title": "No link here", "snippet": "orphan" },
            { "title": "Has link", "link": "https://example.com/ok", "snippet": "kept" }
          ]
        }
        """;

        var results = WebSearchService.Parse(json);

        Assert.Single(results);
        Assert.Equal("https://example.com/ok", results[0].Url);
    }

    [Fact]
    public void Parse_collapses_newlines_in_snippets()
    {
        const string json = """
        {
          "items": [
            { "title": "T", "link": "https://example.com", "snippet": "line one\nline two" }
          ]
        }
        """;

        var results = WebSearchService.Parse(json);

        Assert.Equal("line one line two", results[0].Snippet);
    }

    [Fact]
    public void Parse_falls_back_to_the_url_when_a_title_is_missing()
    {
        const string json = """
        {
          "items": [
            { "link": "https://example.com/titleless", "snippet": "s" }
          ]
        }
        """;

        var results = WebSearchService.Parse(json);

        Assert.Equal("https://example.com/titleless", results[0].Title);
    }
}
