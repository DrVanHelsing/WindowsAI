namespace WindowsAI.Core.Search;

/// <summary>
/// Decides whether overlay input looks like a question/prompt for the AI rather than a
/// launcher query ("slack", "tax report.pdf"). Deliberately conservative: short token-ish
/// text is treated as a search, and a wrong guess is cheap — the results list always
/// carries an explicit "Ask AI" row, and zero search results fall through to the AI anyway.
/// </summary>
public static class PromptClassifier
{
    /// <summary>Interrogatives and imperative "AI verbs" that mark a leading word as prompt-like.</summary>
    private static readonly HashSet<string> PromptLeadingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // interrogatives / auxiliaries
        "how", "what", "whats", "why", "when", "where", "who", "which",
        "can", "could", "should", "would", "will", "do", "does", "did",
        "is", "are", "was", "were",
        // imperative AI verbs
        "write", "draft", "summarize", "summarise", "translate", "explain", "make",
        "create", "generate", "give", "tell", "fix", "help", "list", "compare",
        "convert", "plan", "suggest", "recommend", "calculate", "define", "rewrite",
        "improve", "brainstorm",
    };

    public static bool IsLikelyPrompt(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return false;

        if (text.Contains('?'))
            return true;

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // "write a thank-you note" — imperative/interrogative opener plus enough words
        // that it can't plausibly be a file or app name.
        if (words.Length >= 3 && PromptLeadingWords.Contains(words[0].TrimEnd(',', ':')))
            return true;

        // Long multi-word natural language is a prompt even without a marker word.
        return words.Length >= 7;
    }
}
