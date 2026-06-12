using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace WindowsAI_App.Controls;

/// <summary>
/// Renders assistant message text with a hand-rolled markdown renderer: fenced code blocks
/// (glass card with language header + copy button), headings, bullet/numbered lists, tables,
/// blockquotes, horizontal rules, images (web + local), inline links, and inline
/// bold / italic / code / LaTeX-as-unicode. Built entirely in code — no XAML, no external
/// dependency — so it streams smoothly (only the last block is rebuilt while text grows) and
/// styles stay under the app's liquid-glass tokens.
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
    private sealed record QuoteBlock(string Text) : Block;
    private sealed record RuleBlock : Block;
    private sealed record ImageBlock(string Alt, string Source) : Block;

    // Records compare by value, but arrays compare by reference — wrap the table cells so the
    // streaming diff in Rebuild() treats an unchanged table as equal and doesn't re-render it.
    private sealed record TableBlock(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : Block
    {
        public bool Equals(TableBlock? other) =>
            other is not null && SameRow(Headers, other.Headers) &&
            Rows.Count == other.Rows.Count && Rows.Zip(other.Rows, SameRow).All(x => x);

        public override int GetHashCode() => Headers.Count ^ (Rows.Count << 8);

        private static bool SameRow(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
            a.Count == b.Count && a.SequenceEqual(b);
    }

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

            // Table — a pipe row immediately followed by a |---|:--:| separator row.
            if (trimmed.Contains('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                FlushParagraph();
                FlushList();

                var headers = SplitRow(trimmed);
                var rows = new List<IReadOnlyList<string>>();
                i += 2; // skip header + separator
                for (; i < lines.Length; i++)
                {
                    var rt = lines[i].Trim();
                    if (rt.Length == 0 || !rt.Contains('|'))
                        break;
                    rows.Add(SplitRow(rt));
                }
                i--; // the loop's i++ will re-advance past the last consumed row
                blocks.Add(new TableBlock(headers, rows));
                continue;
            }

            // Horizontal rule — a line of only -, *, or _ (3+), nothing else.
            if (IsHorizontalRule(trimmed))
            {
                FlushParagraph();
                FlushList();
                blocks.Add(new RuleBlock());
                continue;
            }

            // Blockquote — one or more consecutive "> " lines.
            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                FlushList();

                var quote = new List<string> { StripQuoteMarker(trimmed) };
                for (; i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith('>'); i++)
                    quote.Add(StripQuoteMarker(lines[i + 1].TrimStart()));
                blocks.Add(new QuoteBlock(string.Join("\n", quote)));
                continue;
            }

            // Standalone image line — ![alt](src) on its own.
            var imageOnly = StandaloneImage.Match(trimmed);
            if (imageOnly.Success)
            {
                FlushParagraph();
                FlushList();
                blocks.Add(new ImageBlock(imageOnly.Groups[1].Value, imageOnly.Groups[2].Value.Trim()));
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

    private static bool IsHorizontalRule(string t)
        => t.Length >= 3 && (t.All(c => c == '-') || t.All(c => c == '*') || t.All(c => c == '_'));

    /// <summary>A markdown table separator row, e.g. <c>| --- | :--: |</c> — each cell is dashes with optional colons.</summary>
    private static bool IsTableSeparator(string line)
    {
        var t = line.Trim();
        if (!t.Contains('-') || !t.Contains('|')) return false;
        return SplitRow(t).All(c => TableSeparatorCell.IsMatch(c.Trim()));
    }

    /// <summary>Splits a pipe row into trimmed cells, dropping the empty cells from outer pipes.</summary>
    private static List<string> SplitRow(string row)
    {
        var t = row.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|')) t = t[..^1];
        return t.Split('|').Select(c => c.Trim()).ToList();
    }

    private static string StripQuoteMarker(string trimmed)
        => trimmed.Length > 1 && trimmed[1] == ' ' ? trimmed[2..] : trimmed[1..];

    private static readonly Regex StandaloneImage = new(@"^!\[(.*?)\]\((.*?)\)$", RegexOptions.Compiled);
    private static readonly Regex TableSeparatorCell = new(@"^:?-+:?$", RegexOptions.Compiled);

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
        QuoteBlock quote  => RenderQuote(quote),
        RuleBlock         => RenderRule(),
        ImageBlock image  => RenderImage(image),
        TableBlock table  => RenderTable(table),
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

    private static Border RenderQuote(QuoteBlock quote)
    {
        var text = new TextBlock
        {
            FontSize = 15,
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = Brush("TextSecondaryBrush"),
        };
        AppendInlines(text.Inlines, quote.Text);

        var bar = new Border
        {
            Width = 3,
            Background = Brush("AccentBrush"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 2, 12, 2),
        };
        Grid.SetColumn(bar, 0);
        Grid.SetColumn(text, 1);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(bar);
        grid.Children.Add(text);

        return new Border
        {
            Background = Brush("GlassFillSubtleBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Child = grid,
        };
    }

    private static Border RenderRule() => new()
    {
        Height = 1,
        Background = Brush("HairlineBrush"),
        Margin = new Thickness(0, 4, 0, 4),
    };

    private static FrameworkElement RenderImage(ImageBlock block)
    {
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 420,
            MaxHeight = 320,
        };
        ToolTipService.SetToolTip(image, string.IsNullOrEmpty(block.Alt) ? block.Source : block.Alt);
        _ = LoadImageSourceAsync(image, block.Source);

        return new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderBrush = Brush("GlassStrokeSubtleBrush"),
            BorderThickness = new Thickness(1),
            Child = image,
        };
    }

    private static UIElement RenderTable(TableBlock table)
    {
        int cols = Math.Max(table.Headers.Count, table.Rows.Count > 0 ? table.Rows.Max(r => r.Count) : 0);
        if (cols == 0)
            return RenderParagraph(string.Empty);

        var grid = new Grid();
        for (int c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        for (int r = 0; r < table.Rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int c = 0; c < cols; c++)
            grid.Children.Add(TableCell(c < table.Headers.Count ? table.Headers[c] : string.Empty, 0, c, header: true));

        for (int r = 0; r < table.Rows.Count; r++)
            for (int c = 0; c < cols; c++)
                grid.Children.Add(TableCell(c < table.Rows[r].Count ? table.Rows[r][c] : string.Empty, r + 1, c, header: false));

        var scroll = new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
        };

        return new Border
        {
            Background = Brush("GlassFillSubtleBrush"),
            BorderBrush = Brush("GlassStrokeSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = scroll,
        };
    }

    private static Border TableCell(string text, int row, int col, bool header)
    {
        var tb = new TextBlock
        {
            FontSize = 14,
            LineHeight = 21,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(header ? "TextPrimaryBrush" : "TextSecondaryBrush"),
        };
        AppendInlines(tb.Inlines, text);

        var cell = new Border
        {
            // Hairline grid: a right + bottom border on every cell draws the full lattice.
            BorderBrush = Brush("HairlineBrush"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = header ? Brush("CodeHeaderFillBrush") : null,
            Padding = new Thickness(12, 8, 12, 8),
            Child = tb,
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, col);
        return cell;
    }

    private static async Task LoadImageSourceAsync(Image image, string source)
    {
        try
        {
            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                image.Source = new BitmapImage(new Uri(source));
                return;
            }

            // Local file path → load via the storage stream (BitmapImage can't open arbitrary paths).
            var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(source);
            using var stream = await file.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch
        {
            // Unreachable/invalid source — leave the frame empty rather than throwing into the UI.
        }
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
            // Inline image ![alt](src) — render the picture inline.
            if (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[')
            {
                var m = InlineImage.Match(text, i);
                if (m.Success && m.Index == i)
                {
                    FlushLiteral();
                    var image = new Image
                    {
                        Stretch = Stretch.Uniform,
                        MaxWidth = 320,
                        MaxHeight = 200,
                    };
                    _ = LoadImageSourceAsync(image, m.Groups[2].Value.Trim());
                    inlines.Add(new InlineUIContainer { Child = image });
                    i += m.Length;
                    continue;
                }
            }

            // Link [text](url) — opens in the default browser via the Hyperlink's NavigateUri.
            if (text[i] == '[')
            {
                var m = InlineLink.Match(text, i);
                if (m.Success && m.Index == i)
                {
                    FlushLiteral();
                    var label = m.Groups[1].Value;
                    var url = m.Groups[2].Value.Trim();
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        var link = new Hyperlink { NavigateUri = uri, Foreground = Brush("AccentTextBrush") };
                        link.Inlines.Add(new Run { Text = label });
                        inlines.Add(link);
                    }
                    else
                    {
                        AppendInlines(inlines, label); // not a real URL — keep the text, drop the syntax
                    }
                    i += m.Length;
                    continue;
                }
            }

            // Inline LaTeX: $…$ / $$…$$ / \(…\) / \[…\] → translated to unicode, shown in a math style.
            if (TryReadMath(text, i, out var mathInner, out var mathLen))
            {
                FlushLiteral();
                inlines.Add(new Run
                {
                    Text = TranslateLatex(mathInner),
                    FontFamily = new FontFamily("Cambria Math,Cambria,Georgia"),
                    Foreground = Brush("TextPrimaryBrush"),
                });
                i += mathLen;
                continue;
            }

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

    // ── Inline LaTeX → unicode ──────────────────────────────────────────────
    // Not a full math engine: enough to render the common inline math chat produces
    // (arrows, operators, Greek, super/subscripts) as readable unicode instead of leaking raw TeX.

    private static readonly Regex InlineLink = new(@"\[([^\]]*)\]\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex InlineImage = new(@"!\[(.*?)\]\((.*?)\)", RegexOptions.Compiled);

    /// <summary>Reads a math span at <paramref name="i"/> ($…$, $$…$$, \(…\), \[…\]); returns its inner text + total length.</summary>
    private static bool TryReadMath(string text, int i, out string inner, out int length)
    {
        inner = string.Empty;
        length = 0;

        // \(…\) and \[…\]
        if (text[i] == '\\' && i + 1 < text.Length && (text[i + 1] == '(' || text[i + 1] == '['))
        {
            var close = text[i + 1] == '(' ? @"\)" : @"\]";
            int end = text.IndexOf(close, i + 2, StringComparison.Ordinal);
            if (end > i)
            {
                inner = text[(i + 2)..end];
                length = end + 2 - i;
                return true;
            }
        }

        if (text[i] != '$')
            return false;

        // $$…$$ display math.
        if (i + 1 < text.Length && text[i + 1] == '$')
        {
            int end = text.IndexOf("$$", i + 2, StringComparison.Ordinal);
            if (end > i)
            {
                inner = text[(i + 2)..end].Trim();
                length = end + 2 - i;
                return true;
            }
            return false;
        }

        // $…$ inline math. Skip currency: require a non-space, non-digit right after the opener.
        if (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]) && !char.IsAsciiDigit(text[i + 1]))
        {
            int end = text.IndexOf('$', i + 1);
            if (end > i + 1)
            {
                inner = text[(i + 1)..end];
                length = end + 1 - i;
                return true;
            }
        }

        return false;
    }

    private static string TranslateLatex(string s)
    {
        // \frac{a}{b} → (a)/(b); \sqrt{x} → √(x) — applied before token replacement.
        s = Regex.Replace(s, @"\\frac\s*\{([^{}]*)\}\s*\{([^{}]*)\}", "($1)/($2)");
        s = Regex.Replace(s, @"\\sqrt\s*\{([^{}]*)\}", "√($1)");

        foreach (var (cmd, sym) in LatexSymbols)
            s = s.Replace(cmd, sym);

        // Superscripts / subscripts: ^{…}/_{…} and single-char ^x/_x, mapped to unicode where possible.
        s = Regex.Replace(s, @"\^\{([^{}]*)\}", m => Script(m.Groups[1].Value, sup: true));
        s = Regex.Replace(s, @"\^(\S)", m => Script(m.Groups[1].Value, sup: true));
        s = Regex.Replace(s, @"_\{([^{}]*)\}", m => Script(m.Groups[1].Value, sup: false));
        s = Regex.Replace(s, @"_(\S)", m => Script(m.Groups[1].Value, sup: false));

        // Drop any remaining braces / stray backslashes.
        return s.Replace("{", "").Replace("}", "").Replace("\\", "").Trim();
    }

    private static string Script(string text, bool sup)
    {
        var map = sup ? Superscripts : Subscripts;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text)
            sb.Append(map.TryGetValue(ch, out var u) ? u : (sup ? '^' + ch.ToString() : '_' + ch.ToString()));
        return sb.ToString();
    }

    // Common LaTeX commands → unicode. Longer commands first so e.g. \leftrightarrow wins over \left.
    private static readonly (string, string)[] LatexSymbols =
    [
        (@"\leftrightarrow", "↔"), (@"\Leftrightarrow", "⇔"), (@"\rightarrow", "→"), (@"\Rightarrow", "⇒"),
        (@"\leftarrow", "←"), (@"\Leftarrow", "⇐"), (@"\rightleftharpoons", "⇌"), (@"\to", "→"), (@"\mapsto", "↦"),
        (@"\times", "×"), (@"\div", "÷"), (@"\pm", "±"), (@"\mp", "∓"), (@"\cdot", "·"), (@"\ast", "∗"),
        (@"\leq", "≤"), (@"\geq", "≥"), (@"\neq", "≠"), (@"\approx", "≈"), (@"\equiv", "≡"), (@"\sim", "∼"),
        (@"\ll", "≪"), (@"\gg", "≫"), (@"\propto", "∝"), (@"\infty", "∞"), (@"\partial", "∂"), (@"\nabla", "∇"),
        (@"\sum", "∑"), (@"\prod", "∏"), (@"\int", "∫"), (@"\oint", "∮"), (@"\sqrt", "√"), (@"\angle", "∠"),
        (@"\in", "∈"), (@"\notin", "∉"), (@"\subset", "⊂"), (@"\subseteq", "⊆"), (@"\supset", "⊃"),
        (@"\cup", "∪"), (@"\cap", "∩"), (@"\emptyset", "∅"), (@"\forall", "∀"), (@"\exists", "∃"),
        (@"\wedge", "∧"), (@"\vee", "∨"), (@"\neg", "¬"), (@"\oplus", "⊕"), (@"\otimes", "⊗"),
        (@"\langle", "⟨"), (@"\rangle", "⟩"), (@"\lfloor", "⌊"), (@"\rfloor", "⌋"), (@"\ldots", "…"), (@"\dots", "…"),
        (@"\degree", "°"), (@"\circ", "∘"), (@"\bullet", "•"), (@"\star", "⋆"), (@"\prime", "′"),
        (@"\alpha", "α"), (@"\beta", "β"), (@"\gamma", "γ"), (@"\delta", "δ"), (@"\epsilon", "ε"), (@"\varepsilon", "ε"),
        (@"\zeta", "ζ"), (@"\eta", "η"), (@"\theta", "θ"), (@"\vartheta", "ϑ"), (@"\iota", "ι"), (@"\kappa", "κ"),
        (@"\lambda", "λ"), (@"\mu", "μ"), (@"\nu", "ν"), (@"\xi", "ξ"), (@"\pi", "π"), (@"\rho", "ρ"),
        (@"\sigma", "σ"), (@"\tau", "τ"), (@"\upsilon", "υ"), (@"\phi", "φ"), (@"\varphi", "φ"), (@"\chi", "χ"),
        (@"\psi", "ψ"), (@"\omega", "ω"), (@"\Gamma", "Γ"), (@"\Delta", "Δ"), (@"\Theta", "Θ"), (@"\Lambda", "Λ"),
        (@"\Xi", "Ξ"), (@"\Pi", "Π"), (@"\Sigma", "Σ"), (@"\Phi", "Φ"), (@"\Psi", "Ψ"), (@"\Omega", "Ω"),
        (@"\left", ""), (@"\right", ""), (@"\,", " "), (@"\;", " "), (@"\!", ""), (@"\quad", "  "), (@"\qquad", "    "),
    ];

    private static readonly Dictionary<char, char> Superscripts = new()
    {
        ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴', ['5'] = '⁵', ['6'] = '⁶',
        ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹', ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽',
        [')'] = '⁾', ['n'] = 'ⁿ', ['i'] = 'ⁱ', ['a'] = 'ᵃ', ['b'] = 'ᵇ', ['c'] = 'ᶜ', ['x'] = 'ˣ',
    };

    private static readonly Dictionary<char, char> Subscripts = new()
    {
        ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄', ['5'] = '₅', ['6'] = '₆',
        ['7'] = '₇', ['8'] = '₈', ['9'] = '₉', ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍',
        [')'] = '₎', ['a'] = 'ₐ', ['e'] = 'ₑ', ['i'] = 'ᵢ', ['j'] = 'ⱼ', ['n'] = 'ₙ', ['x'] = 'ₓ',
    };

    // ── Resource helpers ────────────────────────────────────────────────────

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key) =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];

    private static FontFamily Mono() => new("Cascadia Code,Cascadia Mono,Consolas");
}
