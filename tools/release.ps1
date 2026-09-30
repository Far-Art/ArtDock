<#
.SYNOPSIS
    Builds a release of ArtDock with Velopack and, with -Publish, publishes it.

.DESCRIPTION
    Publishes the app, packages it with vpk into artifacts\releases, and with -Publish uploads
    it to the public releases repository as a GitHub release. Installed docks find it there when
    someone presses Check for updates. The package holds the setup, the full package, a delta
    from the last published release, and the feed the docks read. Without -Publish the script
    stops after packaging, so the setup can be tried first.

    The version, author, product name, releases repository and install id all come from
    src\ArtDock\ArtDock.csproj, so each is set in one place. Bump <Version> there first:
    GitHub refuses a tag that has been published already, and an installed dock offers only a
    version newer than its own.

    Publishing needs a GitHub token that can write to the releases repository, in
    $env:GITHUB_TOKEN. A fine-grained token with Contents: read and write on that one
    repository is enough. Reading the last release, for the delta, needs no token while the
    repository is public.

.PARAMETER Publish
    Upload the release and publish it. Without this, the script only builds it.

.PARAMETER ReleaseNotes
    A Markdown file of what changed, carried in the package.

.PARAMETER AllowDirty
    Release from a working tree with uncommitted changes. Off by default, because a release
    reports the commit it was built from, and a dirty tree makes that report false.

.EXAMPLE
    .\tools\release.ps1
    Builds the release into artifacts\releases, to try artifacts\releases\*-Setup.exe.

.EXAMPLE
    .\tools\release.ps1 -Publish -ReleaseNotes notes.md
    Builds it and publishes it.
#>
param(
    [switch] $Publish,
    [string] $ReleaseNotes,
    [switch] $AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ArtDock\ArtDock.csproj'
$icon = Join-Path $root 'src\ArtDock\Assets\ArtDock.ico'
$splash = Join-Path $root 'brand\installer\splash.png'
$artifacts = Join-Path $root 'artifacts'
$buildDir = Join-Path $artifacts 'build'
$publishDir = Join-Path $artifacts 'publish'
$releasesDir = Join-Path $artifacts 'releases'

# Runs a native command and stops the script if it fails. Without this, PowerShell carries
# on past a failed dotnet or vpk as if nothing had happened.
function Invoke-Native {
    param([string] $What, [scriptblock] $Command)

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed (exit code $LASTEXITCODE)."
    }
}

Push-Location $root
try {
    if (-not $AllowDirty -and (git status --porcelain)) {
        throw 'The working tree has uncommitted changes. Commit them first, or pass -AllowDirty.'
    }

    if ($Publish -and -not $env:GITHUB_TOKEN) {
        throw 'Publishing needs a token that can write to the releases repository, in $env:GITHUB_TOKEN.'
    }

    if ($ReleaseNotes -and -not (Test-Path $ReleaseNotes)) {
        throw "There are no release notes at $ReleaseNotes."
    }

    $properties = (dotnet msbuild $project -p:Platform=x64 `
            -getProperty:Version -getProperty:Authors -getProperty:Product `
            -getProperty:ArtDockReleasesRepository -getProperty:ArtDockInstallId |
        ConvertFrom-Json).Properties

    $version = $properties.Version
    $repository = $properties.ArtDockReleasesRepository
    $installId = $properties.ArtDockInstallId

    Write-Host "Releasing $($properties.Product) $version as $installId, to $repository"

    Invoke-Native 'Restoring vpk' { dotnet tool restore }

    # The releases folder goes too. vpk refuses to pack a version it finds there already, so
    # a setup built to try and not published would stop the next build at the same version.
    # Nothing in it is needed: the last published release is downloaded into it again below.
    Remove-Item $buildDir, $publishDir, $releasesDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $releasesDir | Out-Null

    # Built into artifacts rather than bin\x64\Release, which the dock running here holds
    # locked: the build's copy into bin would fail while the dock is up.
    Invoke-Native 'Publishing' {
        dotnet publish $project -c Release -p:Platform=x64 "-p:OutDir=$buildDir" -o $publishDir
    }

    # The last published release, for vpk to make the delta from. Finding none is not an
    # error: the first release has nothing to be a delta from, and installs whole.
    $token = if ($env:GITHUB_TOKEN) { @('--token', $env:GITHUB_TOKEN) } else { @() }
    dotnet vpk download github --repoUrl $repository --outputDir $releasesDir @token
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'No earlier release could be read, so this one has no delta, and every update to it downloads it whole.'
    }

    # The Start menu and the desktop. A location added here reaches copies already installed
    # too: an update makes the shortcuts its version adds, though never one the user deleted.
    # The splash is the brand's dark lockup, made at 1x by tools/make-brand.py since
    # the setup scales it to the display itself; its progress bar, green unless told, is the
    # magnified icon's blue.
    $pack = @(
        'pack',
        '--packId', $installId,
        '--packVersion', $version,
        '--runtime', 'win-x64',
        '--packDir', $publishDir,
        '--mainExe', 'ArtDock.exe',
        '--packTitle', $properties.Product,
        '--packAuthors', $properties.Authors,
        '--icon', $icon,
        '--splashImage', $splash,
        '--splashProgressColor', '#4d5cff',
        '--shortcuts', 'Desktop,StartMenuRoot',
        '--outputDir', $releasesDir
    )
    if ($ReleaseNotes) {
        $pack += @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path)
    }
    Invoke-Native 'Packaging' { dotnet vpk @pack }

    if (-not $Publish) {
        Write-Host "Built into $releasesDir. Nothing was published; pass -Publish to publish it."
        return
    }

    Invoke-Native 'Publishing the release' {
        dotnet vpk upload github --repoUrl $repository --token $env:GITHUB_TOKEN `
            --outputDir $releasesDir --publish `
            --releaseName "$($properties.Product) $version" --tag "v$version"
    }

    Write-Host "Published $($properties.Product) $version to $repository/releases."
}
finally {
    Pop-Location
}
