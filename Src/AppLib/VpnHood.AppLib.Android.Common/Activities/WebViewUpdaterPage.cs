using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;

namespace VpnHood.AppLib.Droid.Common.Activities;

internal static class WebViewUpdaterPage
{
    public static void InitPage(Activity activity, Exception ex)
    {
        // get all current cpu architecture in a string
        var cpuArch = string.Join(", ", Build.SupportedAbis ?? []);
        var text =
            $"WebView initialization failed. Please update your Android System WebView and Chrome Browser. \r\n\r\n" +
            $"Error: {ex.Message} \r\n" +
            $"CPU Architecture: {cpuArch}";

        // Create a LinearLayout to hold the content
        var layout = new LinearLayout(activity) {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        layout.SetPadding(32, 32, 32, 32);
        layout.SetBackgroundColor(Color.White);

        // Create a TextView for the exception message
        var messageTextView = new TextView(activity) {
            Text = text,
            TextSize = 16,
            Gravity = GravityFlags.Left,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        messageTextView.SetTextColor(Color.Black);

        // Create a Button to open a URL
        var button = new Button(activity) {
            Text = "Update Android System WebView"
        };
        // Center-align the button
        var buttonLayoutParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent) {
            Gravity = GravityFlags.Center
        };
        buttonLayoutParams.SetMargins(16, 32, 16, 0); // Add margins for spacing
        button.LayoutParameters = buttonLayoutParams;

        // Add border and background color
        button.SetTextColor(Color.ParseColor("#6200EE"));
        button.SetPadding(32, 16, 32, 16); // Padding inside the button
        button.Background = GetBorderDrawable(Color.ParseColor("#6200EE"));

        // Set button click event to open a URL
        button.Click += (_, _) => {
            var url = "https://github.com/vpnhood/VpnHood/wiki/Update-Android-System-WebView-for-VpnHood-Android-App";
            var intent = new Intent(Intent.ActionView, Android.Net.Uri.Parse(url));
            activity.StartActivity(intent);
        };

        // Add views to the layout
        layout.AddView(messageTextView);
        layout.AddView(button);

        // Set the content view to the layout
        activity.SetContentView(layout);
    }

    // Helper method to create a border drawable for the button
    private static Drawable GetBorderDrawable(Color borderColor)
    {
        var shape = new GradientDrawable();
        shape.SetShape(ShapeType.Rectangle);
        shape.SetStroke(4, borderColor); // Border width and color
        shape.SetCornerRadius(16); // Rounded corners
        shape.SetColor(Color.LightGreen); // Transparent background
        return shape;
    }
}