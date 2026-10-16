# Publishes Sextant for Windows and packs each build with make-msix.ps1.
# Requires PowerShell 7, the .NET 10 SDK, and git. Packing also needs the
# Windows 10 SDK (makeappx.exe and signtool.exe).
#
#   pwsh src/tools/publish-windows.ps1
#   pwsh src/tools/publish-windows.ps1 -Runtime win-x64
#   pwsh src/tools/publish-windows.ps1 -Store -PackageVersion 1.0.10.0
#   pwsh src/tools/publish-windows.ps1 -WhatIf
#
# To pack a GitHub release that is already published, use
# pack-store-release.ps1 instead. It downloads the Windows zips and writes
# the .msixupload files to the Desktop.
#
# The default publishes self-contained win-x64 and win-arm64 into
# artifacts\, then packs a sideload MSIX for each. An x64 PC can publish
# and pack arm64. It cannot run that build, so the AskPass check is skipped
# there. -PackOnly packs folders that are already in artifacts\.
#
# Certificates. These are three different files. Do not substitute one for
# another.
#
# 1. Sideload, the default. No certificate of yours. make-msix.ps1 creates
#    CN=Sextant-Dev and writes a .cer beside the .msix. Trust that .cer once,
#    then install the .msix. That package is not the Store app.
#
# 2. Store upload, -Store. A pfx whose subject is exactly
#    CN=409B546D-EA32-4065-A208-A777236471B3. The usual local file is
#    %USERPROFILE%\Desktop\SextantStore.pfx, with the password in
#    SextantStore-password.txt in that same folder. Pass -PfxPath to use
#    another file. This certificate is not the Authenticode certificate.
#    The Store re-signs the upload. A self-signed certificate with that
#    subject is enough. -PackageVersion must be major.minor.build.0 with a
#    major of at least 1, and it must increase on every submission. About
#    still shows the git imprint, such as 0.1.9-beta.
#
# 3. Authenticode, optional -AuthenticodePfx. Signs Sextant.exe,
#    Sextant.Git.dll, and Sextant.AskPass.exe inside the publish folder.
#    CI uses the Base64_Encoded_Pfx secret for this and no other step.
#    Leave it unset for a local pack. The MSIX signature is separate.

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet("both", "win-x64", "win-arm64")]
    [string] $Runtime = "both",
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $Version,
    [switch] $Store,
    [string] $PackageVersion = $env:SEXTANT_STORE_PACKAGE_VERSION,
    [string] $PfxPath,
    [string] $PfxPassword,
    [string] $AuthenticodePfx,
    [string] $AuthenticodePassword,
    [switch] $SkipPack,
    [switch] $PackOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "publish-windows.ps1 requires PowerShell 7. Run it with pwsh."
}
$PSNativeCommandUseErrorActionPreference = $false

$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$Project = Join-Path $RepoRoot "src\Sextant.csproj"
$PackScript = Join-Path $PSScriptRoot "make-msix.ps1"
$StoreSubject = "CN=409B546D-EA32-4065-A208-A777236471B3"
$DefaultStorePfx = Join-Path $env:USERPROFILE "Desktop\SextantStore.pfx"

function Get-SextantImprint {
    param([string] $Requested)
    $tag = $Requested
    if ([string]::IsNullOrWhiteSpace($tag)) {
        $tag = & git -C $RepoRoot describe --tags --abbrev=0 --match "v*" 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tag)) {
            $tag = ""
        }
    }
    $tag = $tag.Trim()
    if ($tag -match '^[vV]([0-9].*)$') {
        $tag = $Matches[1]
    }
    if ($tag -notmatch '^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.+-]*)?$') {
        throw "Expected a version tag such as v0.1.3-beta. Got: $(if ($tag) { $tag } else { 'none' })."
    }
    return $tag
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

function Test-HostCanRun {
    param([Parameter(Mandatory)][string] $TargetRuntime)
    $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
    if ($hostArch -eq "arm64") {
        return $true
    }
    return $TargetRuntime -eq "win-x64"
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

$runtimes = if ($Runtime -eq "both") { @("win-x64", "win-arm64") } else { @($Runtime) }
$imprint = Get-SextantImprint $Version

Write-Host "Windows local publish"
Write-Host "  Imprint:   $imprint"
Write-Host "  Runtimes:  $($runtimes -join ', ')"
Write-Host "  Publish:   $(Join-Path $RepoRoot 'artifacts')"
Write-Host ""
Write-Host "Certificates"
Write-Host "  Sideload uses no certificate of yours. make-msix.ps1 signs CN=Sextant-Dev and writes a .cer beside the .msix."
Write-Host "  Store upload (-Store) uses a pfx whose subject is $StoreSubject."
Write-Host "    Default file: $DefaultStorePfx"
Write-Host "    Password:     $(Join-Path (Split-Path -Parent $DefaultStorePfx) 'SextantStore-password.txt')"
Write-Host "    That pfx is not the Authenticode certificate. The Store re-signs the package."
Write-Host "  Authenticode (-AuthenticodePfx) signs Sextant.exe, Sextant.Git.dll, and Sextant.AskPass.exe."
Write-Host "    CI keeps that certificate in Base64_Encoded_Pfx. Leave the switch unset for a local pack."
Write-Host ""

$storePfx = $null
$storePassword = $null
if ($Store) {
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
About still shows the imprint $imprint. A prerelease label is not part of the Store version. Increase the Store version on every submission.
"@
    }
    if ([string]::IsNullOrWhiteSpace($PfxPath)) {
        $PfxPath = $DefaultStorePfx
    }
    $storePfx = [IO.Path]::GetFullPath($PfxPath)
    if (-not (Test-Path -LiteralPath $storePfx -PathType Leaf)) {
        throw @"
Store certificate not found: $storePfx
Create a self-signed pfx whose subject is exactly $StoreSubject, or pass -PfxPath. This is not the Authenticode certificate.
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
    Write-Host "Store pack: $storePfx"
    Write-Host "Store version: $PackageVersion"
    Write-Host ""
} else {
    Write-Host "Sideload pack. Pass -Store -PackageVersion <major.minor.build.0> to build the Partner Center upload instead."
    Write-Host ""
}

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
    Write-Host "Authenticode: $AuthenticodePfx"
    Write-Host ""
}

