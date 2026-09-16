using Avalonia.Controls;
using Avalonia.Media.Imaging;
using VpnHood.AppLib.AvaloniaUI.Helpers;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.AvaloniaUI.ViewModels;
using VpnHood.AppLib.Settings;

namespace VpnHood.AppLib.AvaloniaUI.Views;

public partial class SplitAppsView : UserControl, IPage, ILeaveGuard
{
    // the row standing for apps installed later (split-apps.vue's "$")
    private const string FutureAppsId = "$";

    private readonly MainView _host;
    private IReadOnlyList<FilterItem> _items = [];

    public SplitAppsView(MainView host)
    {
        _host = host;
        InitializeComponent();
        List.IsLoading = true;
        _ = Load();
    }

    private static SplitTunnelingSettings Split => AppData.UserSettings.SplitTunneling;

    // the icons come as PNG bytes; decoding a few hundred of them is off the UI thread
    private async Task Load()
    {
        try {
            var mode = Split.AppMode;
            var selected = Split.Apps;
            var installed = await AppData.Api.App.GetInstalledApps(CancellationToken.None);

            var items = await Task.Run(() => installed.Select(app => new FilterItem {
                Id = app.AppId,
                Name = app.AppName,
                Icon = DecodePng(app.IconPng),
                IsSelected = mode == SplitAppMode.All
                             || (mode == SplitAppMode.Include && selected.Contains(app.AppId))
                             || (mode == SplitAppMode.Exclude && !selected.Contains(app.AppId))
            }).ToList());

            items.Add(new FilterItem {
                Id = FutureAppsId,
                Name = Strings.Current.AllFutureApps,
                Icon = FutureAppsIcon(),
                IsSelected = mode is SplitAppMode.All or SplitAppMode.Exclude
            });

            _items = Sort(items);
            List.Items = _items;
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
        finally {
            List.IsLoading = false;
        }
    }

    // split-apps.vue's sortApps: the enabled first when future apps are in, the disabled first when
    // they are not; "All future apps" ahead of its group; then by name
    private static IReadOnlyList<FilterItem> Sort(IReadOnlyList<FilterItem> items)
    {
        var isFutureSelected = items.Any(x => x is { Id: FutureAppsId, IsSelected: true });
        return [.. items.OrderBy(x => isFutureSelected ? x.IsSelected ? 1 : 0 : x.IsSelected ? 0 : 1)
            .ThenBy(x => x.Id == FutureAppsId ? 0 : 1)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static Bitmap? DecodePng(string base64)
    {
        try {
            using var stream = new MemoryStream(Convert.FromBase64String(base64));
            return new Bitmap(stream);
        }
        catch (Exception) {
            return null;
        }
    }

    // the web UI's UiConstants.futureAppsIcon*, one per product
    private static Bitmap FutureAppsIcon()
    {
        return AppAssets.Image(AppData.IsConnectApp ? "future-apps-connect.png" : "future-apps-client.png");
    }

    // Include with nothing in it is the one state that cannot be saved: the app would tunnel
    // nothing. It is held until the person leaves, and refused then (ALL_APPS_EXCLUDED_ERROR_MSG).
    private static bool IsSaveRejected => Split is { AppMode: SplitAppMode.Include, Apps.Length: 0 };

    private async void OnSelectionChanged(object? sender, EventArgs e)
    {
        try {
            var items = _items;
            if (items.All(x => x.IsSelected)) {
                Split.AppMode = SplitAppMode.All;
                Split.Apps = [];
                await AppData.SaveUserSettings(AppData.UserSettings, CancellationToken.None);
                return;
            }

            if (items.Any(x => x is { Id: FutureAppsId, IsSelected: true })) {
                Split.AppMode = SplitAppMode.Exclude;
                Split.Apps = [.. items.Where(x => !x.IsSelected && x.Id != FutureAppsId).Select(x => x.Id)];
                await AppData.SaveUserSettings(AppData.UserSettings, CancellationToken.None);
                return;
            }

            Split.AppMode = SplitAppMode.Include;
            Split.Apps = [.. items.Where(x => x.IsSelected && x.Id != FutureAppsId).Select(x => x.Id)];
            if (!IsSaveRejected)
                await AppData.SaveUserSettings(AppData.UserSettings, CancellationToken.None);
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
        }
    }

    public async Task<bool> CanLeave()
    {
        if (IsSaveRejected) {
            await _host.ShowError(Strings.Current.AllAppsExcludedErrorMsg);
            return false;
        }

        try {
            await AppData.SaveUserSettings(AppData.UserSettings, CancellationToken.None);
            return true;
        }
        catch (Exception ex) {
            await _host.ProcessError(ex);
            return false;
        }
    }

    public void FocusDefault()
    {
        if (List.FirstRow is { } row) row.LandFocus();
        else Header.FocusBack();
    }
}
