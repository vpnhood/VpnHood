param(
	# the name the message box carries: the setup passes the product's
	[string]$productName = "Kill switch",
	# the uninstaller's and the command line's: a line of output rather than a message box
	[switch]$quiet
)
# The kill switch's way out (WindowsVpnKillSwitch): lifts its block with neither the service nor the app's exe, for
# a person whose app cannot open while the internet is blocked, so they are never stuck behind it. The block is the
# Windows Filtering Platform's persistent provider, sublayer and filters, under keys every app built on VpnHood shares, so this lifts the block whichever app put it up, as the tunnel's holder does. The
# setup places it in the install root as kill-switch-off.ps1 (install-client.nsi); its Start-menu entry
# ("<product> - Turn off kill switch"), "<command> kill-switch off" while the service does not run, and the
# uninstaller run it. An administrator's: the block is the machine's, so a run without one asks Windows for one
# (UAC) and waits. Windows PowerShell 5.1's own, so its C# is C# 5's.
$ErrorActionPreference = "Stop";

function Show-Result([string]$text, [bool]$isError) {
	if ($quiet) {
		Write-Output $text;
		return;
	}
	Add-Type -AssemblyName System.Windows.Forms;
	$icon = if ($isError) { [System.Windows.Forms.MessageBoxIcon]::Error } else { [System.Windows.Forms.MessageBoxIcon]::Information };
	[void][System.Windows.Forms.MessageBox]::Show($text, $productName, [System.Windows.Forms.MessageBoxButtons]::OK, $icon);
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent());
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
	$arguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", "`"$PSCommandPath`"",
		"-productName", "`"$productName`"");
	if ($quiet) { $arguments += "-quiet"; }
	try {
		$process = Start-Process -FilePath "$PSHOME\powershell.exe" -ArgumentList $arguments -Verb RunAs -Wait -PassThru;
	}
	catch {
		Show-Result "The kill switch was not turned off: it needs an administrator's OK." $true;
		exit 2;
	}

	# the elevated run's own window is gone with its line
	if ($quiet) {
		Show-Result $(if ($process.ExitCode -eq 0) { "The kill switch is not blocking the internet." }
			else { "The kill switch could not be turned off (exit $($process.ExitCode))." }) ($process.ExitCode -ne 0);
	}
	exit $process.ExitCode;
}

# The keys WindowsVpnKillSwitch adds, and its order: the filters (a base with an index in the last byte), then the
# sublayer, then the provider, in one transaction; a key that is not there is no error.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class VhKillSwitchOff
{
	private const uint FilterNotFound = 0x80320003;
	private const uint ProviderNotFound = 0x80320005;
	private const uint SubLayerNotFound = 0x80320007;
	private const uint RpcAuthnDefault = 0xFFFFFFFF;

	[DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
	private static extern uint FwpmEngineOpen0(string serverName, uint authnService, IntPtr authIdentity,
		IntPtr session, out IntPtr engineHandle);

	[DllImport("fwpuclnt.dll")]
	private static extern uint FwpmEngineClose0(IntPtr engineHandle);

	[DllImport("fwpuclnt.dll")]
	private static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

	[DllImport("fwpuclnt.dll")]
	private static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

	[DllImport("fwpuclnt.dll")]
	private static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

	[DllImport("fwpuclnt.dll")]
	private static extern uint FwpmFilterDeleteByKey0(IntPtr engineHandle, ref Guid key);

	[DllImport("fwpuclnt.dll")]
	private static extern uint FwpmSubLayerDeleteByKey0(IntPtr engineHandle, ref Guid key);

	[DllImport("fwpuclnt.dll")]
	private static extern uint FwpmProviderDeleteByKey0(IntPtr engineHandle, ref Guid key);

	// how many of the block's objects were there
	public static int Remove()
	{
		IntPtr engine;
		Check(FwpmEngineOpen0(null, RpcAuthnDefault, IntPtr.Zero, IntPtr.Zero, out engine),
			"open the Windows Filtering Platform");
		try {
			Check(FwpmTransactionBegin0(engine, 0), "begin a transaction");
			try {
				var removed = 0;
				var filterKey = new Guid("146ff0ba-f091-48a7-b02e-585acdfbdb00").ToByteArray();
				for (var i = 0; i < 64; i++) {
					filterKey[15] = (byte)i;
					var key = new Guid(filterKey);
					removed += Deleted(FwpmFilterDeleteByKey0(engine, ref key), FilterNotFound, "delete a filter");
				}

				var subLayerKey = new Guid("552f1197-0054-4f0b-bc94-d9225d265b5b");
				removed += Deleted(FwpmSubLayerDeleteByKey0(engine, ref subLayerKey), SubLayerNotFound,
					"delete the sublayer");
				var providerKey = new Guid("7e45054d-79d4-41a8-9bc4-f1409b25dcfe");
				removed += Deleted(FwpmProviderDeleteByKey0(engine, ref providerKey), ProviderNotFound,
					"delete the provider");
				Check(FwpmTransactionCommit0(engine), "commit the transaction");
				return removed;
			}
			catch {
				FwpmTransactionAbort0(engine);
				throw;
			}
		}
		finally {
			FwpmEngineClose0(engine);
		}
	}

	private static int Deleted(uint error, uint notFound, string action)
	{
		if (error == notFound)
			return 0;
		Check(error, action);
		return 1;
	}

	private static void Check(uint error, string action)
	{
		if (error != 0)
			throw new Win32Exception((int)error, "Could not " + action + " (0x" + error.ToString("X8") + ").");
	}
}
'@;

try {
	$removed = [VhKillSwitchOff]::Remove();
}
catch {
	Show-Result "The kill switch could not be turned off: $($_.Exception.Message)" $true;
	exit 1;
}

Show-Result $(if ($removed -gt 0) { "The kill switch is off: the internet is no longer blocked." }
	else { "The kill switch was not blocking the internet." }) $false;
exit 0;
