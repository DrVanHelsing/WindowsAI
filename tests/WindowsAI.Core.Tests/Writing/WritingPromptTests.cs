using Microsoft.Extensions.AI;
using WindowsAI.Core.Writing;
using Xunit;

namespace WindowsAI.Core.Tests.Writing;

public class WritingPromptTests
{
    [Fact]
    public void All_actions_are_resolvable_and_unique()
    {
        var kinds = WritingAction.All.Select(a => a.Kind).ToList();
        Assert.Equal(kinds.Count, kinds.Distinct().Count());

        foreach (WritingActionKind kind in Enum.GetValues<WritingActionKind>())
            Assert.Equal(kind, WritingAction.For(kind).Kind);
    }

    [Fact]
    public void Build_produces_system_then_user_with_the_instruction_and_fenced_selection()
    {
        var action = WritingAction.For(WritingActionKind.Proofread);

        var messages = WritingPrompt.Build(action, "teh quick brown fox");

        Assert.Equal(2, messages.Count);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal(ChatRole.User, messages[1].Role);
        Assert.Contains(action.Instruction, messages[0].Text);
        // The selection is fenced so the model can tell content from instruction.
        Assert.Contains("<<<TEXT", messages[1].Text);
        Assert.Contains("teh quick brown fox", messages[1].Text);
        Assert.Contains("TEXT>>>", messages[1].Text);
    }

    [Fact]
    public void System_prompt_forbids_preamble_so_edge_models_dont_inject_chatter()
    {
        var messages = WritingPrompt.Build(WritingAction.For(WritingActionKind.Rewrite), "hello");
        var system = messages[0].Text;

        Assert.Contains("ONLY", system);
        Assert.Contains("no preamble", system);
    }

    [Theory]
    [InlineData("\"quoted result\"", "quoted result")]
    [InlineData("'single quoted'", "single quoted")]
    [InlineData("“curly quoted”", "curly quoted")]
    [InlineData("  surrounded by space  ", "surrounded by space")]
    [InlineData("plain text", "plain text")]
    public void CleanResult_strips_surrounding_quotes_and_whitespace(string raw, string expected)
        => Assert.Equal(expected, WritingPrompt.CleanResult(raw));

    [Fact]
    public void CleanResult_strips_a_markdown_code_fence()
    {
        const string raw = "```\nthe cleaned text\n```";
        Assert.Equal("the cleaned text", WritingPrompt.CleanResult(raw));
    }

    [Fact]
    public void CleanResult_strips_a_fence_with_a_language_tag()
    {
        const string raw = "```text\nbody line one\nbody line two\n```";
        Assert.Equal("body line one\nbody line two", WritingPrompt.CleanResult(raw));
    }

    [Fact]
    public void CleanResult_leaves_inner_quotes_untouched()
        => Assert.Equal("she said \"hi\" to me", WritingPrompt.CleanResult("she said \"hi\" to me"));
}
