using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace WindowsAI_App.Controls;

/// <summary>
/// Renders assistant message text with lightweight markdown support: fenced code blocks
/// (glass card with language header + copy button), headings, bullet/numbered lists, and
/// inline bold / italic / code. Built entirely in code — no XAML, no external dependency —
/// so it streams smoothly (only the last block is rebuilt while text grows) and styles
/// stay under the app's liquid-glass tokens.
/// </summary>
public sealed partial class MessageContentControl : ContentControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MessageContentControl),
        new PropertyMetadata(string.Empty, static (d, _) => ((MessageContentControl)d).Rebuild()));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private readonly StackPanel _host = new() { Spacing = 10 };
    private List<Block> _blocks = [];

    public MessageContentControl()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = _host;
    }

    // ── Block model ─────────────────────────────────────────────────────────

    private abstract record Block;
    private sealed record CodeBlock(string Language, string Code) : Block;
    private sealed record HeadingBlock(int Level, string Text) : Block;
    private sealed record ListBlock(bool Ordered, IReadOnlyList<string> Items) : Block;
    private sealed record ParagraphBlock(string Text) : Block;

    // ── Parsing ─────────────────────────────────────────────────────────────

    private static List<Block> Parse(string text)
    {
        var blocks = new List<Block>();
        if (string.IsNullOrEmpty(text))
            return blocks;

        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var paragraph = new List<string>();
        List<string>? listItems = null;
        bool listOrdered = false;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new ParagraphBlock(string.Join("\n", paragraph)));
                paragraph.Clear();
            }
        }

        void FlushList()
        {
            if (listItems is { Count: > 0 })
                blocks.Add(new ListBlock(listOrdered, listItems));
            listItems = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Fenced code block — an unclosed fence (still streaming) is treated as code.
            if (trimmed.StartsWith("```"))
            {
                FlushParagraph();
                FlushList();

                var language = trimmed[3..].Trim();
                var code = new List<string>();
                i++;
                for (; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("```"))
                        break;
                    code.Add(lines[i]);
                }
                blocks.Add(new CodeBlock(language, string.Join("\n", code)));
                continue;
            }

            // Heading
            if (trimmed.StartsWith('#'))
            {
                int level = 0;
                while (level < trimmed.Length && level < 4 && trimmed[level] == '#')
                    level++;
                if (level > 0 && level < trimmed.Length && trimmed[level] == ' ')
                {
                    FlushParagraph();
                    FlushList();
                    blocks.Add(new HeadingBlock(level, trimmed[(level + 1)..].Trim()));
                    continue;
                }
            }

            // List items (- / * / 1. / 1))
            var (isItem, ordered, itemText) = TryParseListItem(trimmed);
            if (isItem)
            {
                FlushParagraph();
                if (listItems is null || listOrdered != ordered)
                {
                    FlushList();
                    listItems = [];
                    listOrdered = ordered;
                }
                listItems.Add(itemText);
                continue;
            }

            // Blank line — paragraph/list separator
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            FlushList();
            paragraph.Add(line.TrimEnd());
        }

        FlushParagraph();
        FlushList();
        return blocks;
    }

    private static (bool isItem, bool ordered, string text) TryParseListItem(string trimmed)
    {
        if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*') && trimmed[1] == ' ')
            return (true, false, trimmed[2..]);

        int d = 0;
        while (d < trimmed.Length && char.IsAsciiDigit(trimmed[d]))
            d++;
        if (d is > 0 and <= 3 && d + 1 < trimmed.Length &&
            (trimmed[d] == '.' || trimmed[d] == ')') && trimmed[d + 1] == ' ')
            return (true, true, trimmed[(d + 2)..]);

        return (false, false, string.Empty);
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    private void Rebuild()
    {
        var fresh = Parse(Text);

        // Streaming fast path: if everything except the last block is unchanged,
        // update or append just the tail instead of rebuilding the whole panel.
        int stable = 0;
        while (stable < fresh.Count - 1 &&
               stable < _blocks.Count &&
               fresh[stable] == _blocks[stable])
            stable++;

        if (stable == _blocks.Count - 1 || stable == _blocks.Count)
        {
            for (int i = _host.Children.Count - 1; i >= stable; i--)
                _host.Children.RemoveAt(i);
            for (int i = stable; i < fresh.Count; i++)
                _host.Children.Add(Render(fresh[i]));
        }
        else
        {
            _host.Children.Clear();
            foreach (var block in fresh)
                _host.Children.Add(Render(block));
        }

        _blocks = fresh;
    }

    private static UIElement Render(Block block) => block switch
    {
        CodeBlock code    => RenderCode(code),
        HeadingBlock head => RenderHeading(head),
        ListBlock list    => RenderList(list),
        _                 => RenderParagraph(((ParagraphBlock)block).Text),
    };

    private static TextBlock RenderParagraph(string text)
    {
        var tb = new TextBlock
        {
            FontSize = 15,
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = Brush("TextPrimaryBrush"),
        };
        AppendInlines(tb.Inlines, text);
        return tb;
    }

    private static TextBlock RenderHeading(HeadingBlock head)
    {
        var tb = new TextBlock
        {
            FontSize = head.Level switch { 1 => 20, 2 => 18, 3 => 16, _ => 15 },
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = Brush("TextPrimaryBrush"),
            Margin = new Thickness(0, 6, 0, 0),
        };
        AppendInlines(tb.Inlines, head.Text);
        return tb;
    }

    private static StackPanel RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 5 };
        for (int i = 0; i < list.Items.Count; i++)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text = list.Ordered ? $"{i + 1}." : "•",
                FontSize = 15,
                MinWidth = 18,
                Foreground = Brush("AccentTextBrush"),
            };
            Grid.SetColumn(marker, 0);

            var item = new TextBlock
            {
                FontSize = 15,
                LineHeight = 24,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = Brush("TextPrimaryBrush"),
            };
            AppendInlines(item.Inlines, list.Items[i]);
            Grid.SetColumn(item, 1);

            row.Children.Add(marker);
            row.Children.Add(item);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static Border RenderCode(CodeBlock code)
    {
        var body = new TextBlock
        {
            Text = code.Code,
            FontFamily = Mono(),
            FontSize = 13,
            LineHeight = 20,
            IsTextSelectionEnabled = true,
            Foreground = Brush("TextPrimaryBrush"),
            Padding = new Thickness(14, 12, 14, 12),
        };

        var scroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        Grid.SetRow(scroll, 1);

        var header = BuildCodeHeader(code);
        Grid.SetRow(header, 0);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(header);
        grid.Children.Add(scroll);

        return new Border
        {
            Background = Brush("CodeFillBrush"),
            BorderBrush = Brush("GlassStrokeSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = grid,
        };
    }

    private static Grid BuildCodeHeader(CodeBlock code)
    {
        var header = new Grid
        {
            Background = Brush("CodeHeaderFillBrush"),
            Padding = new Thickness(14, 6, 6, 6),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lang = new TextBlock
        {
            Text = string.IsNullOrEmpty(code.Language) ? "code" : code.Language,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextSecondaryBrush"),
        };
        Grid.SetColumn(lang, 0);

        var copyIcon = new FontIcon { Glyph = "", FontSize = 12 };
        var copy = new Button
        {
            Content = copyIcon,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            Foreground = Brush("TextSecondaryBrush"),
        };
        ToolTipService.SetToolTip(copy, "Copy code");
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(code.Code);
            Clipboard.SetContent(package);

            copyIcon.Glyph = ""; // checkmark
            var timer = copy.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(1.2);
            timer.IsRepeating = false;
            timer.Tick += (_, _) => copyIcon.Glyph = "";
            timer.Start();
        };
        Grid.SetColumn(copy, 1);

        header.Children.Add(lang);
        header.Children.Add(copy);
        return header;
    }

    // ── Inline formatting: **bold**, *italic*, `code` ───────────────────────

    private static void AppendInlines(InlineCollection inlines, string text)
    {
        int i = 0;
        var literal = new System.Text.StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                inlines.Add(new Run { Text = literal.ToString() });
                literal.Clear();
            }
        }

        while (i < text.Length)
        {
            // `inline code`
            if (text[i] == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushLiteral();
                    inlines.Add(new Run
                    {
                        Text = text[(i + 1)..close],
                        FontFamily = Mono(),
                        FontSize = 13.5,
                        Foreground = Brush("AccentTextBrush"),
                    });
                    i = close + 1;
                    continue;
                }
            }

            // **bold**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                int close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i)
                {
                    FlushLiteral();
                    var bold = new Bold();
                    AppendInlines(bold.Inlines, text[(i + 2)..close]);
                    inlines.Add(bold);
                    i = close + 2;
                    continue;
                }
            }

            // *italic*
            if (text[i] == '*')
            {
                int close = text.IndexOf('*', i + 1);
                if (close > i + 1)
                {
                    FlushLiteral();
                    var italic = new Italic();
                    AppendInlines(italic.Inlines, text[(i + 1)..close]);
                    inlines.Add(italic);
                    i = close + 1;
                    continue;
                }
            }

            literal.Append(text[i]);
            i++;
        }

        FlushLiteral();
    }

    // ── Resource helpers ────────────────────────────────────────────────────

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];

    private static FontFamily Mono() => new("Cascadia Code,Cascadia Mono,Consolas");
}
