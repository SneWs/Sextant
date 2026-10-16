# Downloads a GitHub release's Windows zips and packs a Store upload for each.
# Requires PowerShell 7, the Windows 10 SDK, and gh on PATH.
#
#   pwsh src/tools/pack-store-release.ps1 v1.0.7 -PackageVersion 1.0.11.0
#
# The tag is a published GitHub release, such as v1.0.7. The script downloads
# Sextant-win-x64.zip and Sextant-win-arm64.zip from SneWs/Sextant, checks
# that each Sextant.exe is the architecture named in the zip, and packs both
# with make-msix.ps1. The .msixupload files are copied to the Desktop.
# Partner Center wants those two files. It does not want the .msix.
#
# Signing uses the local Store certificate, not a certificate from GitHub.
# The default file is %USERPROFILE%\Desktop\SextantStore.pfx and the password
# is SextantStore-password.txt beside it. Its subject must be
# CN=409B546D-EA32-4065-A208-A777236471B3. That is the package signature.
# It is not the Authenticode certificate CI uses for Sextant.exe. The Store
# re-signs the upload. Pass -AuthenticodePfx only when you also want to
# replace the signature already on Sextant.exe, Sextant.Git.dll, and
# Sextant.AskPass.exe.
#
# -PackageVersion is major.minor.build.0 with a major of at least 1. It must
# be higher than the last package Partner Center accepted. About still shows
# the release tag. A prerelease label is not part of the Store version.

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Tag,
    [Parameter(Mandatory = $true)]
    [string] $PackageVersion,
    [string] $Repo = "SneWs/Sextant",
    [string] $PfxPath,
    [string] $PfxPassword,
    [string] $Destination,
    [string] $AuthenticodePfx,
    [string] $AuthenticodePassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "pack-store-release.ps1 requires PowerShell 7. Run it with pwsh."
}
$PSNativeCommandUseErrorActionPreference = $false

$StoreSubject = "CN=409B546D-EA32-4065-A208-A777236471B3"
$DefaultStorePfx = Join-Path $env:USERPROFILE "Desktop\SextantStore.pfx"
$PackScript = Join-Path $PSScriptRoot "make-msix.ps1"
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $env:USERPROFILE "Desktop"
}

function ConvertTo-PeMachine {
    param([Parameter(Mandatory)][string] $Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "$Path is not a PE file."
    }
    $offset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($offset -lt 0 -or ($offset + 6) -gt $bytes.Length) {
        throw "$Path has no PE header."
    }
    $machine = [BitConverter]::ToUInt16($bytes, $offset + 4)
    switch ($machine) {
        0x8664 { return "x64" }
        0xAA64 { return "arm64" }
        default { return ("0x{0:X4}" -f $machine) }
    }
}

function Read-SecretFile {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    $text = [IO.File]::ReadAllText($Path).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }
    return $text
}

function Get-SignTool {
    $root = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $root)) {
        throw "Windows SDK not found at $root. Install the Windows 10 SDK so signtool.exe is available."
    }
    $preferred = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
    $tool = Get-ChildItem -Path $root -Recurse -Filter "signtool.exe" -File |
        Where-Object { $_.FullName -match '\\(x64|arm64)\\signtool.exe$' } |
        Sort-Object @{ Expression = { if ($_.FullName -match "\\$preferred\\signtool.exe$") { 0 } else { 1 } } }, @{ Expression = "FullName"; Descending = $true } |
        Select-Object -First 1
    if (-not $tool) {
        throw "signtool.exe was not found under $root."
    }
    return $tool.FullName
}

$Tag = $Tag.Trim()
if ($Tag -match '^[vV]([0-9].*)$') {
    $imprint = $Matches[1]
    $releaseTag = "v$imprint"
} elseif ($Tag -match '^[0-9].*') {
    $imprint = $Tag
    $releaseTag = "v$Tag"
} else {
    throw "Expected a release tag such as v1.0.7. Got: $Tag"
}
if ($imprint -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.+-]*)?$') {
    throw "Expected a release tag such as v1.0.7. Got: $Tag"
}

$packageNumbers = $null
if ($PackageVersion -match '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<build>0|[1-9][0-9]*)\.(?<revision>0|[1-9][0-9]*)$') {
    $packageNumbers = @(
        [int]$Matches.major, [int]$Matches.minor, [int]$Matches.build, [int]$Matches.revision
    )
}
$packageVersionLegal = $packageNumbers -and $packageNumbers[0] -ge 1 -and $packageNumbers[3] -eq 0 -and -not ($packageNumbers | Where-Object { $_ -gt 65535 })
if (-not $packageVersionLegal) {
    throw @"
-PackageVersion must be major.minor.build.0 with a major of at least 1. Got: '$PackageVersion'.
About still shows $imprint. Increase the Store version on every submission.
"@
}

if ([string]::IsNullOrWhiteSpace($PfxPath)) {
    $PfxPath = $DefaultStorePfx
}
$storePfx = [IO.Path]::GetFullPath($PfxPath)
if (-not (Test-Path -LiteralPath $storePfx -PathType Leaf)) {
    throw @"
Store certificate not found: $storePfx
The package is signed with this pfx. Its subject must be $StoreSubject.
"@
}
if (-not [string]::IsNullOrWhiteSpace($PfxPassword)) {
    $storePassword = $PfxPassword.Trim()
} elseif (-not [string]::IsNullOrWhiteSpace($env:SEXTANT_STORE_PFX_PASSWORD)) {
    $storePassword = $env:SEXTANT_STORE_PFX_PASSWORD.Trim()
} else {
    $passwordFile = Join-Path (Split-Path -Parent $storePfx) "SextantStore-password.txt"
    $storePassword = Read-SecretFile $passwordFile
    if (-not $storePassword) {
        throw "No Store pfx password. Put it in $passwordFile, set SEXTANT_STORE_PFX_PASSWORD, or pass -PfxPassword."
    }
}

