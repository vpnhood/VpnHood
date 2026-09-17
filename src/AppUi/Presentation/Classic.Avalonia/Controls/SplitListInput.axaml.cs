using Avalonia.Controls;
using VpnHood.AppLib.Assets;
using Avalonia;
using Avalonia.Layout;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Controls;

public partial class SplitListInput : UserControl
{
    // raised on every keystroke; the page compares against what it loaded when it leaves
    public event EventHandler? Changed;

    public SplitListInput()
    {
        InitializeComponent();
    }

    // the format hints of an IP list (SplitIpInput.vue)
    public void UseIpFormat(bool hasBlocks)
    {
        var s = Strings.Current;
        SetHints([
            (s.SingleIp, "192.168.1.1"),
            (s.RangesOfIp, "192.168.1.1-192.168.1.255"),
            (s.CidrNotation, "192.168.1.0/24"),
            (s.Comment, s.CommentDesc)
        ]);
        SetTitles(s.ExcludeIps, s.IncludeIps, s.BlockIps, s.SplitIpPlaceHolder, hasBlocks);
    }

    // the format hints of a domain list (SplitDomainInput.vue)
    public void UseDomainFormat()
    {
        var s = Strings.Current;
        SetHints([
            (s.ExactDomain, "example.com"),
            (s.WildcardDomain, "*.example.com"),
            (s.Comment, s.CommentDesc),
            (s.DomainRulesHint, null)
        ]);
        SetTitles(s.ExcludeDomains, s.IncludeDomains, s.BlockDomains, s.SplitDomainPlaceHolder, hasBlocks: true);
    }

    private void SetHints(IReadOnlyList<(string Label, string? Sample)> hints)
    {
        Hints.Children.Clear();
        foreach (var (label, sample) in hints) {
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            text.Classes.Add("body-small");
            text.Classes.Add("disabled");
            row.Children.Add(text);
            if (sample != null) {
                var chip = new Border { Child = new TextBlock { Text = sample } };
                chip.Classes.Add("chip");
                chip.Classes.Add("sample");
                row.Children.Add(chip);
            }
            Hints.Children.Add(row);
        }
    }

    private void SetTitles(string exclude, string include, string block, string placeholder, bool hasBlocks)
    {
        ExcludeTitle.Text = exclude;
        IncludeTitle.Text = include;
        BlockTitle.Text = block;
        ExcludeBox.PlaceholderText = placeholder;
        IncludeBox.PlaceholderText = placeholder;
        BlockBox.PlaceholderText = placeholder;
        BlockCard.IsVisible = hasBlocks;
    }

    public string Excludes {
        get => ExcludeBox.Text ?? "";
        set => ExcludeBox.Text = value;
    }

    public string Includes {
        get => IncludeBox.Text ?? "";
        set => IncludeBox.Text = value;
    }

    public string Blocks {
        get => BlockBox.Text ?? "";
        set => BlockBox.Text = value;
    }

    // dims the lists and blocks typing; the entries keep showing
    public bool IsDisabled {
        get => !IsEnabled;
        set {
            IsEnabled = !value;
            Opacity = value ? 0.5 : 1;
        }
    }

    public TextBox FirstBox => ExcludeBox;

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
