using WindowsAI.Core.Tools;
using Xunit;

namespace WindowsAI.Core.Tests.Tools;

public class WebSearchServiceTests
{
    [Fact]
    public void Parse_maps_title_url_and_content_for_each_result()
    {
        const string json = """
        {
          "results": [
            { "title": "First result", "url": "https://example.com/a", "content": "Alpha snippet" },
            { "title": "Second result", "url": "https://example.com/b", "content": "Beta snippet" }
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
    public void Parse_returns_empty_when_there_are_no_results()
    {
        Assert.Empty(WebSearchService.Parse("{}"));
        Assert.Empty(WebSearchService.Parse("""{ "results": [] }"""));
    }

    [Fact]
    public void Parse_skips_results_without_a_url()
    {
        const string json = """
        {
          "results": [
            { "title": "No url here", "content": "orphan" },
            { "title": "Has url", "url": "https://example.com/ok", "content": "kept" }
          ]
        }
        """;

        var results = WebSearchService.Parse(json);

        Assert.Single(results);
        Assert.Equal("https://example.com/ok", results[0].Url);
    }

    [Fact]
    public void Parse_collapses_newlines_in_content()
    {
        const string json = """
        {
          "results": [
            { "title": "T", "url": "https://example.com", "content": "line one\nline two" }
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
          "results": [
            { "url": "https://example.com/titleless", "content": "s" }
          ]
        }
        """;

        var results = WebSearchService.Parse(json);

        Assert.Equal("https://example.com/titleless", results[0].Title);
    }
}
