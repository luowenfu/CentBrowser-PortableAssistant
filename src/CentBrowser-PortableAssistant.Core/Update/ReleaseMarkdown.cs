namespace CentBrowser_PortableAssistant.Core.Update;

public abstract record MdBlock;

public sealed record MdHeading(int Level, IReadOnlyList<MdRun> Runs) : MdBlock;

public sealed record MdParagraph(IReadOnlyList<MdRun> Runs) : MdBlock;

public sealed record MdList(bool Ordered, IReadOnlyList<IReadOnlyList<MdRun>> Items) : MdBlock;

public sealed record MdCode(string Text) : MdBlock;

public sealed record MdRun(string Text, bool Bold = false, bool Italic = false, bool Code = false, string? Link = null);

/// <summary>发行说明里用到的 Markdown：标题、段落、列表、代码块，以及加粗、行内代码和链接。</summary>
public static class ReleaseMarkdown
{
    public static IReadOnlyList<MdBlock> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<MdBlock>();
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;
                blocks.Add(new MdCode(string.Join('\n', code)));
                continue;
            }

            if (TryHeading(line, out var level, out var heading))
            {
                blocks.Add(new MdHeading(level, ParseInlines(heading)));
                i++;
                continue;
            }

            if (TryItem(line, out var ordered, out var item))
            {
                var items = new List<IReadOnlyList<MdRun>>();
                var kind = ordered;
                while (i < lines.Length && TryItem(lines[i], out ordered, out item) && ordered == kind)
                {
                    var text = item;
                    i++;
                    while (i < lines.Length && IsContinuation(lines[i]))
                    {
                        text += " " + lines[i].Trim();
                        i++;
                    }
                    items.Add(ParseInlines(text));
                }
                blocks.Add(new MdList(kind, items));
                continue;
            }

            var paragraph = new List<string>();
            while (i < lines.Length && IsParagraphLine(lines[i]))
            {
                paragraph.Add(lines[i].Trim());
                i++;
            }
            blocks.Add(new MdParagraph(ParseInlines(string.Join(' ', paragraph))));
        }
        return blocks;
    }

    public static IReadOnlyList<MdRun> ParseInlines(string text)
    {
        var runs = new List<MdRun>();
        var plain = new System.Text.StringBuilder();
        var i = 0;

        void Flush()
        {
            if (plain.Length == 0) return;
            runs.Add(new MdRun(plain.ToString()));
            plain.Clear();
        }

        while (i < text.Length)
        {
            if (TrySpan(text, i, "`", "`", out var code, out var next))
            {
                Flush();
                runs.Add(new MdRun(code, Code: true));
                i = next;
                continue;
            }
            if (TrySpan(text, i, "**", "**", out var bold, out next))
            {
                Flush();
                runs.Add(new MdRun(bold, Bold: true));
                i = next;
                continue;
            }
            if (TrySpan(text, i, "*", "*", out var italic, out next) && italic.Length > 0 && italic[0] != ' ' && italic[^1] != ' ')
            {
                Flush();
                runs.Add(new MdRun(italic, Italic: true));
                i = next;
                continue;
            }
            if (TryLink(text, i, out var label, out var url, out next))
            {
                Flush();
                runs.Add(new MdRun(label, Link: url));
                i = next;
                continue;
            }
            plain.Append(text[i]);
            i++;
        }
        Flush();
        return runs;
    }

    private static bool IsParagraphLine(string line) =>
        !string.IsNullOrWhiteSpace(line)
        && !line.TrimStart().StartsWith("```", StringComparison.Ordinal)
        && !TryHeading(line, out _, out _)
        && !TryItem(line, out _, out _);

    private static bool IsContinuation(string line) =>
        line.StartsWith("  ", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(line)
        && !TryItem(line, out _, out _);

    private static bool TryHeading(string line, out int level, out string text)
    {
        level = 0;
        text = "";
        var i = 0;
        while (i < line.Length && line[i] == '#') i++;
        if (i is < 1 or > 6 || i >= line.Length || line[i] != ' ') return false;
        level = i;
        text = line[(i + 1)..].Trim();
        return text.Length > 0;
    }

    private static bool TryItem(string line, out bool ordered, out string text)
    {
        ordered = false;
        text = "";
        var i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        if (i > 3 || i >= line.Length) return false;
        var rest = line[i..];
        if (rest[0] is '-' or '*' or '+')
        {
            if (rest.Length < 2 || rest[1] != ' ') return false;
            text = rest[2..].Trim();
            return true;
        }
        var n = 0;
        while (n < rest.Length && char.IsDigit(rest[n])) n++;
        if (n is 0 or > 9 || n + 1 >= rest.Length || rest[n] is not ('.' or ')') || rest[n + 1] != ' ') return false;
        ordered = true;
        text = rest[(n + 2)..].Trim();
        return true;
    }

    private static bool TrySpan(string text, int start, string open, string close, out string inner, out int next)
    {
        inner = "";
        next = start;
        if (!text.AsSpan(start).StartsWith(open)) return false;
        var from = start + open.Length;
        var end = text.IndexOf(close, from, StringComparison.Ordinal);
        if (end < 0) return false;
        inner = text[from..end];
        next = end + close.Length;
        return true;
    }

    private static bool TryLink(string text, int start, out string label, out string url, out int next)
    {
        label = "";
        url = "";
        next = start;
        if (text[start] != '[') return false;
        var close = text.IndexOf(']', start + 1);
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(') return false;
        var urlEnd = text.IndexOf(')', close + 2);
        if (urlEnd < 0) return false;
        label = text[(start + 1)..close];
        url = text[(close + 2)..urlEnd].Trim();
        next = urlEnd + 1;
        return label.Length > 0 && url.Length > 0;
    }
}
