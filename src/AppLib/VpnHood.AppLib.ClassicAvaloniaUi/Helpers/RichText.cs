using System.Net;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace VpnHood.AppLib.ClassicAvaloniaUi.Helpers;

// Some of the web UI's strings carry markup - a word in the premium colour, a bold name, a short
// document with headings and bullets - which it renders with v-html. This puts the same marks on a
// TextBlock's inlines: the few tags the strings use, the classes the theme names, and nothing else.
internal static partial class RichText
{
    [GeneratedRegex("<(/?)([a-zA-Z0-9]+)([^>]*)>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex("class\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled)]
    private static partial Regex ClassRegex();

    public static void Apply(TextBlock block, string markup)
    {
        var inlines = new InlineCollection();
        var brushStack = new Stack<IBrush?>();
        var boldDepth = 0;
        var sizeStack = new Stack<double?>();
        var position = 0;
        var atLineStart = true;

        foreach (Match match in TagRegex().Matches(markup)) {
            AddText(markup[position..match.Index]);
            position = match.Index + match.Length;

            var isClosing = match.Groups[1].Value == "/";
            var tag = match.Groups[2].Value.ToLowerInvariant();
            var attributes = match.Groups[3].Value;

            switch (tag) {
                case "span":
                    if (isClosing) { PopBrush(); PopSize(); }
                    else { brushStack.Push(BrushOf(attributes, block)); sizeStack.Push(SizeOf(attributes)); }
                    break;
                case "strong":
                case "b":
                    boldDepth += isClosing ? -1 : 1;
                    break;
                case "br":
                    AddBreak();
                    break;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                    if (isClosing) { boldDepth--; AddBreak(); }
                    else { boldDepth++; }
                    break;
                case "p":
                    if (isClosing) AddBreak();
                    break;
                case "ul":
                case "ol":
                    if (isClosing) AddBreak();
                    break;
                case "li":
                    if (isClosing) AddBreak();
                    else AddText("\u2022 ");
                    break;
                // a link's address is kept as its text; a TV cannot follow it anyway
                case "a":
                    break;
            }
        }
        AddText(markup[position..]);

        // the trailing break of a closing paragraph or heading
        while (inlines.Count > 0 && inlines[^1] is LineBreak)
            inlines.RemoveAt(inlines.Count - 1);

        block.Inlines = inlines;
        return;

        void AddText(string text)
        {
            if (text.Length == 0)
                return;
            var run = new Run(WebUtility.HtmlDecode(text.Replace("\r", "").Replace("\n", " ")));
            if (brushStack.Count > 0 && brushStack.Peek() is { } brush)
                run.Foreground = brush;
            if (boldDepth > 0)
                run.FontWeight = FontWeight.Bold;
            if (sizeStack.Count > 0 && sizeStack.Peek() is { } size)
                run.FontSize = size;
            inlines.Add(run);
            atLineStart = false;
        }

        void AddBreak()
        {
            if (atLineStart && inlines.Count == 0)
                return;
            inlines.Add(new LineBreak());
            atLineStart = true;
        }

        void PopBrush()
        {
            if (brushStack.Count > 0) brushStack.Pop();
        }

        void PopSize()
        {
            if (sizeStack.Count > 0) sizeStack.Pop();
        }
    }

    // the theme's text colours, by the class the web UI's stylesheet names them
    private static IBrush? BrushOf(string attributes, Control context)
    {
        var classes = ClassRegex().Match(attributes).Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in classes) {
            var key = name switch {
                "text-promote-premium-color-premium" => "PromotePremiumColorPremiumBrush",
                "text-highlight" => "HighlightBrush",
                "text-active" => "ActiveBrush",
                "text-error" => "ErrorBrush",
                "text-white" => "WhiteBrush",
                "text-disabled" => "AppDisabledBrush",
                _ => null
            };
            if (key != null && context.TryFindResource(key, out var value) && value is IBrush brush)
                return brush;
        }
        return null;
    }

    private static double? SizeOf(string attributes)
    {
        var classes = ClassRegex().Match(attributes).Groups[1].Value;
        return classes.Contains("text-label-large") ? 14 : null;
    }
}
