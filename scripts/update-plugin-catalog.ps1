param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$PluginMasterPath,

    [Parameter(Mandatory = $true)]
    [string]$DownloadUrl,

    [string]$RepoJsonPath = "",

    [string]$IconUrl = "https://raw.githubusercontent.com/Peterseer/DalamudSamplePlugin/master/AICompanion/icon.png",

    [string]$RepoUrl = "https://github.com/Peterseer/DalamudSamplePlugin"
)

function Read-CatalogEntries([string]$Path) {
    if (-not (Test-Path $Path)) {
        return @()
    }

    $text = (Get-Content -Raw -Path $Path).Trim()
    if (-not $text) {
        return @()
    }

    $parsed = $text | ConvertFrom-Json -AsHashtable
    if ($parsed -is [System.Collections.IDictionary]) {
        return @($parsed)
    }

    return @($parsed)
}

function Write-CatalogEntries([string]$Path, [object[]]$Entries) {
    ($Entries | ConvertTo-Json -Depth 8) + "`n" | Set-Content -Path $Path -Encoding utf8 -NoNewline
}

function Find-EntryIndex([object[]]$Entries, [string]$InternalName) {
    for ($i = 0; $i -lt $Entries.Length; $i++) {
        if ([string]$Entries[$i].InternalName -eq $InternalName) {
            return $i
        }
    }

    return -1
}

function Build-PluginMasterEntry($Manifest, $Existing) {
    $lastUpdate = [int64][double]::Parse(
        (Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds)

    $entry = [ordered]@{
        Author              = [string]$Manifest.Author
        Name                = [string]$Manifest.Name
        InternalName        = [string]$Manifest.InternalName
        Punchline           = [string]$Manifest.Punchline
        Description         = [string]$Manifest.Description
        ApplicableVersion   = if ($Manifest.ApplicableVersion) { [string]$Manifest.ApplicableVersion } else { "any" }
        IconUrl             = $IconUrl
        Tags                = @($Manifest.Tags)
        DalamudApiLevel     = [int]$Manifest.DalamudApiLevel
        LastUpdate          = $lastUpdate
        DownloadCount       = if ($Existing -and $Existing.DownloadCount) { [int]$Existing.DownloadCount } else { 0 }
        DownloadLinkInstall = $DownloadUrl
        DownloadLinkUpdate  = $DownloadUrl
        AssemblyVersion     = [string]$Manifest.AssemblyVersion
        RepoUrl             = if ($Manifest.RepoUrl) { [string]$Manifest.RepoUrl } else { $RepoUrl }
    }

    if ($Existing -and $Existing.Contains("Changelog") -and $Existing.Changelog) {
        $entry.Changelog = [string]$Existing.Changelog
    }

    if ($null -ne $Manifest.LoadRequiredState) {
        $entry.LoadRequiredState = [int]$Manifest.LoadRequiredState
    }

    if ($null -ne $Manifest.LoadSync) {
        $entry.LoadSync = [bool]$Manifest.LoadSync
    }

    if ($null -ne $Manifest.CanUnloadAsync) {
        $entry.CanUnloadAsync = [bool]$Manifest.CanUnloadAsync
    }

    return $entry
}

function Build-RepoEntry($Manifest, $Existing, [int64]$LastUpdate) {
    return [ordered]@{
        Author              = [string]$Manifest.Author
        Name                = [string]$Manifest.Name
        Description         = [string]$Manifest.Description
        Punchline           = [string]$Manifest.Punchline
        InternalName        = [string]$Manifest.InternalName
        AssemblyVersion     = [string]$Manifest.AssemblyVersion
        RepoUrl             = if ($Manifest.RepoUrl) { [string]$Manifest.RepoUrl } else { $RepoUrl }
        ApplicableVersion   = if ($Manifest.ApplicableVersion) { [string]$Manifest.ApplicableVersion } else { "any" }
        Tags                = @($Manifest.Tags)
        DalamudApiLevel     = [int]$Manifest.DalamudApiLevel
        IsHide              = "False"
        IsTestingExclusive  = "False"
        LastUpdated         = $LastUpdate
        DownloadCount       = if ($Existing -and $Existing.DownloadCount) { [int]$Existing.DownloadCount } else { 0 }
        DownloadLinkInstall = $DownloadUrl
        DownloadLinkTesting = $DownloadUrl
        DownloadLinkUpdate  = $DownloadUrl
    }
}

function Update-CatalogFile(
    [string]$Path,
    $Manifest,
    [scriptblock]$BuildEntry) {

    $entries = @(Read-CatalogEntries $Path)
    $index = Find-EntryIndex $entries $Manifest.InternalName
    $existing = if ($index -ge 0) { $entries[$index] } else { $null }
    $entry = & $BuildEntry $existing

    if ($index -ge 0) {
        $entries[$index] = $entry
    }
    else {
        $entries = @($entry) + $entries
    }

    Write-CatalogEntries $Path $entries
    Write-Host "Updated $Path -> $($Manifest.InternalName) $($Manifest.AssemblyVersion)"
    return $entry
}

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json -AsHashtable

$pluginEntry = Update-CatalogFile $PluginMasterPath $manifest {
    param($Existing)
    Build-PluginMasterEntry $manifest $Existing
}

if ($RepoJsonPath) {
    Update-CatalogFile $RepoJsonPath $manifest {
        param($Existing)
        Build-RepoEntry $manifest $Existing $pluginEntry.LastUpdate
    } | Out-Null
}
