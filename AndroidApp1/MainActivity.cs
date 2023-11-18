namespace AndroidApp1
{
    [Activity(Label = "@string/app_name", MainLauncher = true)]
    public class MainActivity : Activity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Set our view from the "main" layout resource
            //SetContentView(Resource.Layout.activity_main);

            Window?.SetStatusBarColor(Android.Graphics.Color.Green);
            Window?.SetNavigationBarColor(Android.Graphics.Color.Green);
            return;
        }
    }
}