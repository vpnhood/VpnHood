using System.Text;
using System.Text.RegularExpressions;

namespace VpnHood.AppUi.Services;

// The web UI's content documents (src/content/<lang>/*.md) are markdown its build renders to HTML.
// This renders the same subset - the front matter's title, paragraphs, headings, bullet and
// numbered lists, bold, links - to the markup RichText draws, so a document reads the same here.
public static partial class Markdown
{
    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Compiled)]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^\d+\.\s+", RegexOptions.Compiled)]
    private static partial Regex NumberedRegex();

    public static (string Title, string Markup) Render(string markdown)
    {
        var lines = markdown.Replace("\r", "").Split('\n');
        var title = "";
        var index = 0;

        // the front matter: title: "..." between two --- lines
        if (lines.Length > 0 && lines[0].Trim() == "---") {
            index = 1;
            while (index < lines.Length && lines[index].Trim() != "---") {
                var line = lines[index].Trim();
                if (line.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                    title = line["title:".Length..].Trim().Trim('"');
                index++;
            }
            index++;
        }

        var markup = new StringBuilder();
        var paragraph = new StringBuilder();
        string? listTag = null;

        void CloseParagraph()
        {
            if (paragraph.Length == 0) return;
            markup.Append("<p>").Append(paragraph).Append("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (listTag == null) return;
            markup.Append('<').Append('/').Append(listTag).Append('>');
            listTag = null;
        }

        for (; index < lines.Length; index++) {
            var line = lines[index].TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0) {
                CloseParagraph();
                CloseList();
                continue;
            }

            if (trimmed.StartsWith('#')) {
                CloseParagraph();
                CloseList();
                var level = Math.Min(4, trimmed.TakeWhile(x => x == '#').Count());
                markup.Append($"<h{level}>").Append(Inline(trimmed.TrimStart('#').Trim())).Append($"</h{level}>");
                continue;
            }

            var isBullet = trimmed.StartsWith("- ") || trimmed.StartsWith("* ");
            var isNumbered = NumberedRegex().IsMatch(trimmed);
            if (isBullet || isNumbered) {
                CloseParagraph();
                var tag = isBullet ? "ul" : "ol";
                if (listTag != tag) {
                    CloseList();
                    markup.Append('<').Append(tag).Append('>');
                    listTag = tag;
                }
                var item = isBullet ? trimmed[2..] : NumberedRegex().Replace(trimmed, "");
                markup.Append("<li>").Append(Inline(item.Trim())).Append("</li>");
                continue;
            }

            // a line continues the paragraph or the list item before it
            if (listTag != null && line.StartsWith(' ')) {
                markup.Append(' ').Append(Inline(trimmed));
                continue;
            }
            CloseList();
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(Inline(trimmed));
        }

        CloseParagraph();
        CloseList();
        return (title, markup.ToString());
    }

    private static string Inline(string text)
    {
        var escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        escaped = LinkRegex().Replace(escaped, "$1");
        return BoldRegex().Replace(escaped, "<strong>$1</strong>");
    }
}