$signTool = $null
foreach ($target in $runtimes) {
    $publishDir = Join-Path $RepoRoot "artifacts\$target"
    $expected = if ($target -eq "win-arm64") { "arm64" } else { "x64" }

    if (-not $PackOnly) {
        if ($PSCmdlet.ShouldProcess($target, "publish")) {
            Write-Host "Publishing $target"
            & dotnet publish $Project --configuration $Configuration --runtime $target --self-contained true --output $publishDir "-p:Version=$imprint" "-p:InformationalVersion=$imprint" "-p:IncludeSourceRevisionInInformationalVersion=false"
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet publish exited $LASTEXITCODE for $target."
            }
            foreach ($name in @("Sextant.exe", "Sextant.AskPass.exe", "assimp.dll", "libSkiaSharp.dll")) {
                $path = Join-Path $publishDir $name
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                    throw "$name was not in $publishDir."
                }
                $actual = ConvertTo-PeMachine $path
                if ($actual -ne $expected) {
                    throw "$path is $actual. Expected $expected."
                }
            }
            if (Test-HostCanRun $target) {
                $previousAskPass = $env:SEXTANT_ASKPASS
                try {
                    $env:SEXTANT_ASKPASS = ""
                    $helper = Join-Path $publishDir "Sextant.AskPass.exe"
                    $output = & $helper 2>&1
                    if ($LASTEXITCODE -ne 1 -or $output) {
                        throw "The published SSH prompt helper failed its startup check (exit $LASTEXITCODE): $output"
                    }
                } finally {
                    if ($null -eq $previousAskPass) {
                        Remove-Item Env:SEXTANT_ASKPASS -ErrorAction SilentlyContinue
                    } else {
                        $env:SEXTANT_ASKPASS = $previousAskPass
                    }
                }
            } else {
                Write-Host "Skipping the AskPass startup check. This PC cannot run $target."
            }
            if ($AuthenticodePfx) {
                if (-not $signTool) {
                    $signTool = Get-SignTool
                }
                foreach ($name in @("Sextant.exe", "Sextant.Git.dll", "Sextant.AskPass.exe")) {
                    $file = Join-Path $publishDir $name
                    & $signTool sign /fd SHA256 /f $AuthenticodePfx /p $AuthenticodePassword /tr http://timestamp.digicert.com /td SHA256 $file
                    if ($LASTEXITCODE -ne 0) {
                        throw "signtool exited $LASTEXITCODE for $file"
                    }
                }
            }
        }
    } elseif (-not (Test-Path -LiteralPath (Join-Path $publishDir "Sextant.exe") -PathType Leaf)) {
        throw "No publish folder at $publishDir. Run without -PackOnly first."
    }

    if ($SkipPack) {
        continue
    }
    $storeDir = Join-Path $RepoRoot "artifacts\store"
    $msix = Join-Path $storeDir "Sextant-$target.msix"
    if ($PSCmdlet.ShouldProcess($msix, "pack")) {
        New-Item -ItemType Directory -Force -Path $storeDir | Out-Null
        Write-Host "Packing $msix"
        $packArgs = @{
            PublishDir = $publishDir
            Version = $imprint
            OutputFile = $msix
            Runtime = $target
        }
        if ($Store) {
            $packArgs.PfxPath = $storePfx
            $packArgs.PfxPassword = $storePassword
            $packArgs.PackageVersion = $PackageVersion
            $packArgs.ForStore = $true
        }
        & $PackScript @packArgs
        if (-not (Test-Path -LiteralPath $msix -PathType Leaf)) {
            throw "make-msix.ps1 did not write $msix."
        }
    }
}

if ($SkipPack -or $WhatIfPreference) {
    return
}

Write-Host ""
if ($Store) {
    Write-Host "Upload these files in one Partner Center submission. Do not upload the .msix."
    foreach ($target in $runtimes) {
        Write-Host ("  " + (Join-Path $RepoRoot "artifacts\store\Sextant-$target.msixupload"))
    }
} else {
    Write-Host "Sideload install. Trust each new .cer once, then add the package:"
    foreach ($target in $runtimes) {
        $cer = Join-Path $RepoRoot "artifacts\store\Sextant-$target.cer"
        $msix = Join-Path $RepoRoot "artifacts\store\Sextant-$target.msix"
        Write-Host "  Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\CurrentUser\TrustedPeople"
        Write-Host "  Add-AppxPackage -Path '$msix'"
    }
    Write-Host "A sideload package does not update the Store app, and the Store app does not update it."
}
