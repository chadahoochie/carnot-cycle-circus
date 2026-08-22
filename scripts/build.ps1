# Load helper scripts
. "$PSScriptRoot/getReleaseNotes.ps1"
. "$PSScriptRoot/bumpVersion.ps1"

# Parse release notes and update Directory.Build.props
$releaseNotes = Get-ReleaseNotes -MarkdownFile (Join-Path -Path (Split-Path $PSScriptRoot -Parent) -ChildPath "RELEASE_NOTES.md")
UpdateVersionAndReleaseNotes -ReleaseNotesResult $releaseNotes -XmlFilePath (Join-Path -Path (Split-Path $PSScriptRoot -Parent) -ChildPath "Directory.Build.props")

Write-Output "Updated to version $($releaseNotes.Version)"
