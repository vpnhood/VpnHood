using System.Text;
using VpnHood.AppLib.App;

namespace VpnHood.AppUi.Hosting.Cli.Commands;

// The service's log as the API answers it: the app's part, then the VPN service's, each under a
// header of its own (VpnHoodApp.CopyLogToStream). Both grow, so an answer never begins with the one
// before: each part is followed apart. A part that no longer begins with what was printed of it has
// started again, as both do at a connect, and is printed again from the top; new lines of the part
// not printed last come under its header, so the reader knows whose they are.
internal sealed class ServiceLogFollower
{
    private const int HeaderLineCount = 3;
    private readonly string[] _parts = ["", ""];
    private int _lastPrinted = -1;
    private bool _atLineStart = true;

    // The first answer: each part's header and its last lines.
    public string First(string answer, int lines)
    {
        var parts = Split(answer);
        var text = new StringBuilder();
        for (var i = 0; i < parts.Length; i++) {
            var header = Header(parts[i]);
            Print(text, i, header + LastLines(parts[i][header.Length..], lines));
        }

        parts.CopyTo(_parts, 0);
        return text.ToString();
    }

    // What is new since the answer before.
    public string Next(string answer)
    {
        var parts = Split(answer);
        var text = new StringBuilder();
        for (var i = 0; i < parts.Length; i++) {
            var part = parts[i];
            var printed = _parts[i];
            if (printed.Length > 0 && part.StartsWith(printed, StringComparison.Ordinal)) {
                var added = part[printed.Length..];
                if (added.Length > 0 && _lastPrinted != i)
                    Print(text, i, Header(part));
                Print(text, i, added);
            }
            else {
                // new, or started again: from the top, header and all
                Print(text, i, part);
            }

            _parts[i] = part;
        }

        return text.ToString();
    }

    // A part begins on a line of its own.
    private void Print(StringBuilder text, int part, string value)
    {
        if (value.Length == 0)
            return;

        if (part != _lastPrinted && !_atLineStart)
            text.Append('\n');

        text.Append(value);
        _lastPrinted = part;
        _atLineStart = value.EndsWith('\n');
    }

    // At the VPN service's header, whose part begins with the line of dashes above its name. The empty
    // line written between the parts belongs to neither: the app's part would grow by it when the
    // other part first came.
    private static string[] Split(string answer)
    {
        var name = answer.IndexOf("\n" + VpnHoodApp.LogVpnServiceHeader, StringComparison.Ordinal);
        if (name < 0)
            return [answer, ""];

        var start = name > 0 ? answer.LastIndexOf('\n', name - 1) + 1 : 0;
        var app = answer[..start];
        app = app.EndsWith("\r\n\r\n", StringComparison.Ordinal) ? app[..^2]
            : app.EndsWith("\n\n", StringComparison.Ordinal) ? app[..^1]
            : app;

        return [app, answer[start..]];
    }

    // The lines that name a part - dashes, its name, dashes - or nothing where it has none.
    private static string Header(string part)
    {
        if (!part.StartsWith('-'))
            return "";

        var end = 0;
        for (var i = 0; i < HeaderLineCount; i++) {
            var lineBreak = part.IndexOf('\n', end);
            if (lineBreak < 0)
                return "";

            end = lineBreak + 1;
        }

        return part[..end];
    }

    // The last lines of a text, not counting the empty ones it ends with: an entry of the log ends with
    // one, and "the last line" is its message.
    private static string LastLines(string text, int lines)
    {
        if (lines <= 0)
            return "";

        var start = text.Length;
        while (start > 0 && text[start - 1] is '\n' or '\r')
            start--;

        for (var i = 0; i < lines; i++) {
            var lineBreak = start > 0 ? text.LastIndexOf('\n', start - 1) : -1;
            if (lineBreak < 0)
                return text;

            start = lineBreak;
        }

        return text[(start + 1)..];
    }
}
