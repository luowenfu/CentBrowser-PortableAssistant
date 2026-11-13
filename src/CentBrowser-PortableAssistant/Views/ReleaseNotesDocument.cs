using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using CentBrowser_PortableAssistant.Core.Update;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>把发行说明排成接近 GitHub 发行版正文的版式。</summary>
public static class ReleaseNotesDocument
{
    private static readonly FontFamily Prose = new("Segoe UI, Microsoft YaHei UI");
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Microsoft YaHei UI");
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x1F, 0x23, 0x28));
    private static readonly Brush Link = Freeze(Color.FromRgb(0x09, 0x69, 0xDA));
    private static readonly Brush CodeBack = Freeze(Color.FromRgb(0xEF, 0xF1, 0xF3));
    private static readonly Brush CodeBox = Freeze(Color.FromRgb(0xF6, 0xF8, 0xFA));
    private static readonly Brush Rule = Freeze(Color.FromRgb(0xD1, 0xD9, 0xE0));

    public static FlowDocument Create(string? markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = Prose,
            FontSize = 16,
            Foreground = Ink,
            PagePadding = new Thickness(2, 0, 8, 0),
            TextAlignment = TextAlignment.Left,
            Background = Brushes.White,
        };
        var blocks = ReleaseMarkdown.Parse(markdown);
        if (blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run("没有更新说明")) { Margin = new Thickness(0) });
            return document;
        }

        var first = true;
        foreach (var block in blocks)
        {
            document.Blocks.Add(Render(block, first));
            first = false;
        }
        return document;
    }

    private static Block Render(MdBlock block, bool first) => block switch
    {
        MdHeading heading => Heading(heading, first),
        MdList list => Items(list),
        MdCode code => Code(code.Text),
        MdParagraph paragraph => Paragraph(paragraph.Runs, new Thickness(0, 0, 0, 16)),
        _ => new Paragraph(),
    };

    private static Paragraph Heading(MdHeading heading, bool first)
    {
        var size = heading.Level switch
        {
            1 => 32d,
            2 => 24d,
            3 => 20d,
            _ => 16d,
        };
        var top = first ? 0 : 24;
        var bottom = heading.Level <= 2 ? 8 : 16;
        var paragraph = Paragraph(heading.Runs, new Thickness(0, top, 0, bottom));
        paragraph.FontSize = size;
        paragraph.FontWeight = FontWeights.SemiBold;
        paragraph.LineHeight = size * 1.25;
        if (heading.Level <= 2)
            paragraph.BorderBrush = Rule;
        paragraph.BorderThickness = heading.Level <= 2 ? new Thickness(0, 0, 0, 1) : new Thickness(0);
        paragraph.Padding = heading.Level <= 2 ? new Thickness(0, 0, 0, 8) : new Thickness(0);
        return paragraph;
    }

    private static List Items(MdList source)
    {
        var list = new List
        {
            MarkerStyle = source.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 16),
            Padding = new Thickness(8, 0, 0, 0),
        };
        foreach (var item in source.Items)
        {
            var paragraph = Paragraph(item, new Thickness(0, 4, 0, 4));
            paragraph.LineHeight = 24;
            list.ListItems.Add(new ListItem(paragraph));
        }
        return list;
    }

    private static BlockUIContainer Code(string text)
    {
        var box = new System.Windows.Controls.Border
        {
            Background = CodeBox,
            BorderBrush = Rule,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = text,
                FontFamily = Mono,
                FontSize = 13.5,
                Foreground = Ink,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        return new BlockUIContainer(box) { Margin = new Thickness(0, 0, 0, 16) };
    }

    private static Paragraph Paragraph(IReadOnlyList<MdRun> runs, Thickness margin)
    {
        var paragraph = new Paragraph
        {
            Margin = margin,
            LineHeight = 24,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        foreach (var run in runs)
        {
            if (run.Link is not null && Uri.TryCreate(run.Link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var link = new Hyperlink(new Run(run.Text))
                {
                    NavigateUri = uri,
                    Foreground = Link,
                    TextDecorations = TextDecorations.Underline,
                };
                link.RequestNavigate += OnNavigate;
                paragraph.Inlines.Add(link);
                continue;
            }

            var inline = new Run(run.Text) { FontWeight = run.Bold ? FontWeights.SemiBold : FontWeights.Normal };
            if (run.Italic) inline.FontStyle = FontStyles.Italic;
            if (run.Code)
            {
                inline.FontFamily = Mono;
                inline.FontSize = 13.5;
                inline.Background = CodeBack;
            }
            paragraph.Inlines.Add(inline);
        }
        return paragraph;
    }

    private static void OnNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
