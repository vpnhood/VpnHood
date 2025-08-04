using VpnHood.App.Client;
using VpnHood.AppLib;
using VpnHood.AppLib.Win.Common;
using VpnHood.AppLib.Win.Common.WpfSpa;

namespace VpnHood.App.Connect.Win.Web;

public class App : VpnHoodWpfSpaApp
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.Run();
    }

    public override bool SpaListenToAllIps => AppConfigs.Instance.SpaListenToAllIps;
    public override int? SpaDefaultPort => AppConfigs.Instance.SpaDefaultPort;

    protected override AppOptions CreateAppOptions()
    {
        var appConfigs = AppConfigs.Load();

        // load app settings and resources
        var resources = ConnectAppResources.Resources;
        resources.Strings.AppName = AppConfigs.AppName;
        var foo =
            "vh://eyJ2Ijo0LCJuYW1lIjoiVnBuSG9vZCBHbG9iYWwgU2VydmVycyIsInNpZCI6IjIyMDciLCJ0aWQiOiIwMTk4NDNhYi05MjMxLTc4Y2QtYjc3OS00MTUxNzM2MWM0ZTciLCJpYXQiOiIyMDI1LTA4LTA0VDE4OjExOjQzLjI2NzcxOTlaIiwic2VjIjoiYXU1YTgwaUoxTzFFUXVseGZ1a1F4UT09Iiwic2VyIjp7ImN0IjoiMjAyNS0wNy0yOFQxNzoyMToxNFoiLCJobmFtZSI6ImRvd25sb2FkLm1pY3Jvc29mdC5jb20iLCJocG9ydCI6MCwiaXN2IjpmYWxzZSwic2VjIjoidmFCcVU5UkMzUUhhVzR4RjVpYllGdz09IiwiY2giOiI5RFBRNi9yZHJKYXJtT3Q2ZDJUdFEyYTZyOXM9IiwidXJsIjoiaHR0cHM6Ly9naXRsYWIuY29tL3Zwbmhvb2QvVnBuSG9vZC5GYXJtS2V5cy8tL3Jhdy9tYWluL0dsb2JhbF9GYXJtX2VuY3J5cHRlZF90b2tlbi50eHQiLCJ1cmxzIjpbImh0dHBzOi8vZ2l0bGFiLmNvbS92cG5ob29kL1Zwbkhvb2QuRmFybUtleXMvLS9yYXcvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0IiwiaHR0cHM6Ly9iaXRidWNrZXQub3JnL3Zwbmhvb2QvdnBuaG9vZC5mYXJta2V5cy9yYXcvbWFpbi9HbG9iYWxfRmFybV9lbmNyeXB0ZWRfdG9rZW4udHh0IiwiaHR0cHM6Ly9yYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tL3Zwbmhvb2QvVnBuSG9vZC5GYXJtS2V5cy9tYWluL0dsb2JhbF9GYXJtX2VuY3J5cHRlZF90b2tlbi50eHQiXSwiZXAiOlsiNTEuODEuODEuMjUwOjQ0MyIsIlsyNjA0OjJkYzA6MTAxOjIwMDo6OTNlXTo0NDMiLCI1MS44MS41NS4xMjM6NDQzIiwiMTUuMjA0Ljg3LjQ0OjM5OTYyIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjoxOTUzXTozOTk2MiIsIjk1LjE2NC41LjIwMjo0NDMiLCI1MS44MS4xNzEuMTcxOjQ0MyIsIjE1LjIwNC40MS4xOTA6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjoxNTZdOjQ0MyIsIjE1LjIwNC44Ny45MDo0NDMiLCIxNS4yMDQuNDEuMTg5OjQ0MyIsIjE1LjIwNC4yMDkuODg6NDQzIiwiMTM1LjE0OC4zOS4yMjI6NDQzIiwiWzI2MDQ6MmRjMDoxMDE6MjAwOjoyNTczXTo0NDMiLCI4Mi4xODAuMTQ3LjE5NDo0NDMiLCI1Ny4xMjkuMTM5LjEyMzo0NDMiLCI1MS4yMjIuMTQuMzY6NDQzIiwiMTk0LjE2NC4xMjYuNzA6NDQzIiwiWzJhMDA6ZGEwMDpmNDBkOjMzMDA6OjFdOjQ0MyIsIjUxLjgxLjIxMC4xNjQ6NDQzIiwiWzI2MDQ6MmRjMDoyMDI6MzAwOjo1Y2VdOjQ0MyIsIjUxLjgxLjE3MS4xODM6NDQzIiwiNTEuNzkuNzMuMjQwOjQ0MyIsIlsyNjA3OjUzMDA6MjA1OjIwMDo6NTNiMF06NDQzIiwiMTkyLjk5LjE3Ny4yNDQ6NDQzIiwiNjYuMTc5LjI1Mi4xNTo0NDMiLCJbMjYwNzpiNTAwOjQxNjozMTAwOjoxXTo0NDMiLCI1MS44MS41NS4xMjI6NDQzIiwiWzI2MDQ6MmRjMDoxMDE6MjAwOjo4YjldOjQ0MyIsIjEzNS4xNDguNDguMTAxOjQ0MyIsIjU3LjEyOC4yMDAuMTM5OjQ0MyIsIlsyMDAxOjQxZDA6NjAxOjExMDA6OjEzYTRdOjQ0MyIsIjUxLjgxLjE3MS4xNzA6NDQzIiwiMTk0LjI0Ni4xMTQuMjI6NDQzIiwiNS4yNTAuMTkwLjg6NDQzIiwiWzIwMDE6YmEwOjIyZDplZDAwOjoxXTo0NDMiXSwibG9jIjpbIkFNL1llcmV2YW4gIiwiQVUvTmV3IFNvdXRoIFdhbGVzICIsIkJSL1NhbyBQYXVsbyAiLCJDQS9RdWViZWMgIiwiRlIvSGF1dHMtZGUtRnJhbmNlICIsIkRFL0JlcmxpbiIsIkRFL0hlc3NlICIsIkhLL0hvbmcgS29uZyAiLCJJTi9NYWhhcmFzaHRyYSAiLCJKUC9Ub2t5byAiLCJLWi9BbG1hdHkgIiwiTVgvUXVlcmV0YXJvICIsIlBML01hem92aWEiLCJSVS9Nb3Njb3cgIiwiU0cvU2luZ2Fwb3JlICIsIlpBL0dhdXRlbmcgIiwiS1IvU2VvdWwgIiwiRVMvTWFkcmlkIiwiVFIvSXN0YW5idWwgIiwiQUUvRHViYWkgIiwiR0IvRW5nbGFuZCAiLCJVUy9OZXcgWW9yayAiLCJVUy9PcmVnb24gIiwiVVMvVmlyZ2luaWEgIl0sImxvYzIiOlsiQU0vWWVyZXZhbiBbI3ByZW1pdW1dIiwiQVUvTmV3IFNvdXRoIFdhbGVzIFsjcHJlbWl1bV0iLCJCUi9TYW8gUGF1bG8gW34jcHJlbWl1bV0iLCJDQS9RdWViZWMgW34jcHJlbWl1bV0iLCJGUi9IYXV0cy1kZS1GcmFuY2UgW34jcHJlbWl1bV0iLCJERS9CZXJsaW4iLCJERS9IZXNzZSBbI3ByZW1pdW1dIiwiSEsvSG9uZyBLb25nIFsjcHJlbWl1bV0iLCJJTi9NYWhhcmFzaHRyYSBbI3ByZW1pdW1dIiwiSlAvVG9reW8gWyNwcmVtaXVtXSIsIktaL0FsbWF0eSBbI3ByZW1pdW1dIiwiTVgvUXVlcmV0YXJvIFsjcHJlbWl1bV0iLCJQTC9NYXpvdmlhIiwiUlUvTW9zY293IFsjcHJlbWl1bV0iLCJTRy9TaW5nYXBvcmUgWyNwcmVtaXVtXSIsIlpBL0dhdXRlbmcgWyNwcmVtaXVtXSIsIktSL1Nlb3VsIFsjcHJlbWl1bV0iLCJFUy9NYWRyaWQiLCJUUi9Jc3RhbmJ1bCBbI3ByZW1pdW1dIiwiQUUvRHViYWkgWyNwcmVtaXVtXSIsIkdCL0VuZ2xhbmQgW34jcHJlbWl1bV0iLCJVUy9OZXcgWW9yayBbI3ByZW1pdW0gI3VuYmxvY2thYmxlXSIsIlVTL09yZWdvbiBbfiNwcmVtaXVtIH4jdW5ibG9ja2FibGVdIiwiVVMvVmlyZ2luaWEgW34jcHJlbWl1bV0iXX0sInRhZ3MiOltdLCJpc3B1YiI6dHJ1ZSwiY3BvbHMiOlt7ImNjcyI6WyIqIl0sIm4iOjAsInBidCI6MTAsInBidGRsIjoiMDA6MTA6MDAiLCJwYnIiOjYwLCJwYnAiOnRydWUsInBiYyI6dHJ1ZSwicHVyX3VybCI6Imh0dHBzOi8vd3d3LnZwbmhvb2QuY29tL2ZyZWUtdnBuL2dvLXByZW1pdW0ifSx7ImNjcyI6WyJDTiJdLCJmcmVlIjpbXSwibiI6MCwicGJ0IjoxMCwicGJ0ZGwiOiIwMDoxMDowMCIsInBicCI6dHJ1ZSwicGJjIjp0cnVlLCJwdXJfdXJsIjoiaHR0cHM6Ly93d3cuYy1ob29kLmNvbS8ifV19"; //todo
        return new AppOptions("com.vpnhood.connect.windows", "VpnHoodConnect", AppConfigs.IsDebugMode) {
            CustomData = appConfigs.CustomData,
            UiName = "VpnHoodConnect",
            Resources = resources,
            AccessKeys = [foo], //[appConfigs.DefaultAccessKey],
            UpdateInfoUrl = appConfigs.UpdateInfoUrl,
            UpdaterProvider = new AdvancedInstallerUpdaterProvider(),
            IsAddAccessKeySupported = false,
            LocalSpaHostName = "my-vpnhood-connect",
            AllowEndPointTracker = appConfigs.AllowEndPointTracker,
            Ga4MeasurementId = appConfigs.Ga4MeasurementId,
            PremiumFeatures = ConnectAppResources.PremiumFeatures,
            AllowRecommendUserReviewByServer = true,
            LogServiceOptions = {
                SingleLineConsole = false
            }
        };
    }
}