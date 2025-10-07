# Get latest changelog section by extracting the first H1 Markdown heading block (a line starting with exactly one '#' followed by a space)
# Rules:
#   - The FIRST H1 heading (line starting with exactly one '#' followed by a space) defines the start.
#   - The section ends right BEFORE the next H1 heading (another line starting with exactly one '#' followed by a space).
#   - The heading line itself is EXCLUDED from the returned text.
#   - Lines that CONTAIN (substring match, case-insensitive) any value from $excludeLines are excluded.
#   - Trailing / leading blank lines are trimmed in the final result.
#
# Parameters:
#   fullContent  : Full changelog file content as a single string.
#   excludeLines : Array of substrings; any line containing one of these (case-insensitive) will be removed.
#
# Returns:
#   A string containing the latest changelog block content without its heading; empty string if none found.
#
# Example:
#   $content = Get-Content CHANGELOG.md -Raw
#   $latest = GetLastLog $content @("internal", "[skip-ci]")
#   Write-Host $latest

function GetLastLog([string]$fullContent, [string[]]$excludeLines)
{
    if ([string]::IsNullOrWhiteSpace($fullContent)) {
        return ""
    }

    if (-not $excludeLines) {
        $excludeLines = @()
    }

    # Split lines properly handling different line endings
    $lines = $fullContent -split "`r`n|`n|`r"
    if (-not $lines.Length) {
        return ""
    }

    $startIndex = -1

    # Find first H1 heading (exactly one '#')
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i].TrimEnd()
        if ($line -match '^#\s+') {
            $startIndex = $i
            break
        }
    }

    if ($startIndex -lt 0) {
        return ""
    }

    # Collect lines until next H1 heading (exactly one '#')
    $collected = New-Object System.Collections.Generic.List[string]

    for ($j = $startIndex + 1; $j -lt $lines.Length; $j++) {
        $current = $lines[$j]
        $trimmedCurrent = $current.TrimEnd()

        # Stop at next H1 heading (exactly one '#')
        if ($trimmedCurrent -match '^#\s+') {
            break
        }

        # Exclusion check (substring, case-insensitive)
        $exclude = $false
        foreach ($ex in $excludeLines) {
            if ([string]::IsNullOrWhiteSpace($ex)) { continue }
            if ($current.IndexOf($ex, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $exclude = $true
                break
            }
        }

        if (-not $exclude) {
            # Clean the line: remove hashtags at end and normalize spaces
            $cleanedLine = $current
            
            # Remove hashtags at the end of the line (e.g., "text #aaa" -> "text")
            $cleanedLine = $cleanedLine -replace '\s*#\w*\s*$', ''
            
            # Replace multiple spaces with single space (including tabs)
            $cleanedLine = $cleanedLine -replace '\s+', ' '
            
            # Trim leading and trailing spaces
            $cleanedLine = $cleanedLine.Trim()
            
            $collected.Add($cleanedLine)
        }
    }

    # Trim leading/trailing blank lines
    while ($collected.Count -gt 0 -and [string]::IsNullOrWhiteSpace($collected[0])) {
        $collected.RemoveAt(0)
    }
    while ($collected.Count -gt 0 -and [string]::IsNullOrWhiteSpace($collected[$collected.Count - 1])) {
        $collected.RemoveAt($collected.Count - 1)
    }

    return ($collected -join "`n")
}

