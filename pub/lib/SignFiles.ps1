# Authenticode signing via Azure Trusted Signing (now surfaced as "Artifact Signing").
# Dot-source this to get $signEnabled and Invoke-VhSign. Self-contained: it resolves its own
# credential/target sources and does not require Common.ps1 (safe either way — it never overwrites
# an already-set AZURE_* env var).
#
# Signing is OPTIONAL and all-or-nothing:
#   both sources present  -> signing on; any signing failure is fatal.
#   both sources absent   -> signing off (fork-friendly); callers decide how loudly to warn.
#   exactly one present   -> THROW. A half-configured signer ships unsigned artifacts from a green
#                            run — exactly how every release before 8.1.843 went out unsigned.

$ErrorActionPreference = "Stop";

# .user/ lives beside the repo checkout (same layout Common.ps1 uses: <solutionDir>/../.user).
$vhSignUserDir = "$(Split-Path -parent (Split-Path -parent $PSScriptRoot))/../.user";

# --- Resolve the Azure signing credential from a single consolidated source ----------
# The Azure service principal is supplied as ONE JSON credential — exactly the file you download
# from Azure (e.g. `az ad sp create-for-rbac ...`). In CI it arrives as the single
# AZURE_SIGNING_CREDENTIAL secret; locally it's read from .user/azure_signing_credential.json. It
# carries AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET (any other fields are ignored).
# The signing target (Endpoint/CodeSigningAccountName/CertificateProfileName) is NOT part of this file and stays
# separate. Explicit AZURE_* env vars (if already set) win; this only fills them in when absent.
if (-not ($env:AZURE_TENANT_ID -and $env:AZURE_CLIENT_ID -and $env:AZURE_CLIENT_SECRET)) {
	$azCredRaw =
		if ($env:AZURE_SIGNING_CREDENTIAL) { $env:AZURE_SIGNING_CREDENTIAL }
		elseif (Test-Path "$vhSignUserDir/azure_signing_credential.json") { Get-Content "$vhSignUserDir/azure_signing_credential.json" -Raw }
		else { $null };
	if ($azCredRaw) {
		try { $azCred = $azCredRaw | ConvertFrom-Json }
		catch { Throw "Azure signing credential is not valid JSON: $($_.Exception.Message)"; }
		foreach ($k in "AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET") {
			if ($azCred.$k) {
				# Register the value as a masked secret in CI logs before it lands in the process env.
				if ($env:GITHUB_ACTIONS -eq "true") { Write-Host "::add-mask::$($azCred.$k)"; }
				Set-Item -Path "Env:$k" -Value ([string]$azCred.$k);
			}
		}
	}
}

# --- Resolve the signing target from a single consolidated source ------------
# The signing target is supplied as ONE JSON in Azure's own metadata.json schema (the file
# signtool's dlib consumes): { Endpoint, CodeSigningAccountName, CertificateProfileName }. In CI it
# arrives as the AZURE_SIGNING_TARGET variable; locally it's read from .user/azure_signing_target.json.
# These are identifiers (not the Azure secret).
$signAccount = $null; $signProfile = $null; $signEndpoint = $null;
$signTargetRaw =
	if ($env:AZURE_SIGNING_TARGET) { $env:AZURE_SIGNING_TARGET }
	elseif (Test-Path "$vhSignUserDir/azure_signing_target.json") { Get-Content "$vhSignUserDir/azure_signing_target.json" -Raw }
	else { $null };
if ($signTargetRaw) {
	try { $signTarget = $signTargetRaw | ConvertFrom-Json }
	catch { Throw "AZURE_SIGNING_TARGET is not valid JSON: $($_.Exception.Message)"; }
	$signAccount  = $signTarget.CodeSigningAccountName;
	$signProfile  = $signTarget.CertificateProfileName;
	$signEndpoint = $signTarget.Endpoint;
}

$signCredentialSet = [bool]($env:AZURE_TENANT_ID -and $env:AZURE_CLIENT_ID -and $env:AZURE_CLIENT_SECRET);
$signTargetSet     = [bool]($signAccount -and $signProfile -and $signEndpoint);

