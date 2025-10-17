using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Win.Common.WpfSpa;

internal class WpfAppLoader
{
    public static void Init(Window window)
    {
        try {
            window.Content = Create();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to initialize loading screen.");
        }
    }

    public static UIElement Create()
    {
        // Create main container
        var grid = new Grid();

        // Set window background color
        var backgroundColor = VpnHoodApp.Instance.Resources.Colors.WindowBackgroundColor;
        if (backgroundColor != null) {
            var color = Color.FromArgb(backgroundColor.Value.A, backgroundColor.Value.R,
                backgroundColor.Value.G, backgroundColor.Value.B);
            VhUtils.TryInvoke("grid.Background", () =>
                grid.Background = new SolidColorBrush(color));
        }

        // Create progress bar
        var progressBar = new ProgressBar {
            IsIndeterminate = true,
            Height = 4,
            Margin = new Thickness(60, 0, 60, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Set progress bar color
        var progressBarColor = VpnHoodApp.Instance.Resources.Colors.ProgressBarColor;
        if (progressBarColor != null) {
            var color = Color.FromArgb(progressBarColor.Value.A, progressBarColor.Value.R,
                progressBarColor.Value.G, progressBarColor.Value.B);
            VhUtils.TryInvoke("progressBar.Foreground", () =>
                progressBar.Foreground = new SolidColorBrush(color));
        }

        // Add progress bar to grid
        grid.Children.Add(progressBar);

        return grid;
    }
}