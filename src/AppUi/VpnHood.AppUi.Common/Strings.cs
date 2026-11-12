using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VpnHood.AppLib.Api.App;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppUi.Common;

// The UI's words: the locale files of the asset store (locales/<culture>.json - en.json the source
// a person edits, the rest its translations), read through the provider the head hands in - a folder
// in process, the app's web host from a browser page - so the words ship once, as files, the way
// the pictures do, and a brand is a package swap. Strings.g.cs names every key of en.json as a
// member (_sync-assets.ps1), so a page asks for a string in C# and a key that disappears is a
// compile error. An app built on this package lays its own words over these through AddSourceAsync.
//
// Loaded by InitAsync before the first view (IAvaloniaUi.PrepareContentAsync); a store with no
// words is a build with no store, and says so with the file's path.
//
// One instance, Current, and the pages bind to its members rather than reading them once:
//
//     Text="{Binding Connect, Source={x:Static res:Strings.Current}}"
//
// so a language chosen on the paired phone reaches a TV that is already showing the page - a
// property change with an empty name, which every binding on this object takes as its own.

/// <summary>
/// Provides the UI's words, from the locale files of the UI's content store, as members named after their keys.
/// </summary>
/// <remarks>
/// The pages bind to <see cref="Current"/>'s members, so a language chosen on a paired phone reaches a page already
/// showing: a property change with an empty name. An app built on this package lays its own words over these
/// (<see cref="AddSourceAsync"/>). Every generated word throws <see cref="InvalidOperationException"/> until
/// <see cref="InitAsync"/> has loaded the words.
/// </remarks>
public sealed partial class Strings : INotifyPropertyChanged
{
    private const string LocalesFolder = "locales";
    private const string EnglishName = "en";

    // The languages the store has words for, written beside them by _sync-assets.ps1: a list
    // rather than a listing, because a provider answers by name and never enumerates.
    private const string IndexPath = $"{LocalesFolder}/index.json";

    private static IAssetProvider? _assets;
    private static IReadOnlyDictionary<string, string>? _english;
    private static IReadOnlyList<string> _shippedCultures = [];
    private static readonly List<IStringSource> Sources = [];

    private IReadOnlyDictionary<string, string>? _texts;
    private string _cultureName = EnglishName;

    /// <summary>Gets the one instance the pages bind to.</summary>
    public static Strings Current { get; } = new();

    private static IAssetProvider Assets => _assets ?? throw NotLoaded();
    private static IReadOnlyDictionary<string, string> English => _english ?? throw NotLoaded();

    private static InvalidOperationException NotLoaded()
    {
        return new InvalidOperationException($"The words have not been loaded: {nameof(Strings)}.{nameof(InitAsync)} runs before the first view.");
    }

    /// <summary>
    /// Loads the words from the UI's content store: English, which every other language falls back to, and the languages
    /// there are files for.
    /// </summary>
    /// <param name="assets">The UI's content store.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>It runs before the first view, and on Android before every activity's; a language already shown stays.</remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.Text.Json.JsonException">A file of the store's words cannot be read.</exception>
    /// <exception cref="VpnHood.Net.Toolkit.Assets.AssetNotFoundException">The store has no English words or no index of its languages.</exception>
    /// <exception cref="InvalidOperationException">The store's English file holds no words.</exception>
    public static async Task InitAsync(IAssetProvider assets, CancellationToken cancellationToken)
    {
        _assets = assets;

        // English is the one language that must be there - every other falls back to it - so it is
        // asked for rather than looked up, and a store without it says so by name.
        await using (var english = await assets.OpenReadAsync(FilePathOf(EnglishName), cancellationToken).ConfigureAwait(false))
            _english = await JsonSerializer.DeserializeAsync(english, AssetJsonContext.Default.DictionaryStringString, cancellationToken).ConfigureAwait(false)
                       ?? throw new InvalidOperationException($"{FilePathOf(EnglishName)} holds no words.");
        _shippedCultures = await AssetIndex.ReadAsync(assets, IndexPath, cancellationToken).ConfigureAwait(false);
        await CountryNames.LoadEnglishAsync(assets, cancellationToken).ConfigureAwait(false);
        Current._texts ??= _english;
    }

