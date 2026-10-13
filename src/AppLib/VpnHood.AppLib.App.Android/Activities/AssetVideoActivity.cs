using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using VpnHood.AppLib.App.Android.Utils;
using VpnHood.Core.Client.Devices.Android.ActivityEvents;
using VpnHood.Core.Client.Devices.Android.Utils;
using Orientation = Android.Widget.Orientation;

namespace VpnHood.AppLib.App.Android.Activities;

// Plays a video that ships as an asset, full screen, over whatever is showing - the shape every ad
// network uses for an interstitial, so a promotion of ours and a bought one look the same to the
// person watching, and the platform's own rotation and lifecycle handling come free.
//
// The file is read where it lies, through the descriptor AssetManager gives for a STORED entry; it
// is never copied to storage. That costs nothing at run time and keeps the video once in the
// package. It does mean the entry must be stored rather than deflated - aapt leaves .mp4 alone by
// default, but a build that compressed it would fail here at run time and nowhere earlier.
//
// The screen is the video's, as an ad network's is: no status bar, the cutout and the see-through
// navigation bar drawn over. The video keeps its shape, fitted and centered between the controls,
// and the rest takes the colour the caller names - a film's own background, so a film shot for
// another shape still fills the screen.
//
// It runs to its end before it can be closed, as an ad network's does: the seconds left count down
// in the corner and become a close mark, Close and the caller's action below wake at the end, and
// Back waits as well. The video then stays on its last frame until the person chooses. Away before
// the end - Home, another app - it pauses and plays on when the person is back, as the main
// activity's launch mode lets the app's icon bring it back; away after the end closes it.
//
// Deliberately knows nothing about ads: the caller names the words and the colour, decides what
// the action does, and is told how it ended.
[Activity(
    // Material: an older theme leaves the system bars' backgrounds to the system, black
    Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden,
    ExcludeFromRecents = true)]
public class AssetVideoActivity : Activity, ISurfaceHolderCallback, View.IOnApplyWindowInsetsListener
{
    private const string AssetPathExtra = "assetPath";
    private const string BackgroundColorExtra = "backgroundColor";
    private const string CloseTextExtra = "closeText";
    private const string ActionTextExtra = "actionText";
    private const string CanCloseEarlyExtra = "canCloseEarly";
    private const string ResultExtra = "result";
    private const int RequestCode = 0x5664; // 'Vd'
    private const long TickMilliseconds = 250;
    private const string CloseMark = "✕";
    private const float PillRadius = 10000; // more than half of any height: the ends are round

    private static readonly Color WhiteTonal = Color.Argb(0x29, 0xFF, 0xFF, 0xFF);
    private static readonly Color RippleColor = Color.Argb(0x40, 0x80, 0x80, 0x80);

    private MediaPlayer? _mediaPlayer;
    private FrameLayout? _videoFrame;
    private SurfaceView? _surfaceView;
    private View? _videoCover;
    private Button? _countButton;
    private LinearLayout? _buttonRow;
    private Button? _closeButton;
    private Button? _actionButton;
    private AndroidBackInvokedCallback? _backCallback;
    private string? _assetPath;
    private bool _canCloseEarly;
    private bool _isPrepared;
    private bool _hasSurface;
    private bool _isAway;
    private int? _resumeAt;
    private bool _isEnded;
    private int _videoWidth;
    private int _videoHeight;

    // Shows the video and returns how it ended. Faults if the asset cannot be opened or the video
    // cannot be played, so a caller that promised to show something knows it did not happen.
    public static async Task<AssetVideoResult> PlayAsync(IActivityEvent activityEvent, AssetVideoParams videoParams,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);
        ArgumentNullException.ThrowIfNull(videoParams);
        ArgumentException.ThrowIfNullOrWhiteSpace(videoParams.AssetPath);

        var completion = new TaskCompletionSource<AssetVideoResult>();

        void OnResult(object? sender, ActivityResultEventArgs args)
        {
            if (args.RequestCode != RequestCode)
                return;

            activityEvent.ActivityResultEvent -= OnResult;

            var error = args.Data?.GetStringExtra(nameof(Exception));
            if (error != null)
                completion.TrySetException(new InvalidOperationException(error));
            else
                completion.TrySetResult(ReadResult(args));
        }

