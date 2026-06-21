param(
	[object]$bump
);

$bump = $bump -eq "1";

. "$PSScriptRoot/../Core/Common.ps1" -bump false

# generic
& "$solutionDir/Src/Core/VpnHood.Core.Toolkit/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Quic.Abstractions/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Quic.MsQuic/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Packets/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.TcpStack.Abstractions/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.TcpStack./_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.PacketTransports/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.Abstractions/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.AndroidTun/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.IosTun/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.WinTun/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.LinuxTun/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.VpnAdapters.WinDivert/_publish.ps1";

# core
& "$solutionDir/Src/Core/VpnHood.Core.Common/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Tunneling/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Filtering.Abstractions/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Filtering.DomainFiltering/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.Abstractions/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.VpnServices.Abstractions/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.VpnServices.Host/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.VpnServices.Manager/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.Devices.Abstractions/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.Devices.Android/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.Devices.Ios/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.Devices.Win/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Client.Devices.Linux/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Server/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Server.Access/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Server.Access.FileAccessManager/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.IpLocations/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.IpLocations.SqliteProvider/_publish.ps1";

# proxies
& "$solutionDir/Src/Core/VpnHood.Core.Proxies.EndPointManagement/_publish.ps1";
& "$solutionDir/Src/Core/VpnHood.Core.Proxies.EndPointManagement.Abstractions/_publish.ps1";

# applib
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Abstractions/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.App/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.WebServer/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Store/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.Common/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.GooglePlay/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.GooglePlay.Core/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Android.Ads.AdMob/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Win.Common/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Win.Common.WpfSpa/_publish.ps1";
& "$solutionDir/Src/AppLib/VpnHood.AppLib.Maui.Common/_publish.ps1";