# HALF-configured is an error, not a warning. "No signing configured at all" is the documented
# fork-friendly path (unsigned + green), but ONE of the two halves present means somebody intended
# to sign and the other half is missing or malformed — that is "present but failing", which ships
# unsigned artifacts from a green run. It stayed hidden exactly that way: the org held
# AZURE_SIGNING_CREDENTIAL while AZURE_SIGNING_TARGET was never set anywhere, so every release built
# unsigned and nothing went red.
if ($signCredentialSet -ne $signTargetSet) {
	$missing = if ($signCredentialSet) { "AZURE_SIGNING_TARGET" } else { "AZURE_SIGNING_CREDENTIAL" };
	$present = if ($signCredentialSet) { "AZURE_SIGNING_CREDENTIAL" } else { "AZURE_SIGNING_TARGET" };
	Throw ("Azure signing is half-configured: $present is set but $missing is missing or " +
		"incomplete, so the artifacts would ship UNSIGNED from a passing build. Set both (see " +
		".github/DEPLOYMENT.md), or unset both to build unsigned on purpose. " +
		"AZURE_SIGNING_TARGET must be JSON with Endpoint, CodeSigningAccountName and CertificateProfileName.");
}

$signEnabled = $signCredentialSet -and $signTargetSet;
$script:signToolReady = $false;
function Invoke-VhSign([string[]]$files) {
	# An empty set is a legitimate no-op (everything was already signed); the CLI would error on it.
	if (-not $signEnabled -or -not $files) { return; }

	# NEVER re-sign. Anything that already carries a signature — Microsoft's runtime, WinDivert.dll,
	# Caphyon's updater.exe, or our own output from an earlier run of this script — is dropped HERE rather
	# than at the call site, so no caller can forget: overwriting a vendor's identity with ours buys
	# nothing and burns signing quota. Callers may therefore hand over a whole publish folder.
	$pending = @($files | Where-Object { -not (Get-AuthenticodeSignature $_).SignerCertificate });
	if ($pending.Count -lt $files.Count) {
		Write-Host "Signing: $($files.Count - $pending.Count) file(s) already signed, left untouched." -ForegroundColor DarkGray;
	}
	if (-not $pending) { return; }

	if (-not $script:signToolReady) {
		# Ensure the dotnet global-tools dir is on PATH for THIS process — a freshly
		# installed global tool is not added to the current process's PATH automatically.
		$toolsDir = if ($env:USERPROFILE) { Join-Path $env:USERPROFILE ".dotnet\tools" } else { Join-Path $env:HOME ".dotnet/tools" };
		$sep = [IO.Path]::PathSeparator;
		if (($env:PATH -split [regex]::Escape($sep)) -notcontains $toolsDir) { $env:PATH = "$toolsDir$sep$env:PATH"; }
		# Install/refresh the Microsoft 'sign' CLI. `update` also upgrades an already-installed global
		# tool (and installs it when absent), which is what keeps the 'artifact-signing' verb below
		# available on a dev box that installed the tool long ago. It ships ONLY as prerelease NuGet
		# versions, so --prerelease is required. Capture output so a failure is diagnosable.
		$installLog = dotnet tool update --global sign --prerelease 2>&1;
		if (-not (Get-Command sign -ErrorAction SilentlyContinue)) {
			Write-Host ($installLog -join "`n");
			Throw "The 'sign' CLI is not available after 'dotnet tool update --global sign --prerelease' (check PATH/install).";
		}
		$script:signToolReady = $true;
	}
	Write-Host "Signing via Azure Artifact Signing ($($pending.Count) file(s)): $($pending -join ', ')" -ForegroundColor Cyan;
	# 'artifact-signing' is the current verb; the CLI still accepts 'trusted-signing' but reports it as
	# obsolete, so it will disappear eventually. Same service, renamed flags.
	# Every file costs two network round-trips (sign request + RFC3161 timestamp), so signing is
	# concurrent. The CLI already parallelises at 4; 8 roughly halves the wall-clock on a full publish
	# and stays well under the service's per-profile throttling.
	sign code artifact-signing $pending `
		--artifact-signing-account "$signAccount" `
		--artifact-signing-certificate-profile "$signProfile" `
		--artifact-signing-endpoint "$signEndpoint" `
		--max-concurrency 8;
	if ($LASTEXITCODE -ne 0) { Throw "Code signing failed (exit $LASTEXITCODE)."; }
}
if (-not $signEnabled) { Write-Host "Code signing skipped: no signing credentials configured (unsigned build)." -ForegroundColor Yellow; }
