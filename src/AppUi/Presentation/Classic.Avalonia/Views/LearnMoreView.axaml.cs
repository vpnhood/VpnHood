using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using VpnHood.AppLib.Assets;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

public partial class LearnMoreView : UserControl, IPage
{
    public LearnMoreView(MainView host)
    {
        _ = host;
        InitializeComponent();

        var s = Strings.Current;
        AddSection(s.LearnMoreFreeServersDisruptions1, s.LearnMoreFreeServersDisruptions2);
        AddSection(s.LearnMoreFreeServersDisruptions3, s.LearnMoreFreeServersDisruptions4);
        AddSection(s.LearnMoreFreeServersDisruptions5, s.LearnMoreFreeServersDisruptions6);
        AddSection(s.LearnMoreFreeServersDisruptions7, s.LearnMoreFreeServersDisruptions8);
        AddTitle(s.LearnMoreFreeServersDisruptions9);
        AddBullet(s.LearnMoreFreeServersDisruptions10, s.LearnMoreFreeServersDisruptions11);
        AddBullet(s.LearnMoreFreeServersDisruptions12, s.LearnMoreFreeServersDisruptions13);
        AddBullet(s.LearnMoreFreeServersDisruptions14, s.LearnMoreFreeServersDisruptions15);
        AddBullet(s.LearnMoreFreeServersDisruptions16, s.LearnMoreFreeServersDisruptions17);
    }

    public void FocusDefault()
    {
        Header.FocusBack();
    }

    private void AddSection(string title, string text)
    {
        AddTitle(title);
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0) };
        body.Classes.Add("body-medium");
        body.Classes.Add("disabled");
        Sections.Children.Add(body);
        var rule = new Border { Margin = new Thickness(0, 20) };
        rule.Classes.Add("divider");
        Sections.Children.Add(rule);
    }

    private void AddTitle(string title)
    {
        var block = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        block.Classes.Add("h3");
        block.Classes.Add("highlight");
        Sections.Children.Add(block);
    }

    private void AddBullet(string lead, string text)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 0, 10) };
        block.Classes.Add("body-medium");
        block.Classes.Add("disabled");
        block.Inlines = [
            new Run("• "),
            new Run(lead + " ") { Foreground = Brushes.White },
            new Run(text)
        ];
        Sections.Children.Add(block);
    }
}
