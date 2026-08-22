function Get-ReleaseNotes {
    param (
        [Parameter(Mandatory=$true)]
        [string]$MarkdownFile
    )

    $content = Get-Content -Path $MarkdownFile -Raw
    $sections = $content -split "####"

    $result = [PSCustomObject]@{
        Version      = $null
        Date         = $null
        ReleaseNotes = $null
    }

    if ($sections.Count -ge 3) {
        $header = $sections[1].Trim()
        $releaseNotes = $sections[2].Trim()

        $headerParts = $header -split " ", 2
        if ($headerParts.Count -eq 2) {
            $result.Version = $headerParts[0]
            $result.Date = $headerParts[1]
        }

        $result.ReleaseNotes = $releaseNotes
    }

    return $result
}