$signNames = $null
if (-not [string]::IsNullOrWhiteSpace($AuthenticodePfx)) {
    $AuthenticodePfx = [IO.Path]::GetFullPath($AuthenticodePfx)
    if (-not (Test-Path -LiteralPath $AuthenticodePfx -PathType Leaf)) {
        throw "Authenticode pfx not found: $AuthenticodePfx"
    }
    if ([string]::IsNullOrWhiteSpace($AuthenticodePassword)) {
        $AuthenticodePassword = $env:SEXTANT_AUTHENTICODE_PFX_PASSWORD
    }
    if ([string]::IsNullOrWhiteSpace($AuthenticodePassword)) {
        throw "Authenticode signing needs -AuthenticodePassword or SEXTANT_AUTHENTICODE_PFX_PASSWORD."
    }
    $AuthenticodePassword = $AuthenticodePassword.Trim()
    $signNames = @("Sextant.exe", "Sextant.Git.dll", "Sextant.AskPass.exe")
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "gh was not found on PATH. Install the GitHub CLI, then run gh auth login."
}

$Destination = [IO.Path]::GetFullPath($Destination)
$assets = @(
    @{ Runtime = "win-x64"; Zip = "Sextant-win-x64.zip"; Machine = "x64" },
    @{ Runtime = "win-arm64"; Zip = "Sextant-win-arm64.zip"; Machine = "arm64" }
)

Write-Host "Store pack from GitHub release $releaseTag ($Repo)"
Write-Host "  Imprint:    $imprint"
Write-Host "  Package:    $PackageVersion"
Write-Host "  Sign with:  $storePfx"
Write-Host "  Subject:    $StoreSubject"
if ($signNames) {
    Write-Host "  Also sign:  $AuthenticodePfx"
} else {
    Write-Host "  The zip's existing Authenticode signature is left as published. Pass -AuthenticodePfx to replace it."
}
Write-Host "  Desktop:    $Destination"
foreach ($asset in $assets) {
    $uploadName = "Sextant-$releaseTag-$($asset.Runtime).msixupload"
    Write-Host "  $($asset.Zip) -> $(Join-Path $Destination $uploadName)"
}

if (-not $PSCmdlet.ShouldProcess($releaseTag, "download and pack")) {
    return
}

$listed = & gh release view $releaseTag --repo $Repo --json assets --jq ".assets[].name"
if ($LASTEXITCODE -ne 0) {
    throw "Could not read GitHub release $releaseTag."
}
foreach ($asset in $assets) {
    if ($listed -notcontains $asset.Zip) {
        throw "$releaseTag does not contain $($asset.Zip)."
    }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("sextant-store-" + [guid]::NewGuid().ToString("n"))
$signTool = $null
$uploads = @()
$finished = $false
try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($asset in $assets) {
        $zipPath = Join-Path $work $asset.Zip
        Write-Host "Downloading $($asset.Zip)"
        & gh release download $releaseTag --repo $Repo --pattern $asset.Zip --dir $work --clobber
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
            throw "Download of $($asset.Zip) from $releaseTag failed."
        }

        $publishDir = Join-Path $work $asset.Runtime
        Expand-Archive -LiteralPath $zipPath -DestinationPath $publishDir -Force
        $exe = Join-Path $publishDir "Sextant.exe"
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
            throw "$($asset.Zip) does not have Sextant.exe at the archive root."
        }
        $actual = ConvertTo-PeMachine $exe
        if ($actual -ne $asset.Machine) {
            throw "$exe is $actual. Expected $($asset.Machine)."
        }

        if ($signNames) {
            if (-not $signTool) {
                $signTool = Get-SignTool
            }
            foreach ($name in $signNames) {
                $file = Join-Path $publishDir $name
                if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
                    throw "$name was not in $($asset.Zip)."
                }
                & $signTool sign /fd SHA256 /f $AuthenticodePfx /p $AuthenticodePassword /tr http://timestamp.digicert.com /td SHA256 $file
                if ($LASTEXITCODE -ne 0) {
                    throw "signtool exited $LASTEXITCODE for $file"
                }
            }
        }

        $msix = Join-Path $work "Sextant-$($asset.Runtime).msix"
        Write-Host "Signing and packing $($asset.Runtime)"
        & $PackScript -PublishDir $publishDir -Version $imprint -OutputFile $msix -Runtime $asset.Runtime -PfxPath $storePfx -PfxPassword $storePassword -PackageVersion $PackageVersion -ForStore
        $packed = [IO.Path]::ChangeExtension($msix, ".msixupload")
        if (-not (Test-Path -LiteralPath $packed -PathType Leaf)) {
            throw "make-msix.ps1 did not write $packed."
        }
        $destinationFile = Join-Path $Destination "Sextant-$releaseTag-$($asset.Runtime).msixupload"
        Copy-Item -LiteralPath $packed -Destination $destinationFile -Force
        $uploads += $destinationFile
    }
    $finished = $true
} finally {
    if ($finished -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    } elseif (Test-Path -LiteralPath $work) {
        Write-Host "Left the downloaded files in $work"
    }
}

Write-Host ""
Write-Host "Upload both files in one Partner Center submission:"
foreach ($upload in $uploads) {
    Write-Host "  $upload"
}