        activityEvent.ActivityResultEvent += OnResult;
        try {
            var activity = activityEvent.Activity;
            var intent = new Intent(activity, typeof(AssetVideoActivity));
            intent.PutExtra(AssetPathExtra, videoParams.AssetPath);
            intent.PutExtra(BackgroundColorExtra, videoParams.BackgroundColor.ToArgb());
            intent.PutExtra(CloseTextExtra, videoParams.CloseText);
            intent.PutExtra(ActionTextExtra, videoParams.ActionText);
            intent.PutExtra(CanCloseEarlyExtra, videoParams.CanCloseEarly);
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

    // the answer the screen gave; a finish it did not make itself - the system's - is a leave
    private static AssetVideoResult ReadResult(ActivityResultEventArgs args)
    {
        return args.ResultCode == Result.Ok && args.Data != null
            ? (AssetVideoResult)args.Data.GetIntExtra(ResultExtra, (int)AssetVideoResult.Left)
            : AssetVideoResult.Left;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var intent = Intent;
        _assetPath = intent?.GetStringExtra(AssetPathExtra);
        if (intent == null || string.IsNullOrWhiteSpace(_assetPath)) {
            Finish(new InvalidOperationException($"{nameof(AssetVideoActivity)} was started with no asset path."));
            return;
        }

        _canCloseEarly = intent.GetBooleanExtra(CanCloseEarlyExtra, false);
        var background = new Color(intent.GetIntExtra(BackgroundColorExtra, Color.Black.ToArgb()));
        Window?.SetBackgroundDrawable(new ColorDrawable(background));

        // The surface is not ready when the activity is created, so playback starts in SurfaceCreated;
        // it fills its room until the video's size is known (FitVideo).
        _surfaceView = new SurfaceView(this);
        _surfaceView.Holder?.AddCallback(this);
        var videoFrame = new FrameLayout(this);
        videoFrame.AddView(_surfaceView, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
        videoFrame.LayoutChange += (_, _) => videoFrame.Post(FitVideo); // a rotation resizes it
        _videoFrame = videoFrame;

        // the background's colour over a new surface, black until the video is back at its frame (PlayOn)
        _videoCover = new View(this) { Visibility = ViewStates.Gone };
        _videoCover.SetBackgroundColor(background);
        videoFrame.AddView(_videoCover, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        // the corner: the seconds left once the video's length is known, then the close mark
        _countButton = CreateButton("", WhiteTonal, Color.White, isBold: false);
        _countButton.SetPadding(0, 0, 0, 0);
        _countButton.Visibility = ViewStates.Invisible;
        _countButton.Enabled = _canCloseEarly;
        _countButton.Click += (_, _) => TryClose();

        _closeButton = CreateButton(intent.GetStringExtra(CloseTextExtra) ?? "", WhiteTonal, Color.White, isBold: false);
        _closeButton.Click += (_, _) => Finish(AssetVideoResult.Closed);
        // white, its words in the background's colour
        _actionButton = CreateButton(intent.GetStringExtra(ActionTextExtra) ?? "", Color.White,
            Color.Rgb(background.R, background.G, background.B), isBold: true);
        _actionButton.Click += (_, _) => Finish(AssetVideoResult.Action);

        // the focus rings take 4 dp of each pill's side, so 4 dp between them makes 12
        _buttonRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _buttonRow.AddView(_closeButton, new LinearLayout.LayoutParams(0, Dp(52), 1));
        _buttonRow.AddView(new Space(this), new LinearLayout.LayoutParams(Dp(4), 0));
        _buttonRow.AddView(_actionButton, new LinearLayout.LayoutParams(0, Dp(52), 1));
        SetButtonsAwake(false);

        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.AddView(_countButton, new LinearLayout.LayoutParams(Dp(44), Dp(44)) {
            Gravity = GravityFlags.End, TopMargin = Dp(8), MarginEnd = Dp(8)
        });
        content.AddView(videoFrame, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1) {
            TopMargin = Dp(8), BottomMargin = Dp(8)
        });
        content.AddView(_buttonRow, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) {
            LeftMargin = Dp(12), RightMargin = Dp(12), BottomMargin = Dp(12)
        });
        content.SetOnApplyWindowInsetsListener(this);
        SetContentView(content);
        FillScreen(); // after the content: the window's decor view must exist

        // Android 16 hands an app that targets it Back through this rather than as a key
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) {
            _backCallback = new AndroidBackInvokedCallback(TryClose);
            OnBackInvokedDispatcher.RegisterOnBackInvokedCallback(0, _backCallback);
        }
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
            window.SetNavigationBarColor(Color.Transparent);

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            window.SetDecorFitsSystemWindows(false);
        else
            window.DecorView.SystemUiFlags = SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation;
    }