    /// <summary>
    /// Gets the languages there are words for: the store's, by its index, and every source's added.
    /// </summary>
    public static IReadOnlyList<string> AvailableCultures {
        get {
            lock (Sources)
                return _shippedCultures.Concat(Sources.SelectMany(x => x.Cultures)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <inheritdoc/>
    /// <remarks>It is raised with an empty name when the words change, for every binding on this object.</remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    private Strings()
    {
    }

    /// <summary>
    /// Gets the language asked for last (<see cref="SetCultureAsync"/>), English at first; where neither the store nor an
    /// added source has it, the words shown are its language's alone, or English.
    /// </summary>
    public string CultureName => _cultureName;

    /// <summary>Gets the words shown now, by key, which the app is given for its own text.</summary>
    /// <exception cref="InvalidOperationException">The words have not been loaded (<see cref="InitAsync"/>).</exception>
    public IReadOnlyDictionary<string, string> Texts => _texts ?? English;

    /// <summary>Lays a source's words over the store's; the last source added speaks first.</summary>
    /// <param name="source">The source.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <remarks>It takes effect as soon as the words are loaded again, on a page already showing.</remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.Text.Json.JsonException">A file of the store's words cannot be read.</exception>
    /// <exception cref="InvalidOperationException">The words have not been loaded (<see cref="Strings.InitAsync"/>).</exception>
    public static async Task AddSourceAsync(IStringSource source, CancellationToken cancellationToken)
    {
        lock (Sources)
            Sources.Insert(0, source);
        await Current.Reload(CultureInfo.GetCultureInfo(Current._cultureName), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the language to show; where neither the store nor an added source has the culture, the words fall back to its
    /// language alone, then to English.
    /// </summary>
    /// <param name="culture">The culture: the app's answer, the person's choice or the device's.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is <see langword="true"/> where the language asked for differs from the one before, so a caller
    /// can rebuild text it composed.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="System.Text.Json.JsonException">A file of the store's words cannot be read.</exception>
    /// <exception cref="InvalidOperationException">The words have not been loaded (<see cref="InitAsync"/>).</exception>
    public async Task<bool> SetCultureAsync(CultureInfo culture, CancellationToken cancellationToken)
    {
        if (_cultureName == culture.Name)
            return false;

        _cultureName = culture.Name;
        await Reload(culture, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task Reload(CultureInfo culture, CancellationToken cancellationToken)
    {
        var texts = await Load(culture.Name, cancellationToken).ConfigureAwait(false)
                    ?? await Load(culture.TwoLetterISOLanguageName, cancellationToken).ConfigureAwait(false);
        await CountryNames.LoadAsync(Assets, culture, cancellationToken).ConfigureAwait(false);
        _texts = texts ?? English;
        // the direction of the words shown, not of the culture asked for: a language we have no
        // file for is shown in English, which runs left to right
        IsRightToLeft = texts != null && culture.TextInfo.IsRightToLeft;

        // no name: every binding on this object reads its property again
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>Gets a value indicating whether the words run right to left, as in Arabic and Persian.</summary>
    /// <remarks>The UI's root mirrors its layout by it; text, images, addresses, keys and figures are not mirrored.</remarks>
    public bool IsRightToLeft { get; private set; }

    /// <summary>Gets the chevron that points forward through the pages, turned for a right-to-left language.</summary>
    public string ChevronForward => IsRightToLeft ? Mdi.ChevronLeft : Mdi.ChevronRight;

    /// <summary>Gets the chevron that points back through the pages, turned for a right-to-left language.</summary>
    public string ChevronBack => IsRightToLeft ? Mdi.ChevronRight : Mdi.ChevronLeft;

    /// <summary>Gets the unit of speed, the same in every language.</summary>
    public string Mbps => "Mbps";

    /// <summary>Gets the words in place of the address the phone dials, before the listener has one.</summary>
    public string RemoteAccessStarting => "Starting…";

    /// <summary>Gets a connection's state in words, as the home page and the desktop's tray show it.</summary>
    /// <param name="connectionState">The state.</param>
    /// <returns>The words.</returns>
    /// <exception cref="InvalidOperationException">The words have not been loaded (<see cref="Strings.InitAsync"/>).</exception>
    public string ConnectionStateText(AppConnectionState connectionState)
    {
        return connectionState switch {
            AppConnectionState.None => Disconnected,
            AppConnectionState.Initializing => Initializing,
            AppConnectionState.WaitingForVpnPermission => Initializing, // the OS's own prompt covers the app
            AppConnectionState.Waiting => Waiting,
            AppConnectionState.Diagnosing => Diagnosing,
            AppConnectionState.ValidatingProxies => ValidatingProxies,
            AppConnectionState.Connecting => Connecting,
            AppConnectionState.Connected => Connected,
            AppConnectionState.Disconnecting => Disconnecting,
            AppConnectionState.WaitingForAd => LoadingAd,
            AppConnectionState.FindingReachableServer => FindingNetwork,
            AppConnectionState.FindingBestServer => FindingBestServer,
            AppConnectionState.Unstable => Unstable,
            AppConnectionState.Updating => Updating,
            _ => connectionState.ToString()
        };
    }

    private string Get(string key)
    {
        if ((_texts ?? English).TryGetValue(key, out var text))
            return text;

        // Strings.g.cs is generated from en.json, so a key missing from English means the two have
        // drifted: show the key rather than nothing, and trip a developer's build.
        Debug.Assert(English.ContainsKey(key), $"No locale key '{key}'. Run _sync-assets.ps1.");
        return English.GetValueOrDefault(key, key);
    }

    // the named placeholders: "Connected from {address}."
    private string Get(string key, params (string Name, object Value)[] arguments)
    {
        var text = Get(key);
        foreach (var (name, value) in arguments)
            text = text.Replace($"{{{name}}}", Convert.ToString(value, CultureInfo.CurrentCulture));
        return text;
    }

    // The words of a language: the store's file, with every source's words laid over it. A
    // language only a source has is that source over English. Null when nobody has it.
    private static async Task<IReadOnlyDictionary<string, string>?> Load(string cultureName, CancellationToken cancellationToken)
    {
        var texts = await LoadFile(cultureName, cancellationToken).ConfigureAwait(false);

        IStringSource[] sources;
        lock (Sources)
            sources = [.. Sources];

        // the first source added is laid last, so the last added speaks first
        foreach (var source in sources.Reverse()) {
            var extra = source.Load(cultureName);
            if (extra == null)
                continue;

            var merged = new Dictionary<string, string>(texts ?? English, StringComparer.Ordinal);
            foreach (var (key, text) in extra)
                merged[key] = text;
            texts = merged;
        }

        return texts;
    }

    private static string FilePathOf(string cultureName)
    {
        return $"{LocalesFolder}/{cultureName}.json";
    }

    private static async Task<IReadOnlyDictionary<string, string>?> LoadFile(string cultureName, CancellationToken cancellationToken)
    {
        await using var stream = await Assets.TryOpenReadAsync(FilePathOf(cultureName), cancellationToken).ConfigureAwait(false);
        return stream == null
            ? null
            : await JsonSerializer.DeserializeAsync(stream, AssetJsonContext.Default.DictionaryStringString, cancellationToken).ConfigureAwait(false);
    }
}
