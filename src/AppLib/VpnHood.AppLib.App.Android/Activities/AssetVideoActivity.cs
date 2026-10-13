using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Views;
using Android.Widget;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.Android.Utils;

namespace VpnHood.AppLib.App.Android.Activities;

// Plays a video that ships as an asset, full screen, over whatever is showing - the shape every ad
// network uses for an interstitial, so a promotion of ours and a bought one look the same to the
// person watching, and the platform's own back button, rotation and lifecycle handling come free.
//
// The file is read where it lies, through the descriptor AssetManager gives for a STORED entry; it
// is never copied to storage. That costs nothing at run time and keeps the video once in the
// package. It does mean the entry must be stored rather than deflated - aapt leaves .mp4 alone by
// default, but a build that compressed it would fail here at run time and nowhere earlier.
//
// The screen is the video's, as an ad network's is: no status bar, the cutout and the see-through
// navigation bar drawn over. The video keeps its shape, fitted and centered, and the rest takes the
// colour the caller names - a film's own background, so a film shot for another shape still fills
// the screen.
//
// Deliberately knows nothing about ads: it is handed an asset path and reports how it ended. The
// countdown, the buttons and the reporting belong to whoever shows it.
[Activity(
    // Material: an older theme leaves the system bars' backgrounds to the system, black
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ExcludeFromRecents = true)]
public class AssetVideoActivity : Activity, ISurfaceHolderCallback
{
    private const string AssetPathExtra = "assetPath";
    private const string BackgroundColorExtra = "backgroundColor";
    private const int RequestCode = 0x5664; // 'Vd'

    private MediaPlayer? _mediaPlayer;
    private FrameLayout? _root;
    private SurfaceView? _surfaceView;
    private string? _assetPath;
    private int _videoWidth;
    private int _videoHeight;

    // Shows the video and returns when it is over: true when it played to the end, false when the
    // person left first. Faults if the asset cannot be opened or the video cannot be played, so a
    // caller that promised to show something knows it did not happen.
    public static async Task<bool> PlayAsync(IActivityEvent activityEvent, string assetPath,
        System.Drawing.Color backgroundColor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);

        var completion = new TaskCompletionSource<bool>();

        void OnResult(object? sender, ActivityResultEventArgs args)
        {
            if (args.RequestCode != RequestCode)
                return;

            activityEvent.ActivityResultEvent -= OnResult;

            var error = args.Data?.GetStringExtra(nameof(Exception));
            if (error != null)
                completion.TrySetException(new InvalidOperationException(error));
            else
                completion.TrySetResult(args.ResultCode == Result.Ok);
        }

