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
        return New-Object System.Collections.Generic.List[object]
    }

    $text = (Get-Content -Raw -Path $Path).Trim()
    if (-not $text) {
        return New-Object System.Collections.Generic.List[object]
    }

    $parsed = $text | ConvertFrom-Json
    $list = New-Object System.Collections.Generic.List[object]

    if ($parsed -is [System.Array]) {
        foreach ($item in $parsed) {
            $list.Add($item)
        }
    }
    else {
        $list.Add($parsed)
    }

    return $list
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
    $array = @()
    foreach ($item in $Catalog) {
        $array += $item
    }

    $json = ConvertTo-Json -InputObject $array -Depth 8
    $json + "`n" | Set-Content -Path $Path -Encoding utf8 -NoNewline
}

function Update-Catalog([string]$Path, $Manifest, $EntryFactory) {
    $catalog = Read-Catalog $Path
    $index = Find-EntryIndex $catalog $Manifest.InternalName
    $existing = if ($index -ge 0) { $catalog[$index] } else { $null }
    $entry = & $EntryFactory $existing

    if ($index -ge 0) {
        $catalog[$index] = $entry
    }
    else {
        $catalog.Insert(0, $entry)
    }

    Write-Catalog $Path $catalog
    Write-Host "Updated $Path -> $($Manifest.InternalName) $($Manifest.AssemblyVersion)"
    return $entry
}

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json

$pluginEntry = Update-Catalog $PluginMasterPath $manifest {
    param($Existing)
    Build-PluginMasterEntry $manifest $Existing $DownloadUrl $IconUrl $RepoUrl
}

if ($RepoJsonPath) {
    Update-Catalog $RepoJsonPath $manifest {
        param($Existing)
        Build-RepoEntry $manifest $Existing $DownloadUrl $RepoUrl $pluginEntry.LastUpdate
    } | Out-Null
}
