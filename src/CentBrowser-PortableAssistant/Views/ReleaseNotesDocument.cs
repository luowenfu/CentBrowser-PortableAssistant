using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using CentBrowser_PortableAssistant.Core.Update;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>按 GitHub 发行说明的字号、行距和列表来排。</summary>
public static class ReleaseNotesDocument
{
    private static readonly FontFamily Prose = new("Segoe UI, Microsoft YaHei");
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Microsoft YaHei");
    private static readonly Brush Ink = Freeze(Color.FromRgb(0x1F, 0x23, 0x28));
    private static readonly Brush LinkBrush = Freeze(Color.FromRgb(0x09, 0x69, 0xDA));
    private static readonly Brush CodeBack = Freeze(Color.FromRgb(0xEF, 0xF1, 0xF3));
    private static readonly Brush CodeBox = Freeze(Color.FromRgb(0xF6, 0xF8, 0xFA));
    private static readonly Brush Rule = Freeze(Color.FromRgb(0xD1, 0xD9, 0xE0));

    public static UIElement Create(string? markdown)
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var blocks = ReleaseMarkdown.Parse(markdown);
        if (blocks.Count == 0)
        {
            root.Children.Add(Body("没有更新说明", new Thickness(0)));
            return root;
        }

        var first = true;
        foreach (var block in blocks)
        {
            root.Children.Add(Render(block, first));
            first = false;
        }
        return root;
    }

    private static UIElement Render(MdBlock block, bool first) => block switch
    {
        MdHeading heading => Heading(heading, first),
        MdList list => Items(list),
        MdCode code => Code(code.Text),
        MdParagraph paragraph => Text(paragraph.Runs, new Thickness(0, 0, 0, 16)),
        _ => new TextBlock(),
    };

    private static UIElement Heading(MdHeading heading, bool first)
    {
        var size = heading.Level switch
        {
            1 => 32d,
            2 => 24d,
            _ => 20d,
        };
        var text = Text(heading.Runs, new Thickness(0, first ? 0 : 24, 0, heading.Level <= 2 ? 8 : 16));
        text.FontSize = size;
        text.FontWeight = FontWeights.SemiBold;
        text.LineHeight = size * 1.25;
        if (heading.Level > 2) return text;

        var box = new StackPanel();
        box.Children.Add(text);
        box.Children.Add(new Border
        {
            Height = 1,
            Background = Rule,
            Margin = new Thickness(0, 0, 0, 16),
        });
        return box;
    }

    private static UIElement Items(MdList source)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var index = 1;
        foreach (var item in source.Items)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(source.Ordered ? 28 : 24) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Marker(source.Ordered, index));
            var text = Text(item, new Thickness(0));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
            index++;
        }
        return panel;
    }

    private static UIElement Marker(bool ordered, int index)
    {
        if (ordered)
        {
            return new TextBlock
            {
                Text = index + ".",
                FontFamily = Prose,
                FontSize = 16,
                Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 8, 0),
                LineHeight = 24,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
        }

        return new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 0, 0),
        };
    }

    private static UIElement Code(string text) => new Border
    {
        Background = CodeBox,
        BorderBrush = Rule,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(16),
        Margin = new Thickness(0, 0, 0, 16),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = Mono,
            FontSize = 13.5,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        },
    };

    private static TextBlock Body(string text, Thickness margin) => new()
    {
        Text = text,
        FontFamily = Prose,
        FontSize = 16,
        Foreground = Ink,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 24,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Margin = margin,
    };

    private static TextBlock Text(IReadOnlyList<MdRun> runs, Thickness margin)
    {
        var block = Body("", margin);
        foreach (var run in runs)
        {
            if (run.Link is not null && Uri.TryCreate(run.Link, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                var link = new Hyperlink(new Run(run.Text))
                {
                    NavigateUri = uri,
                    Foreground = LinkBrush,
                    TextDecorations = TextDecorations.Underline,
                };
                link.RequestNavigate += OnNavigate;
                block.Inlines.Add(link);
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
            block.Inlines.Add(inline);
        }
        return block;
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
