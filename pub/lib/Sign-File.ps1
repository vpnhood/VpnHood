param([Parameter(Mandatory=$true)] [String]$file)
# Signs one file as the publish scripts sign theirs (SignFiles.ps1), for a tool that signs while it
# builds: NSIS's !uninstfinalize hands it the uninstaller before packing it into the setup
# (vh-installer/win/install-client.nsi). Called only when signing is configured, so an unsigned
# file after it is a failure, as everywhere else.

. "$PSScriptRoot/SignFiles.ps1"
if (-not $signEnabled) { Throw "Signing is not configured, so nothing signs '$file'."; }

Invoke-VhSign @($file);
if (-not (Get-AuthenticodeSignature $file).SignerCertificate) {
	Throw "'$file' is still unsigned after the signing step.";
}
