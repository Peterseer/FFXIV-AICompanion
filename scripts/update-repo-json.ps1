param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$RepoJsonPath,

    [Parameter(Mandatory = $true)]
    [string]$DownloadUrl
)

$manifest = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
$repo = @(Get-Content -Raw -Path $RepoJsonPath | ConvertFrom-Json)

$index = -1
for ($i = 0; $i -lt $repo.Count; $i++) {
    if ($repo[$i].InternalName -eq $manifest.InternalName) {
        $index = $i
        break
    }
}

$entry = [ordered]@{
    Author              = $manifest.Author
    Name                = $manifest.Name
    Description         = $manifest.Description
    Punchline           = $manifest.Punchline
    InternalName        = $manifest.InternalName
    AssemblyVersion     = $manifest.AssemblyVersion
    RepoUrl             = if ($manifest.RepoUrl) { $manifest.RepoUrl } else { "https://github.com/Peterseer/DalamudSamplePlugin" }
    ApplicableVersion   = if ($manifest.ApplicableVersion) { $manifest.ApplicableVersion } else { "any" }
    Tags                = @($manifest.Tags)
    DalamudApiLevel     = $manifest.DalamudApiLevel
    IsHide              = "False"
    IsTestingExclusive  = "False"
    LastUpdated         = [int][double]::Parse(
        (Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalSeconds)
    DownloadCount       = if ($index -ge 0 -and $repo[$index].DownloadCount) { [int]$repo[$index].DownloadCount } else { 0 }
    DownloadLinkInstall = $DownloadUrl
    DownloadLinkTesting = $DownloadUrl
    DownloadLinkUpdate  = $DownloadUrl
}

if ($index -ge 0) {
    $repo[$index] = [pscustomobject]$entry
} else {
    $repo = @([pscustomobject]$entry) + $repo
}

($repo | ConvertTo-Json -Depth 6) + "`n" | Set-Content -Path $RepoJsonPath -Encoding utf8 -NoNewline
Write-Host "Updated $RepoJsonPath -> $($manifest.InternalName) $($manifest.AssemblyVersion)"
