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

function Read-Catalog([string]$Path) {
    if (-not (Test-Path $Path)) {
        return @()
    }

    $data = Get-Content -Raw -Path $Path | ConvertFrom-Json
    if ($null -eq $data) {
        return @()
    }

    if ($data -is [System.Array]) {
        return @($data)
    }

    return @($data)
}

function Find-EntryIndex($Catalog, [string]$InternalName) {
    for ($i = 0; $i -lt $Catalog.Count; $i++) {
        if ($Catalog[$i].InternalName -eq $InternalName) {
            return $i
        }
    }

    return -1
}

function Build-PluginMasterEntry($Manifest, $Existing, [string]$DownloadUrl, [string]$IconUrl, [string]$RepoUrl) {
    $lastUpdate = [int64][double]::Parse(
        (Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds)

    $entry = [ordered]@{
        Author              = $Manifest.Author
        Name                = $Manifest.Name
        InternalName        = $Manifest.InternalName
        Punchline           = $Manifest.Punchline
        Description         = $Manifest.Description
        ApplicableVersion   = if ($Manifest.ApplicableVersion) { $Manifest.ApplicableVersion } else { "any" }
        IconUrl             = $IconUrl
        Tags                = @($Manifest.Tags)
        DalamudApiLevel     = $Manifest.DalamudApiLevel
        LastUpdate          = $lastUpdate
        DownloadCount       = if ($Existing -and $Existing.DownloadCount) { [int]$Existing.DownloadCount } else { 0 }
        DownloadLinkInstall = $DownloadUrl
        DownloadLinkUpdate  = $DownloadUrl
        AssemblyVersion     = $Manifest.AssemblyVersion
        RepoUrl             = if ($Manifest.RepoUrl) { $Manifest.RepoUrl } else { $RepoUrl }
    }

    if ($Existing -and $Existing.Changelog) {
        $entry.Changelog = $Existing.Changelog
    }

    if ($null -ne $Manifest.LoadRequiredState) {
        $entry.LoadRequiredState = $Manifest.LoadRequiredState
    }

    if ($null -ne $Manifest.LoadSync) {
        $entry.LoadSync = $Manifest.LoadSync
    }

    if ($null -ne $Manifest.CanUnloadAsync) {
        $entry.CanUnloadAsync = $Manifest.CanUnloadAsync
    }

    return [pscustomobject]$entry
}

function Build-RepoEntry($Manifest, $Existing, [string]$DownloadUrl, [string]$RepoUrl, [int64]$LastUpdate) {
    return [pscustomobject][ordered]@{
        Author              = $Manifest.Author
        Name                = $Manifest.Name
        Description         = $Manifest.Description
        Punchline           = $Manifest.Punchline
        InternalName        = $Manifest.InternalName
        AssemblyVersion     = $Manifest.AssemblyVersion
        RepoUrl             = if ($Manifest.RepoUrl) { $Manifest.RepoUrl } else { $RepoUrl }
        ApplicableVersion   = if ($Manifest.ApplicableVersion) { $Manifest.ApplicableVersion } else { "any" }
        Tags                = @($Manifest.Tags)
        DalamudApiLevel     = $Manifest.DalamudApiLevel
        IsHide              = "False"
        IsTestingExclusive  = "False"
        LastUpdated         = $LastUpdate
        DownloadCount       = if ($Existing -and $Existing.DownloadCount) { [int]$Existing.DownloadCount } else { 0 }
        DownloadLinkInstall = $DownloadUrl
        DownloadLinkTesting = $DownloadUrl
        DownloadLinkUpdate  = $DownloadUrl
    }
}

function Write-Catalog([string]$Path, $Catalog) {
    $json = ConvertTo-Json -InputObject @($Catalog) -Depth 8
    $json + "`n" | Set-Content -Path $Path -Encoding utf8 -NoNewline
}

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
$pluginMaster = Read-Catalog $PluginMasterPath
$pluginIndex = Find-EntryIndex $pluginMaster $manifest.InternalName
$existingPlugin = if ($pluginIndex -ge 0) { $pluginMaster[$pluginIndex] } else { $null }

$pluginEntry = Build-PluginMasterEntry $manifest $existingPlugin $DownloadUrl $IconUrl $RepoUrl
if ($pluginIndex -ge 0) {
    $pluginMaster[$pluginIndex] = $pluginEntry
} else {
    $pluginMaster = @($pluginEntry) + $pluginMaster
}

Write-Catalog $PluginMasterPath $pluginMaster
Write-Host "Updated $PluginMasterPath -> $($manifest.InternalName) $($manifest.AssemblyVersion)"

if ($RepoJsonPath) {
    $repo = Read-Catalog $RepoJsonPath
    $repoIndex = Find-EntryIndex $repo $manifest.InternalName
    $existingRepo = if ($repoIndex -ge 0) { $repo[$repoIndex] } else { $null }
    $repoEntry = Build-RepoEntry $manifest $existingRepo $DownloadUrl $RepoUrl $pluginEntry.LastUpdate

    if ($repoIndex -ge 0) {
        $repo[$repoIndex] = $repoEntry
    } else {
        $repo = @($repoEntry) + $repo
    }

    Write-Catalog $RepoJsonPath $repo
    Write-Host "Updated $RepoJsonPath -> $($manifest.InternalName) $($manifest.AssemblyVersion)"
}