        activityEvent.ActivityResultEvent += OnResult;
        try {
            var activity = activityEvent.Activity;
            var intent = new Intent(activity, typeof(AssetVideoActivity));
            intent.PutExtra(AssetPathExtra, assetPath);
            intent.PutExtra(BackgroundColorExtra, backgroundColor.ToArgb());
            // on the UI thread, as AdMob shows its ads: a caller may be on any thread
            await AndroidUtils.RunOnUiThread(activity, () => activity.StartActivityForResult(intent, RequestCode))
                .ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch {
            activityEvent.ActivityResultEvent -= OnResult;
            throw;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _assetPath = Intent?.GetStringExtra(AssetPathExtra);
        if (string.IsNullOrWhiteSpace(_assetPath)) {
            Finish(new InvalidOperationException($"{nameof(AssetVideoActivity)} was started with no asset path."));
            return;
        }

        var background = new global::Android.Graphics.Color(
            Intent?.GetIntExtra(BackgroundColorExtra, global::Android.Graphics.Color.Black.ToArgb()) ??
            global::Android.Graphics.Color.Black.ToArgb());
        Window?.SetBackgroundDrawable(new ColorDrawable(background));

        // The surface is not ready when the activity is created, so playback starts in SurfaceCreated;
        // it fills the screen until the video's size is known (FitVideo).
        _surfaceView = new SurfaceView(this);
        _surfaceView.Holder?.AddCallback(this);
        _root = new FrameLayout(this);
        _root.AddView(_surfaceView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
        _root.LayoutChange += (_, _) => _root.Post(FitVideo); // a rotation resizes the screen
        SetContentView(_root);
        FillScreen(); // after the content: the window's decor view must exist
    }

    // The background runs under the cutout and the navigation bar; the theme hides the status bar.
    // The navigation bar stays, see-through: hiding it too brings the system's own "Viewing full
    // screen" notice over the video the first time.
    private void FillScreen()
    {
        var window = Window;
        if (window == null)
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(28) && window.Attributes is { } attributes) {
            attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            window.Attributes = attributes;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            window.NavigationBarContrastEnforced = false;

        // Android 15 draws every app under a see-through navigation bar already
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
            window.SetNavigationBarColor(global::Android.Graphics.Color.Transparent);

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            window.SetDecorFitsSystemWindows(false);
        else
            window.DecorView.SystemUiFlags = SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation;
    }

    // the largest size of the video's own shape that fits the screen; the layout centers it
    private void FitVideo()
    {
        var root = _root;
        var surfaceView = _surfaceView;
        if (root == null || surfaceView == null || _videoWidth <= 0 || _videoHeight <= 0 ||
            root.Width <= 0 || root.Height <= 0)
            return;

        var scale = Math.Min((double)root.Width / _videoWidth, (double)root.Height / _videoHeight);
        var width = (int)Math.Round(_videoWidth * scale);
        var height = (int)Math.Round(_videoHeight * scale);
        if (surfaceView.LayoutParameters is { } current && current.Width == width && current.Height == height)
            return;

        surfaceView.LayoutParameters = new FrameLayout.LayoutParams(width, height, GravityFlags.Center);
    }

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        try {
            var assets = Assets ?? throw new InvalidOperationException("The activity has no asset manager.");
            using var descriptor = assets.OpenFd(_assetPath ?? throw new InvalidOperationException("No asset path."));

            var mediaPlayer = new MediaPlayer();
            _mediaPlayer = mediaPlayer;
            mediaPlayer.SetDisplay(holder);
            mediaPlayer.SetDataSource(descriptor.FileDescriptor!, descriptor.StartOffset, descriptor.Length);
            mediaPlayer.Completion += (_, _) => Finish(Result.Ok);
            mediaPlayer.Error += (_, args) => Finish(
                new InvalidOperationException($"The video could not be played. What: {args.What}, Extra: {args.Extra}"));
            mediaPlayer.VideoSizeChanged += (_, args) => {
                _videoWidth = args.Width;
                _videoHeight = args.Height;
                FitVideo();
            };
            mediaPlayer.Prepared += (_, _) => mediaPlayer.Start();
            mediaPlayer.PrepareAsync();
        }
        catch (Exception ex) {
            Finish(ex);
        }
    }

    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
    {
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _mediaPlayer?.SetDisplay(null);
    }

    // Leaving early is an answer, not a failure: the caller is told it was not watched to the end.
    protected override void OnPause()
    {
        base.OnPause();
        if (!IsFinishing)
            Finish(Result.Canceled);
    }

    protected override void OnDestroy()
    {
        var mediaPlayer = _mediaPlayer;
        _mediaPlayer = null;
        if (mediaPlayer != null) {
            mediaPlayer.Reset();
            mediaPlayer.Release();
            mediaPlayer.Dispose();
        }

        _surfaceView?.Holder?.RemoveCallback(this);
        base.OnDestroy();
    }

    private void Finish(Result result, Intent? data = null)
    {
        if (IsFinishing)
            return;

        SetResult(result, data);
        Finish();
    }

    private void Finish(Exception exception)
    {
        var data = new Intent();
        data.PutExtra(nameof(Exception), exception.Message);
        Finish(Result.Canceled, data);
    }
}