    // The controls keep clear of the cutout and of the navigation bar the background runs under.
    public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30)) {
            var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            view.SetPadding(safe.Left, safe.Top, safe.Right, safe.Bottom);
        }
        else {
            var cutout = OperatingSystem.IsAndroidVersionAtLeast(28) ? insets.DisplayCutout : null;
            view.SetPadding(
                Math.Max(insets.SystemWindowInsetLeft, cutout?.SafeInsetLeft ?? 0),
                Math.Max(insets.SystemWindowInsetTop, cutout?.SafeInsetTop ?? 0),
                Math.Max(insets.SystemWindowInsetRight, cutout?.SafeInsetRight ?? 0),
                Math.Max(insets.SystemWindowInsetBottom, cutout?.SafeInsetBottom ?? 0));
        }

        return insets;
    }

    // the largest size of the video's own shape that fits its room; the layout centers it
    private void FitVideo()
    {
        var videoFrame = _videoFrame;
        var surfaceView = _surfaceView;
        if (videoFrame == null || surfaceView == null || _videoWidth <= 0 || _videoHeight <= 0 ||
            videoFrame.Width <= 0 || videoFrame.Height <= 0)
            return;

        var scale = Math.Min((double)videoFrame.Width / _videoWidth, (double)videoFrame.Height / _videoHeight);
        var width = (int)Math.Round(_videoWidth * scale);
        var height = (int)Math.Round(_videoHeight * scale);
        if (surfaceView.LayoutParameters is { } current && current.Width == width && current.Height == height)
            return;

        surfaceView.LayoutParameters = new FrameLayout.LayoutParams(width, height, GravityFlags.Center);
    }

    // Plays from where it is, once the video is ready, has a surface and the person is here.
    private void PlayOn()
    {
        var mediaPlayer = _mediaPlayer;
        if (mediaPlayer == null || !_isPrepared || !_hasSurface || _isAway || _isEnded || IsFinishing ||
            mediaPlayer.IsPlaying)
            return;

        // A new surface gets a new decoder, which starts at a key frame - for a film with few, its
        // first - so the seek decodes its way back to the frame, and takes a moment.
        if (_resumeAt is { } position) {
            _resumeAt = null;
            if (_videoCover != null)
                _videoCover.Visibility = ViewStates.Visible;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
                mediaPlayer.SeekTo(position, MediaPlayerSeekMode.Closest);
            else
                mediaPlayer.SeekTo(position);
        }

        mediaPlayer.Start();
        Tick();
    }

    // the seconds left, counted down in the corner while the video plays
    private void Tick()
    {
        var mediaPlayer = _mediaPlayer;
        var countButton = _countButton;
        if (_isEnded || IsFinishing || mediaPlayer == null || countButton == null || !mediaPlayer.IsPlaying)
            return;

        var remaining = (mediaPlayer.Duration - mediaPlayer.CurrentPosition) / 1000.0;
        countButton.Text = Math.Max(1, (int)Math.Ceiling(remaining)).ToString();
        countButton.Visibility = ViewStates.Visible;
        countButton.PostDelayed(Tick, TickMilliseconds);
    }

    // The end: the count becomes the close mark, the buttons wake, and a remote's focus lands on
    // Close, so a stray OK never takes the action. The video stays on its last frame.
    private void ShowEnded()
    {
        if (_isEnded || IsFinishing)
            return;

        _isEnded = true;
        if (_countButton != null) {
            _countButton.Text = CloseMark;
            _countButton.ContentDescription = _closeButton?.Text;
            _countButton.Enabled = true;
            _countButton.Visibility = ViewStates.Visible;
        }

        SetButtonsAwake(true);
        _closeButton?.RequestFocus();
    }

    // asleep while the video runs: dimmed, and deaf to a touch or a remote
    private void SetButtonsAwake(bool isAwake)
    {
        if (_buttonRow == null || _closeButton == null || _actionButton == null)
            return;

        _buttonRow.Alpha = isAwake ? 1 : 0.5f;
        _closeButton.Enabled = isAwake;
        _actionButton.Enabled = isAwake;
    }

    // the close mark and Back: nothing until the end, unless the caller lets it close early
    private void TryClose()
    {
        if (_isEnded || _canCloseEarly)
            Finish(AssetVideoResult.Closed);
    }

    // Back as a key: a remote's, and a phone's where Android still sends it as one
    public override bool OnKeyDown([GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        if (keyCode != Keycode.Back)
            return base.OnKeyDown(keyCode, e);

        TryClose();
        return true;
    }

    // a pill: none of the theme's capitals, shadow or least size
    private Button CreateButton(string text, Color fill, Color textColor, bool isBold)
    {
        var button = new Button(this) {
            Text = text,
            Background = CreatePillBackground(fill),
            StateListAnimator = null
        };
        button.SetAllCaps(false);
        button.SetTextColor(textColor);
        button.SetTextSize(ComplexUnitType.Sp, 15);
        button.SetTypeface(Typeface.Default, isBold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        button.SetMinWidth(0);
        button.SetMinimumWidth(0);
        button.SetMinHeight(0);
        button.SetMinimumHeight(0);
        button.SetPadding(Dp(16), 0, Dp(16), 0);
        return button;
    }

    // The pill in a ring that shows where a remote's focus is - outside the fill, so it shows on a
    // white pill too - under a touch's ripple.
    private RippleDrawable CreatePillBackground(Color fill)
    {
        var states = new StateListDrawable();
        states.AddState([global::Android.Resource.Attribute.StateFocused], CreatePill(fill, Color.White));
        states.AddState([], CreatePill(fill, Color.Transparent));
        return new RippleDrawable(ColorStateList.ValueOf(RippleColor), states, null);
    }

    private LayerDrawable CreatePill(Color fill, Color ringColor)
    {
        var ring = CreateShape(Color.Transparent);
        ring.SetStroke(Dp(2), ringColor);
        var pill = new LayerDrawable([ring, CreateShape(fill)]);
        pill.SetLayerInset(1, Dp(4), Dp(4), Dp(4), Dp(4));
        return pill;
    }

    private static GradientDrawable CreateShape(Color color)
    {
        var shape = new GradientDrawable();
        shape.SetCornerRadius(PillRadius);
        shape.SetColor(color);
        return shape;
    }

    private int Dp(int value)
    {
        return (int)Math.Round(value * (Resources?.DisplayMetrics?.Density ?? 1));
    }

    // The first surface starts the video; a later one - the person back from away - takes it over.
    public void SurfaceCreated(ISurfaceHolder holder)
    {
        _hasSurface = true;
        if (_mediaPlayer != null) {
            _mediaPlayer.SetDisplay(holder);
            PlayOn();
            return;
        }

        try {
            var assets = Assets ?? throw new InvalidOperationException("The activity has no asset manager.");
            using var descriptor = assets.OpenFd(_assetPath ?? throw new InvalidOperationException("No asset path."));

            var mediaPlayer = new MediaPlayer();
            _mediaPlayer = mediaPlayer;
            mediaPlayer.SetDisplay(holder);
            mediaPlayer.SetScreenOnWhilePlaying(true); // a screen timeout shorter than the video waits
            mediaPlayer.SetDataSource(descriptor.FileDescriptor!, descriptor.StartOffset, descriptor.Length);
            mediaPlayer.Completion += (_, _) => ShowEnded();
            mediaPlayer.SeekComplete += (_, _) => {
                if (_videoCover != null)
                    _videoCover.Visibility = ViewStates.Gone;
            };
            mediaPlayer.Error += (_, args) => Finish(
                new InvalidOperationException($"The video could not be played. What: {args.What}, Extra: {args.Extra}"));
            mediaPlayer.VideoSizeChanged += (_, args) => {
                _videoWidth = args.Width;
                _videoHeight = args.Height;
                FitVideo();
            };
            mediaPlayer.Prepared += (_, _) => {
                _isPrepared = true;
                PlayOn();
            };
            mediaPlayer.PrepareAsync();
        }
        catch (Exception ex) {
            Finish(ex);
        }
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
    {
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        _hasSurface = false;
        _mediaPlayer?.SetDisplay(null);
    }

    // Away is no way out: before the end the video pauses until the person is back. After it, the
    // video has been watched, so away closes it.
    protected override void OnPause()
    {
        base.OnPause();
        if (IsFinishing)
            return;

        if (_isEnded) {
            Finish(AssetVideoResult.Closed);
            return;
        }

        _isAway = true;
        if (_isPrepared && _mediaPlayer is { IsPlaying: true } mediaPlayer) {
            mediaPlayer.Pause();
            _resumeAt = mediaPlayer.CurrentPosition;
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _isAway = false;
        PlayOn();
    }

    protected override void OnDestroy()
    {
        if (_backCallback != null && OperatingSystem.IsAndroidVersionAtLeast(33)) {
            OnBackInvokedDispatcher.UnregisterOnBackInvokedCallback(_backCallback);
            _backCallback.Dispose();
            _backCallback = null;
        }

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

    private void Finish(AssetVideoResult result)
    {
        var data = new Intent();
        data.PutExtra(ResultExtra, (int)result);
        Finish(Result.Ok, data);
    }

    private void Finish(Exception exception)
    {
        var data = new Intent();
        data.PutExtra(nameof(Exception), exception.Message);
        Finish(Result.Canceled, data);
    }

    private void Finish(Result result, Intent data)
    {
        if (IsFinishing)
            return;

        SetResult(result, data);
        Finish();
    }
}
