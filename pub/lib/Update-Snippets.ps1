# Keeps every code example in a README and in a doc comment identical to the test it comes from, so an
# example a reader copies is code that compiles and runs in CI.
#
#   A test holds the example:            #region Snippet:<Name> ... #endregion
#   A Markdown file shows it in a fence: ```csharp Snippet:<Name> ... ```
#   A doc comment shows it in:           /// <code snippet="Snippet:<Name>" language="csharp"> ... /// </code>
#
# Without -Check the fences and code blocks are rewritten from the regions. With -Check nothing is written, and
# the script fails when a file differs from its regions, so a release cannot ship an example that drifted.
# Used by VpnHood and by the module repos (VpnHood.Net.Proxies), whose CI checks out this pub/ folder.
#
#   pwsh pub/lib/Update-Snippets.ps1 -Root .            # rewrite
#   pwsh pub/lib/Update-Snippets.ps1 -Root . -Check     # verify only

param(
	[Parameter(Mandatory)] [string] $Root,
	[switch] $Check
);

$ErrorActionPreference = "Stop";
$Root = (Resolve-Path $Root).Path;

# Git's list of the files it tracks or would track: a walk of the tree would crawl every bin and obj folder,
# which takes minutes on VpnHood.
# -z: NUL-separated and unquoted, or git would quote a path with unusual characters.
# Read as one raw stream: PowerShell would split a native command's output into lines.
function Get-SourceFiles([string[]] $patterns) {
	$startInfo = [System.Diagnostics.ProcessStartInfo]::new("git");
	foreach ($argument in @("-C", $Root, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--") + $patterns) {
		$startInfo.ArgumentList.Add($argument);
	}
	$startInfo.RedirectStandardOutput = $true;
	$startInfo.UseShellExecute = $false;
	$process = [System.Diagnostics.Process]::Start($startInfo);
	$output = $process.StandardOutput.ReadToEnd();
	$process.WaitForExit();
	if ($process.ExitCode -ne 0) { throw "git ls-files failed in $Root."; }
	$paths = $output -split "`0";
	return @($paths | Where-Object { $_ } | ForEach-Object { Join-Path $Root $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf });
}

# The files whose text matches, searched as files: a path piped in as a string would be searched as text.
function Find-FilesContaining([string[]] $files, [string] $pattern) {
	if (!$files.Count) { return @(); }
	return @(Select-String -LiteralPath $files -Pattern $pattern -List | Select-Object -ExpandProperty Path);
}

# The region's lines, without the indentation they share and without blank lines at either end.
function Get-Dedented([System.Collections.Generic.List[string]] $lines) {
	$body = @($lines | ForEach-Object { $_.TrimEnd() });
	while ($body.Count -gt 0 -and $body[0] -eq "") { $body = @($body | Select-Object -Skip 1); }
	while ($body.Count -gt 0 -and $body[-1] -eq "") { $body = @($body | Select-Object -SkipLast 1); }
	$indents = @($body | Where-Object { $_ -ne "" } | ForEach-Object { $_.Length - $_.TrimStart().Length });
	$cut = if ($indents.Count) { ($indents | Measure-Object -Minimum).Minimum } else { 0 };
	return , @($body | ForEach-Object { if ($_ -eq "") { "" } else { $_.Substring($cut) } });
}

function Read-TextFile([string] $path) {
	$bytes = [System.IO.File]::ReadAllBytes($path);
	$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF;
	$text = [System.Text.UTF8Encoding]::new($false).GetString($bytes, ($hasBom ? 3 : 0), $bytes.Length - ($hasBom ? 3 : 0));
	$newLine = $text.Contains("`r`n") ? "`r`n" : "`n";
	$endsWithNewLine = $text.EndsWith("`n");
	$lines = $text -split "`r?`n";
	if ($endsWithNewLine) { $lines = $lines[0..($lines.Count - 2)]; }
	return [pscustomobject]@{ Lines = $lines; NewLine = $newLine; HasBom = $hasBom; EndsWithNewLine = $endsWithNewLine };
}

function Write-TextFile([string] $path, $file, [string[]] $lines) {
	$text = ($lines -join $file.NewLine) + ($file.EndsWithNewLine ? $file.NewLine : "");
	[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($file.HasBom));
}

function ConvertTo-XmlText([string] $line) {
	return $line.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

# 1. The examples: every Snippet region in a C# file.
$snippets = @{};
$regionFiles = Find-FilesContaining (Get-SourceFiles @("*.cs")) '#region\s+Snippet:';
foreach ($path in $regionFiles) {
	$lines = (Read-TextFile $path).Lines;
	for ($i = 0; $i -lt $lines.Count; $i++) {
		if ($lines[$i] -notmatch '^\s*#region\s+(Snippet:\S+)\s*$') { continue; }
		$name = $Matches[1];
		if ($snippets.ContainsKey($name)) { throw "$name is defined twice: in $($snippets[$name].Path) and $path."; }

		# a region inside the snippet nests: its #endregion is not the snippet's, and its lines are not shown
		$body = [System.Collections.Generic.List[string]]::new();
		$depth = 0;
		for ($j = $i + 1; $j -lt $lines.Count; $j++) {
			if ($lines[$j] -match '^\s*#region\b') { $depth++; continue; }
			if ($lines[$j] -match '^\s*#endregion\b') { if ($depth -eq 0) { break; } $depth--; continue; }
			$body.Add($lines[$j]);
		}
		if ($j -ge $lines.Count) { throw "$name in $path has no #endregion."; }
		$snippets[$name] = [pscustomobject]@{ Path = $path; Lines = (Get-Dedented $body) };
	}
}

# 2. Where they are shown: Markdown fences and doc-comment code blocks.
$changed = @();
$used = @{};
$usageFiles = Find-FilesContaining (Get-SourceFiles @("*.md", "*.cs")) 'Snippet:';
foreach ($path in $usageFiles) {
	$file = Read-TextFile $path;
	$lines = $file.Lines;
	$out = [System.Collections.Generic.List[string]]::new();
	$otherFence = $null; # the opening of a Markdown fence that is not a snippet's: its text is shown as written
	for ($i = 0; $i -lt $lines.Count; $i++) {
		$line = $lines[$i];
		$out.Add($line);
		if ($otherFence) {
			# a closing fence is the opening's character, at least as many times
			if ($line -match "^\s*$([regex]::Escape([string]$otherFence[0])){$($otherFence.Length),}\s*$") { $otherFence = $null; }
			continue;
		}

		$isFence = $line -match '^(\s*)```\s*(?:csharp|cs|c#)\s+(Snippet:\S+)\s*$';
		$isXml = !$isFence -and $line -match '^(\s*)///\s*<code\s+snippet="(Snippet:[^"]+)"[^>]*>\s*$';
		if (!$isFence -and !$isXml) {
			if ($line -match '^\s*(```+|~~~+)') { $otherFence = $Matches[1]; }
			continue;
		}

		$indent = $Matches[1];
		$name = $Matches[2];
		if (!$snippets.ContainsKey($name)) { throw "$path shows $name, which no test defines."; }
		$used[$name] = $true;

		$end = $isFence ? '^\s*```+\s*$' : '^\s*///\s*</code>\s*$';
		for ($i++; $i -lt $lines.Count -and $lines[$i] -notmatch $end; $i++) { }
		if ($i -ge $lines.Count) { throw "$name in $path is not closed."; }

		foreach ($snippetLine in $snippets[$name].Lines) {
			if ($isFence) { $out.Add(($snippetLine -eq "") ? "" : "$indent$snippetLine"); }
			else { $out.Add(($snippetLine -eq "") ? "$indent///" : "$indent/// $(ConvertTo-XmlText $snippetLine)"); }
		}
		$out.Add($lines[$i]);
	}

	# case-sensitive: in C#, a change of case is a change of code
	if (($out -join "`n") -cne ($lines -join "`n")) {
		$changed += $path;
		if (!$Check) { Write-TextFile $path $file $out.ToArray(); }
	}
}

foreach ($name in $snippets.Keys | Where-Object { !$used.ContainsKey($_) }) {
	Write-Warning "$name in $($snippets[$name].Path) is shown nowhere.";
}

if ($Check -and $changed.Count) {
	throw "These files differ from the tests their examples come from; run pub/lib/Update-Snippets.ps1:`n$($changed -join "`n")";
}

$result = $Check ? "every example matches its test" : "$($changed.Count) file(s) updated";
Write-Host "Snippets: $($snippets.Count) found; $result." -ForegroundColor Green;
