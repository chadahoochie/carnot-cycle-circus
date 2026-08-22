function UpdateVersionAndReleaseNotes {
    param (
        [Parameter(Mandatory=$true)]
        [PSCustomObject]$ReleaseNotesResult,
        [Parameter(Mandatory=$true)]
        [string]$XmlFilePath
    )

    $xmlContent = New-Object XML
    $xmlContent.Load($XmlFilePath)

    # Update VersionPrefix
    $versionElement = $xmlContent.SelectSingleNode("//VersionPrefix")
    if ($versionElement) {
        $versionElement.InnerText = $ReleaseNotesResult.Version
    }

    # Update PackageReleaseNotes
    $notesElement = $xmlContent.SelectSingleNode("//PackageReleaseNotes")
    if ($notesElement) {
        $notesElement.InnerText = $ReleaseNotesResult.ReleaseNotes
    }

    $xmlContent.Save($XmlFilePath)
}
