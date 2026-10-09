<#
.SYNOPSIS
    Saves the GitHub token that release.ps1 publishes with, encrypted to this Windows account.

.DESCRIPTION
    Asks for the token without showing it, and writes it to
    %USERPROFILE%\.artdock\release-token.xml, encrypted with Windows' data protection: only the
    Windows account that saved it, on this PC, can read it back. Nothing else can decrypt the
    file, a copy of it included. release.ps1 -Publish reads it whenever $env:GITHUB_TOKEN is
    not set.

    Outside the repository, so it cannot be committed, and outside AppData, which packaged
    apps see a private copy of.

    The token wants Contents: read and write on the releases repository and nothing more. Run
    this again to replace it — when it expires, say.

.PARAMETER Remove
    Deletes the saved token.

.EXAMPLE
    .\tools\save-release-token.ps1
    Asks for the token and saves it.
#>
param(
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# release.ps1 reads it from the same place.
$tokenFile = Join-Path $HOME '.artdock\release-token.xml'

if ($Remove) {
    if (Test-Path $tokenFile) {
        Remove-Item $tokenFile
        Write-Host "Deleted the saved token, $tokenFile."
    }
    else {
        Write-Host "There is no saved token at $tokenFile."
    }

    return
}

$token = Read-Host 'GitHub token' -AsSecureString
if ($token.Length -eq 0) {
    throw 'No token was given, so nothing was saved.'
}

New-Item -ItemType Directory -Force (Split-Path -Parent $tokenFile) | Out-Null

# A SecureString is written encrypted with DPAPI, for the current user.
$token | Export-Clixml -Path $tokenFile

Write-Host "Saved, encrypted to this Windows account, in $tokenFile."
