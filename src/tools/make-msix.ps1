# Packs a published Windows build into an MSIX for the Microsoft Store.
# Requires PowerShell 7 and the Windows 10 SDK (makeappx.exe and signtool.exe).
#
# Usage:
#   pwsh src/tools/make-msix.ps1 <publish-dir> <version> <output.msix> [win-x64|win-arm64]
#   pwsh src/tools/make-msix.ps1 -SelfTest
#
# The runtime defaults to win-x64 so an existing caller keeps an x64 package.
# win-arm64 writes ProcessorArchitecture arm64. The SDK tools that pack and
# sign are the host tools: arm64 on Windows on Arm, and x64 otherwise. An
# x64 tool still runs on Windows on Arm when the arm64 kit is absent.
#
# <version> is the imprint from resolve-version.sh, such as 0.1.9-beta or 1.2.3.
# The script also writes <output>.msixupload beside the .msix. Upload that
# file in Partner Center. It holds the package and, when the publish folder
# has PDBs, a symbol package. The GitHub release stays the loose zips.
#
# The package is a full-trust desktop app (runFullTrust), not a sandboxed
# UWP app. Sextant starts the system git, credential helpers, editors, and
# WSL, and it writes settings to the real %APPDATA%\Sextant. File-system and
# registry write virtualization are turned off, which requires the restricted
# unvirtualizedResources capability. MinVersion is 10.0.19041.0 so a machine
# that would ignore that switch cannot install the package.
#
# Partner Center reserved this identity. A Store pack uses it unless an
# environment variable or parameter overrides one field. The package family
# name is derived from the name and publisher; it is not written in the manifest.
#   Name:                  20817M.Grenangen.SextantGit
#   Publisher:             CN=409B546D-EA32-4065-A208-A777236471B3
#   PublisherDisplayName:  M.Grenangen
#   Reserved display name: Sextant Git
# Properties/DisplayName must be the reserved name. The name on the Start
# menu stays Sextant.
#   Package family name:   20817M.Grenangen.SextantGit_nr8zhaqknvty0
#   Package SID:           S-1-15-2-4043513989-1948314254-3861236244-1758161784-3578805874-145697710-4171492892
#   Store ID:              9N1T9BGFCTZS
# The SID and the Store ID are not manifest fields. The listing is
# https://apps.microsoft.com/detail/9N1T9BGFCTZS once Partner Center publishes it.
#
#   SEXTANT_STORE_PACKAGE_NAME            optional override of the identity Name
#   SEXTANT_STORE_PUBLISHER               optional override of the publisher
#   SEXTANT_STORE_PUBLISHER_DISPLAY_NAME  optional override of the display name
#   SEXTANT_STORE_PFX_BASE64              pfx whose certificate subject is that publisher
#   SEXTANT_STORE_PFX_PASSWORD            pfx password; a trailing newline is ignored
#   SEXTANT_STORE_PACKAGE_VERSION         optional major.minor.build.0 override
# Setting the pfx, or -Pfx, packs for upload. The subject must match the
# publisher. A self-signed certificate is enough: the Store re-signs the
# package. This pfx is not the Authenticode certificate that signs Sextant.exe.
#
# Create that certificate once, with the publisher string above as the subject:
#   $subject = "CN=409B546D-EA32-4065-A208-A777236471B3"
#   $cert = New-SelfSignedCertificate -Type Custom -Subject $subject `
#     -KeyUsage DigitalSignature -KeyExportPolicy Exportable `
#     -FriendlyName "Sextant Store" -CertStoreLocation Cert:\CurrentUser\My `
#     -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
#   Export-PfxCertificate -Cert $cert -FilePath SextantStore.pfx -Password $secret
#   [Convert]::ToBase64String([IO.File]::ReadAllBytes("SextantStore.pfx"))
#
# MSIX identity versions are major.minor.build.revision. The Store reserves
# revision and requires 0, and it rejects a major of 0. A prerelease label
# such as -beta is not representable. 1.2.3 becomes 1.2.3.0. 0.1.9-beta is
# packed as 0.1.9.0 for a local sideload only; an upload fails until
# SEXTANT_STORE_PACKAGE_VERSION is a store-legal number that increases on
# every submission. About keeps showing the imprint.
#
# With no store pfx, the script signs a sideload package as CN=Sextant-Dev
# and writes a .cer beside the .msix. Trust it once, then install:
#   Import-Certificate -FilePath .\Sextant-win-x64.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople
#   Add-AppxPackage -Path .\Sextant-win-x64.msix
# An Arm sideload uses the same commands with Sextant-win-arm64.cer and
# Sextant-win-arm64.msix. Pass win-arm64 as the fourth argument.
# That identity is not the Store app. Installing one does not update the other.
#
# Paste this into the submission's notes for certification:
#   Sextant is a full-trust desktop Git client. runFullTrust is required
#   because it launches the system git executable, credential helpers, merge
#   tools, and WSL. unvirtualizedResources is required so those programs and
#   Sextant share the user's real AppData, SSH state, and Git config.
#   Git is not bundled. The app opens without it and asks for an executable.

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $PublishDir,
    [Parameter(Position = 1)]
    [string] $Version,
    [Parameter(Position = 2)]
    [string] $OutputFile,
    [Parameter(Position = 3)]
    [string] $Runtime = "win-x64",
    [string] $PackageName = $env:SEXTANT_STORE_PACKAGE_NAME,
    [string] $Publisher = $env:SEXTANT_STORE_PUBLISHER,
    [string] $PublisherDisplayName = $env:SEXTANT_STORE_PUBLISHER_DISPLAY_NAME,
    [string] $PackageVersion = $env:SEXTANT_STORE_PACKAGE_VERSION,
    [string] $PfxPath,
    [string] $PfxPassword = $env:SEXTANT_STORE_PFX_PASSWORD,
    [switch] $ForStore,
    [switch] $SelfTest,
    [string] $RenderLogos,
    [string] $RenderLogosOut
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
# robocopy and signtool report status in their exit code. A non-zero code
# must not become a terminating error before this script reads it.
$PSNativeCommandUseErrorActionPreference = $false

$script:DevPublisher = "CN=Sextant-Dev"
$script:DevPackageName = "Sextant"
$script:DevPublisherDisplayName = "Marcus Gren$([char]0x00E4)ngen"
# Product identity from Partner Center. The family name is
# 20817M.Grenangen.SextantGit_nr8zhaqknvty0. The SID and Store ID are not
# written into the manifest.
$script:StorePackageName = "20817M.Grenangen.SextantGit"
$script:StorePublisher = "CN=409B546D-EA32-4065-A208-A777236471B3"
$script:StorePublisherDisplayName = "M.Grenangen"
# Partner Center reserved Sextant Git. Sextant itself was not available.
# The package display name must be the reserved one. The installed app keeps Sextant.
$script:StoreDisplayName = "Sextant Git"
$script:AppDisplayName = "Sextant"
$script:StorePackageSid = "S-1-15-2-4043513989-1948314254-3861236244-1758161784-3578805874-145697710-4171492892"
$script:StoreId = "9N1T9BGFCTZS"

function ConvertTo-PackageArchitecture {
    param([AllowEmptyString()][string] $Runtime)
    if ([string]::IsNullOrWhiteSpace($Runtime)) {
        $Runtime = "win-x64"
    }
    switch ($Runtime) {
        "win-x64" { return "x64" }
        "win-arm64" { return "arm64" }
        default { throw "Unsupported runtime: $Runtime (expected win-x64 or win-arm64)." }
    }
}

function ConvertTo-XmlText {
    param([AllowEmptyString()][string] $Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Test-StorePackageName {
    param([string] $Name)
    return $Name -match '^[A-Za-z0-9][A-Za-z0-9.-]{1,48}[A-Za-z0-9]$'
}

function Test-MsiVersionQuad {
    param([string] $Value)
    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.([0-9]+)$') {
        return $null
    }
    $numbers = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4])
    foreach ($number in $numbers) {
        if ($number -gt 65535) {
            return $null
        }
    }
    return $numbers
}

function New-BlockerList {
    # The comma keeps PowerShell from enumerating the empty list into $null.
    return ,([System.Collections.Generic.List[string]]::new())
}

function ConvertTo-SextantPackageVersion {
    param(
        [string] $Imprint,
        [AllowEmptyString()][string] $Override
    )

    $overrideText = $Override.Trim()
    if ($overrideText) {
        $quad = Test-MsiVersionQuad $overrideText
        if (-not $quad) {
            throw "SEXTANT_STORE_PACKAGE_VERSION must be major.minor.build.revision with each number from 0 to 65535. Got: $overrideText"
        }
        if ($quad[3] -ne 0) {
            throw "The Store reserves the last version number and requires 0. Got: $overrideText"
        }
        $blockers = New-BlockerList
        if ($quad[0] -lt 1) {
            $blockers.Add("The Store rejects a package version whose first number is 0 ($overrideText).")
        }
        return [pscustomobject]@{
            Version = "$($quad[0]).$($quad[1]).$($quad[2]).0"
            Blockers = $blockers
        }
    }

    if ($Imprint -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z.]+))?$') {
        throw "Expected a Sextant version such as 0.1.9-beta or 1.2.3. Got: $Imprint"
    }
    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $patch = [int]$Matches[3]
    $prerelease = if ($Matches.ContainsKey(4)) { $Matches[4] } else { "" }
    foreach ($number in @($major, $minor, $patch)) {
        if ($number -gt 65535) {
            throw "An MSIX version number cannot exceed 65535. Got: $Imprint"
        }
    }

    $blockers = New-BlockerList
    if ($major -lt 1) {
        $blockers.Add("The Store rejects a package version whose first number is 0. $Imprint maps to $major.$minor.$patch.0.")
    }
    if ($prerelease) {
        $blockers.Add("The prerelease label '-$prerelease' is not part of an MSIX version. $Imprint maps to $major.$minor.$patch.0, the same identity as $major.$minor.$patch.")
    }
    return [pscustomobject]@{
        Version = "$major.$minor.$patch.0"
        Blockers = $blockers
    }
}

function Invoke-SelfTest {
    $cases = @(
        @{ Imprint = "1.2.3"; Override = ""; Version = "1.2.3.0"; Blockers = 0 },
        @{ Imprint = "1.2.3-beta"; Override = ""; Version = "1.2.3.0"; Blockers = 1 },
        @{ Imprint = "0.1.9"; Override = ""; Version = "0.1.9.0"; Blockers = 1 },
        @{ Imprint = "0.1.9-beta"; Override = ""; Version = "0.1.9.0"; Blockers = 2 },
        @{ Imprint = "0.1.9-beta"; Override = "1.0.109.0"; Version = "1.0.109.0"; Blockers = 0 },
        @{ Imprint = "1.2.3"; Override = "1.2.4.0"; Version = "1.2.4.0"; Blockers = 0 },
        @{ Imprint = "1.2.3"; Override = "0.2.3.0"; Version = "0.2.3.0"; Blockers = 1 },
        @{ Imprint = "1.0.65535"; Override = ""; Version = "1.0.65535.0"; Blockers = 0 },
        @{ Imprint = "1.2.3-beta.2"; Override = ""; Version = "1.2.3.0"; Blockers = 1 }
    )
    foreach ($case in $cases) {
        $mapped = ConvertTo-SextantPackageVersion -Imprint $case.Imprint -Override $case.Override
        if ($mapped.Version -ne $case.Version -or $mapped.Blockers.Count -ne $case.Blockers) {
            throw "Version case $($case.Imprint) override '$($case.Override)' produced $($mapped.Version) with $($mapped.Blockers.Count) blockers."
        }
    }

    foreach ($bad in @("v1.2.3", "1.2", "1.2.3+sha", "01.2.3", "1.2.3-beta+sha")) {
        $rejected = $false
        try { ConvertTo-SextantPackageVersion -Imprint $bad -Override "" } catch { $rejected = $true }
        if (-not $rejected) { throw "Expected $bad to be rejected." }
    }
    foreach ($bad in @("1.2.3.1", "1.2.3", "65536.0.0.0")) {
        $rejected = $false
        try { ConvertTo-SextantPackageVersion -Imprint "1.2.3" -Override $bad } catch { $rejected = $true }
        if (-not $rejected) { throw "Expected override $bad to be rejected." }
    }
    if (-not (Test-StorePackageName "Sextant")) { throw "Sextant should be a legal package name." }
    if (-not (Test-StorePackageName $script:StorePackageName)) {
        throw "The Partner Center package name is not a legal MSIX identity name."
    }
    if ($script:StorePublisher -cnotmatch '^CN=[0-9A-Fa-f]{8}-([0-9A-Fa-f]{4}-){3}[0-9A-Fa-f]{12}$') {
        throw "The Partner Center publisher is not CN= followed by a GUID."
    }
    if ([string]::IsNullOrWhiteSpace($script:StorePublisherDisplayName)) {
        throw "The Partner Center publisher display name is empty."
    }
    if ($script:StorePackageSid -notmatch '^S-1-15-2-(\d+-){6}\d+$') {
        throw "The package SID is not an app package SID."
    }
    if ($script:StoreId -notmatch '^9[A-Z0-9]{11}$') {
        throw "The Store ID is not a 12-character product id."
    }
    if ($script:StoreDisplayName -ne "Sextant Git") {
        throw "The reserved Store display name must be Sextant Git."
    }
    if ($script:AppDisplayName -ne "Sextant") {
        throw "The installed app name must stay Sextant."
    }
    if (-not (Test-StorePackageName "12345.Sextant")) { throw "A Partner Center name should be legal." }
    if (Test-StorePackageName "A") { throw "A one-character name should be rejected." }
    if (Test-StorePackageName ".Sextant") { throw "A leading period should be rejected." }
    if ((ConvertTo-XmlText 'Marcus & Co "Sextant"') -ne "Marcus &amp; Co &quot;Sextant&quot;") {
        throw "XML escaping did not match the manifest encoder."
    }
    if ((ConvertTo-PackageArchitecture "win-x64") -ne "x64") { throw "win-x64 must pack as x64." }
    if ((ConvertTo-PackageArchitecture "win-arm64") -ne "arm64") { throw "win-arm64 must pack as arm64." }
    if ((ConvertTo-PackageArchitecture "") -ne "x64") { throw "An empty runtime must stay win-x64." }
    foreach ($bad in @("win-x86", "linux-arm64", "arm64")) {
        $rejected = $false
        try { ConvertTo-PackageArchitecture $bad } catch { $rejected = $true }
        if (-not $rejected) { throw "Expected runtime $bad to be rejected." }
    }
    Write-Host "Store package version and architecture rules passed."
}

function Get-WindowsKitTool {
    param([Parameter(Mandatory)][string] $Name)
    $root = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $root)) {
        throw "Windows SDK not found at $root. Install the Windows 10 SDK so makeappx.exe and signtool.exe are available."
    }
    $preferred = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "x64" }
    $escaped = [regex]::Escape($Name)
    $tool = Get-ChildItem -Path $root -Recurse -Filter $Name -File |
        Where-Object { $_.FullName -match '\\(x64|arm64)\\' + $escaped + '$' } |
        Sort-Object @{ Expression = { if ($_.FullName -match "\\$preferred\\$escaped$") { 0 } else { 1 } } }, @{ Expression = "FullName"; Descending = $true } |
        Select-Object -First 1
    if (-not $tool) {
        throw "$Name was not found under $root."
    }
    return $tool.FullName
}

function New-StoreLogosSta {
    param(
        [Parameter(Mandatory)][string] $SourcePng,
        [Parameter(Mandatory)][string] $AssetsDir
    )

    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase

    $full = [IO.Path]::GetFullPath($SourcePng)
    $frame = [Windows.Media.Imaging.BitmapFrame]::Create(
        [Uri]$full,
        [Windows.Media.Imaging.BitmapCreateOptions]::IgnoreColorProfile,
        [Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    # CopyPixels writes BGRA. The mark's corners are black and the compass is
    # light, so one center sample can miss the purple field. A coarse grid
    # inset from the edge, skipping near-black and the light symbol, leaves
    # that field. The darkest of those pixels fills the wide tile.
    $stride = $frame.PixelWidth * 4
    $pixels = [byte[]]::new($stride * $frame.PixelHeight)
    $frame.CopyPixels($pixels, $stride, 0)
    $best = $null
    $insetX = [Math]::Max(1, [int]($frame.PixelWidth * 0.12))
    $insetY = [Math]::Max(1, [int]($frame.PixelHeight * 0.12))
    for ($y = $insetY; $y -lt ($frame.PixelHeight - $insetY); $y += 4) {
        for ($x = $insetX; $x -lt ($frame.PixelWidth - $insetX); $x += 4) {
            $offset = $y * $stride + $x * 4
            $b = $pixels[$offset]
            $g = $pixels[$offset + 1]
            $r = $pixels[$offset + 2]
            $luma = (0.3 * $r) + (0.6 * $g) + (0.1 * $b)
            if ($luma -lt 28 -or $luma -gt 150) { continue }
            if ($null -eq $best -or $luma -lt $best.Luma) {
                $best = @{ Luma = $luma; R = $r; G = $g; B = $b }
            }
        }
    }
    $background = if ($null -eq $best) {
        [Windows.Media.Color]::FromRgb(0x3B, 0x22, 0x66)
    } else {
        [Windows.Media.Color]::FromRgb([byte]$best.R, [byte]$best.G, [byte]$best.B)
    }
    $brush = [Windows.Media.SolidColorBrush]::new($background)

    $specs = @(
        @{ Name = "StoreLogo.png"; W = 50; H = 50; Fill = $true },
        @{ Name = "StoreLogo.scale-200.png"; W = 100; H = 100; Fill = $true },
        @{ Name = "Square44x44Logo.png"; W = 44; H = 44; Fill = $true },
        @{ Name = "Square44x44Logo.scale-200.png"; W = 88; H = 88; Fill = $true },
        @{ Name = "Square44x44Logo.targetsize-16.png"; W = 16; H = 16; Fill = $true },
        @{ Name = "Square44x44Logo.targetsize-24.png"; W = 24; H = 24; Fill = $true },
        @{ Name = "Square44x44Logo.targetsize-32.png"; W = 32; H = 32; Fill = $true },
        @{ Name = "Square44x44Logo.targetsize-48.png"; W = 48; H = 48; Fill = $true },
        @{ Name = "Square44x44Logo.targetsize-256.png"; W = 256; H = 256; Fill = $true },
        @{ Name = "Square150x150Logo.png"; W = 150; H = 150; Fill = $true },
        @{ Name = "Square150x150Logo.scale-200.png"; W = 300; H = 300; Fill = $true },
        @{ Name = "Wide310x150Logo.png"; W = 310; H = 150; Fill = $false },
        @{ Name = "Wide310x150Logo.scale-200.png"; W = 620; H = 300; Fill = $false },
        @{ Name = "SplashScreen.png"; W = 620; H = 300; Fill = $false }
    )

    New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null
    foreach ($spec in $specs) {
        $width = [int]$spec.W
        $height = [int]$spec.H
        $visual = [Windows.Media.DrawingVisual]::new()
        [Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [Windows.Media.BitmapScalingMode]::Fant)
        $context = $visual.RenderOpen()
        $context.DrawRectangle($brush, $null, [Windows.Rect]::new(0, 0, $width, $height))
        $scale = [Math]::Min($width / $frame.PixelWidth, $height / $frame.PixelHeight)
        if (-not $spec.Fill) {
            $scale *= 0.86
        }
        $drawWidth = $frame.PixelWidth * $scale
        $drawHeight = $frame.PixelHeight * $scale
        $context.DrawImage($frame, [Windows.Rect]::new(($width - $drawWidth) / 2, ($height - $drawHeight) / 2, $drawWidth, $drawHeight))
        $context.Close()
        $target = [Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
        $target.Render($visual)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($target))
        $path = Join-Path $AssetsDir $spec.Name
        $stream = [IO.File]::Open($path, [IO.FileMode]::Create)
        try {
            $encoder.Save($stream)
        } finally {
            $stream.Dispose()
        }
        if ((Get-Item -LiteralPath $path).Length -lt 100) {
            throw "Logo $($spec.Name) was not written."
        }
    }
}

function New-StoreLogos {
    param(
        [Parameter(Mandatory)][string] $SourcePng,
        [Parameter(Mandatory)][string] $AssetsDir
    )
    if ([Threading.Thread]::CurrentThread.GetApartmentState() -eq [Threading.ApartmentState]::STA) {
        New-StoreLogosSta -SourcePng $SourcePng -AssetsDir $AssetsDir
        return
    }
    & pwsh -NoProfile -Sta -File $PSCommandPath -RenderLogos $SourcePng -RenderLogosOut $AssetsDir
    if ($LASTEXITCODE -ne 0) {
        throw "Generating the Store logos failed."
    }
}

function New-DevSigningCertificate {
    param([Parameter(Mandatory)][string] $Subject)
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $Subject `
        -KeyUsage DigitalSignature -KeyExportPolicy Exportable -KeyAlgorithm RSA -KeyLength 2048 `
        -FriendlyName "Sextant MSIX dev" -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    return $certificate
}

function Open-PfxCertificate {
    param(
        [Parameter(Mandatory)][string] $Path,
        [AllowEmptyString()][string] $Password
    )
    $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet
    $loader = [type]::GetType("System.Security.Cryptography.X509Certificates.X509CertificateLoader, System.Security.Cryptography")
    if ($loader) {
        return [Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadPkcs12FromFile($Path, $Password, $flags)
    }
    $secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
    return [Security.Cryptography.X509Certificates.X509Certificate2]::new($Path, $secure, $flags)
}

function Invoke-PackageSign {
    param(
        [Parameter(Mandatory)][string] $SignTool,
        [Parameter(Mandatory)][string] $Pfx,
        [AllowEmptyString()][string] $Password,
        [Parameter(Mandatory)][string] $File,
        [bool] $RequireTimestamp
    )
    $base = @("sign", "/fd", "SHA256", "/f", $Pfx)
    if (-not [string]::IsNullOrEmpty($Password)) {
        $base += @("/p", $Password)
    }
    & $SignTool @($base + @("/tr", "http://timestamp.digicert.com", "/td", "SHA256", $File))
    if ($LASTEXITCODE -eq 0) {
        return
    }
    if ($RequireTimestamp) {
        throw "signtool exited $LASTEXITCODE while signing $File."
    }
    Write-Warning "The timestamp server was unavailable. The sideload package is signed without a timestamp."
    & $SignTool @($base + $File)
    if ($LASTEXITCODE -ne 0) {
        throw "signtool exited $LASTEXITCODE while signing $File."
    }
}

function New-ZipArchive {
    param(
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][array] $Entries
    )
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Force
    }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Entries) {
            $created = $archive.CreateEntry($entry.Name, $entry.Level)
            $input = [IO.File]::OpenRead($entry.Source)
            $output = $created.Open()
            try {
                $input.CopyTo($output)
            } finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }
}

function Assert-PackageContents {
    param(
        [Parameter(Mandatory)][string] $PackagePath,
        [Parameter(Mandatory)][string] $ExpectedVersion,
        [Parameter(Mandatory)][string] $ExpectedPublisher,
        [Parameter(Mandatory)][string] $ExpectedDisplayName,
        [Parameter(Mandatory)][string] $ExpectedArchitecture
    )
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $names = foreach ($entry in $archive.Entries) { ($entry.FullName -replace '\\', '/') }
        foreach ($required in @(
                "AppxManifest.xml",
                "Sextant.exe",
                "Sextant.AskPass.exe",
                "Assets/StoreLogo.png",
                "Assets/Square44x44Logo.png",
                "Assets/Square150x150Logo.png",
                "Assets/Wide310x150Logo.png",
                "Assets/SplashScreen.png")) {
            if ($names -notcontains $required) {
                throw "The package is missing $required."
            }
        }
        $manifestEntry = $archive.Entries | Where-Object { ($_.FullName -replace '\\', '/') -eq "AppxManifest.xml" } | Select-Object -First 1
        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try {
            $manifest = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }
    } finally {
        $archive.Dispose()
    }
    foreach ($needle in @(
            "Version=`"$ExpectedVersion`"",
            "Publisher=`"$([System.Security.SecurityElement]::Escape($ExpectedPublisher))`"",
            "ProcessorArchitecture=`"$ExpectedArchitecture`"",
            "Executable=`"Sextant.exe`"",
            "EntryPoint=`"Windows.FullTrustApplication`"",
            "Name=`"runFullTrust`"",
            "Name=`"unvirtualizedResources`"",
            "<desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>",
            "<desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>",
            "Alias=`"Sextant.exe`"",
            "<DisplayName>$ExpectedDisplayName</DisplayName>",
            "DisplayName=`"$($script:AppDisplayName)`"")) {
        if ($manifest -notlike "*$needle*") {
            throw "The package manifest does not contain $needle."
        }
    }
}

if ($RenderLogos) {
    if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
        throw "Logo rendering must run on an STA thread."
    }
    if (-not $RenderLogosOut) {
        throw "Logo rendering needs an output directory."
    }
    New-StoreLogosSta -SourcePng $RenderLogos -AssetsDir $RenderLogosOut
    exit 0
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

if (-not $PublishDir -or -not $Version -or -not $OutputFile) {
    throw "Usage: make-msix.ps1 <publish-dir> <version> <output.msix> [win-x64|win-arm64]"
}

$PublishDir = [IO.Path]::GetFullPath($PublishDir)
$OutputFile = [IO.Path]::GetFullPath($OutputFile)
if (-not $OutputFile.EndsWith(".msix", [StringComparison]::OrdinalIgnoreCase)) {
    throw "The output file must end in .msix. Got: $OutputFile"
}
if ([string]::IsNullOrWhiteSpace($Runtime)) {
    $Runtime = "win-x64"
}
$architecture = ConvertTo-PackageArchitecture $Runtime
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir "Sextant.exe") -PathType Leaf)) {
    throw "No Sextant.exe in $PublishDir. Publish $Runtime first."
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishDir "Sextant.AskPass.exe") -PathType Leaf)) {
    throw "No Sextant.AskPass.exe in $PublishDir. The Windows publish copies that helper beside Sextant.exe."
}

$pfxBase64 = $env:SEXTANT_STORE_PFX_BASE64
$storeMode = [bool]($ForStore -or $PfxPath -or -not [string]::IsNullOrWhiteSpace($pfxBase64))
if (-not $storeMode -and ($PackageName -or $Publisher)) {
    throw "Store identity is set without a signing certificate. Set SEXTANT_STORE_PFX_BASE64, or clear SEXTANT_STORE_PACKAGE_NAME and SEXTANT_STORE_PUBLISHER for a sideload package."
}

$mapped = ConvertTo-SextantPackageVersion -Imprint $Version.Trim() -Override $(if ($PackageVersion) { $PackageVersion } else { "" })
if ($storeMode -and $mapped.Blockers.Count -gt 0) {
    $reasons = $mapped.Blockers -join [Environment]::NewLine
    throw @"
This package cannot be uploaded to the Microsoft Store.
$reasons
Set SEXTANT_STORE_PACKAGE_VERSION to major.minor.build.0 with a major of 1 or more, and increase it on every submission. About still shows $Version.
"@
}
foreach ($blocker in $mapped.Blockers) {
    Write-Warning $blocker
    Write-Warning "The sideload package uses $($mapped.Version). The Store would reject that version."
}

if (-not $PackageName) { $PackageName = $(if ($storeMode) { $script:StorePackageName } else { $script:DevPackageName }) }
if (-not $Publisher) { $Publisher = $(if ($storeMode) { $script:StorePublisher } else { $script:DevPublisher }) }
if (-not $PublisherDisplayName) {
    $PublisherDisplayName = $(if ($storeMode) { $script:StorePublisherDisplayName } else { $script:DevPublisherDisplayName })
}
$PackageName = $PackageName.Trim()
$Publisher = $Publisher.Trim()
$PublisherDisplayName = $PublisherDisplayName.Trim()

if (-not (Test-StorePackageName $PackageName)) {
    throw "Package name must be 3 to 50 letters, digits, periods, or hyphens, and must start and end with a letter or digit. Got: $PackageName"
}
if ($Publisher -notmatch '^CN=.+') {
    throw "Publisher must be the Partner Center identity, including the CN= prefix. Got: $Publisher"
}
if ($storeMode -and $Publisher -eq $script:DevPublisher) {
    throw "A Store upload cannot use the sideload publisher $script:DevPublisher."
}
if ($storeMode -and $PackageName -eq $script:DevPackageName) {
    throw "A Store upload cannot use the sideload package name $script:DevPackageName."
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("sextant-msix-" + [guid]::NewGuid().ToString("n"))
$layout = Join-Path $tempRoot "layout"
$generatedPfx = $null
$generatedCer = $null
$devThumbprint = $null
$pfxFromEnvironment = $null
try {
    if (-not [string]::IsNullOrWhiteSpace($pfxBase64) -and -not $PfxPath) {
        $pfxFromEnvironment = Join-Path $tempRoot "store.pfx"
        New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
        [IO.File]::WriteAllBytes($pfxFromEnvironment, [Convert]::FromBase64String($pfxBase64.Trim()))
        $PfxPath = $pfxFromEnvironment
    }
    if ($PfxPassword) {
        $PfxPassword = $PfxPassword.Trim()
    }

    if ($storeMode) {
        if (-not $PfxPath -or -not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) {
            throw "A Store upload needs SEXTANT_STORE_PFX_BASE64 or -PfxPath. The certificate subject must equal the publisher."
        }
        $signingCert = Open-PfxCertificate -Path $PfxPath -Password $(if ($PfxPassword) { $PfxPassword } else { "" })
        try {
            if ($signingCert.Subject -ne $Publisher) {
                throw "Certificate subject '$($signingCert.Subject)' does not match publisher '$Publisher'. Partner Center requires those strings to be identical."
            }
        } finally {
            $signingCert.Dispose()
        }
    } else {
        New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
        $devCert = New-DevSigningCertificate -Subject $Publisher
        $devThumbprint = $devCert.Thumbprint
        if ($devCert.Subject -ne $Publisher) {
            throw "Dev certificate subject '$($devCert.Subject)' does not match publisher '$Publisher'."
        }
        $generatedPfx = Join-Path $tempRoot "dev.pfx"
        $generatedCer = [IO.Path]::ChangeExtension($OutputFile, ".cer")
        $devPassword = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
        $securePassword = ConvertTo-SecureString -String $devPassword -Force -AsPlainText
        Export-PfxCertificate -Cert $devCert -FilePath $generatedPfx -Password $securePassword | Out-Null
        Export-Certificate -Cert $devCert -FilePath (Join-Path $tempRoot "dev.cer") | Out-Null
        $PfxPath = $generatedPfx
        $PfxPassword = $devPassword
    }

    New-Item -ItemType Directory -Force -Path $layout | Out-Null
    & robocopy $PublishDir $layout /E /NFL /NDL /NJH /NJS /nc /ns /np /XF *.pdb *.msix *.msixupload *.cer
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy exited $LASTEXITCODE while copying $PublishDir."
    }

    $icon = Join-Path $PublishDir "sextant.png"
    if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
        $icon = Join-Path $PSScriptRoot "..\Assets\sextant.png"
    }
    if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
        throw "sextant.png was not in the publish folder or in src/Assets."
    }
    New-StoreLogos -SourcePng $icon -AssetsDir (Join-Path $layout "Assets")

    $packageDisplayName = if ($storeMode) { $script:StoreDisplayName } else { $script:AppDisplayName }
    if ($storeMode -and -not [string]::IsNullOrWhiteSpace($env:SEXTANT_STORE_DISPLAY_NAME)) {
        $packageDisplayName = $env:SEXTANT_STORE_DISPLAY_NAME.Trim()
    }

    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
  xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap5 desktop6 rescap">
  <Identity
    Name="$(ConvertTo-XmlText $PackageName)"
    Publisher="$(ConvertTo-XmlText $Publisher)"
    Version="$($mapped.Version)"
    ProcessorArchitecture="$architecture" />
  <Properties>
    <DisplayName>$(ConvertTo-XmlText $packageDisplayName)</DisplayName>
    <PublisherDisplayName>$(ConvertTo-XmlText $PublisherDisplayName)</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
    <desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>
    <desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="Sextant" Executable="Sextant.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="$(ConvertTo-XmlText $script:AppDisplayName)"
        Description="A Git client for very large repositories."
        BackgroundColor="#3B2266"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
        <uap:SplashScreen Image="Assets\SplashScreen.png" />
      </uap:VisualElements>
      <Extensions>
        <uap5:Extension Category="windows.appExecutionAlias">
          <uap5:AppExecutionAlias>
            <uap5:ExecutionAlias Alias="Sextant.exe" />
          </uap5:AppExecutionAlias>
        </uap5:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <rescap:Capability Name="unvirtualizedResources" />
  </Capabilities>
</Package>
"@
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $layout "AppxManifest.xml"), $manifest.TrimStart(), $utf8)

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputFile) | Out-Null
    if (Test-Path -LiteralPath $OutputFile) {
        Remove-Item -LiteralPath $OutputFile -Force
    }
    $makeAppx = Get-WindowsKitTool "makeappx.exe"
    $makeLog = Join-Path $tempRoot "makeappx.log"
    & $makeAppx pack /d $layout /p $OutputFile /o /h SHA256 *> $makeLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $makeLog | Write-Host
        throw "makeappx pack exited $LASTEXITCODE."
    }

    $signTool = Get-WindowsKitTool "signtool.exe"
    Invoke-PackageSign -SignTool $signTool -Pfx $PfxPath -Password $(if ($PfxPassword) { $PfxPassword } else { "" }) -File $OutputFile -RequireTimestamp:$storeMode

    $pdbs = @(Get-ChildItem -Path $PublishDir -Recurse -Filter *.pdb -File)
    $upload = [IO.Path]::ChangeExtension($OutputFile, ".msixupload")
    $symbols = $null
    $uploadEntries = @(
        @{ Name = [IO.Path]::GetFileName($OutputFile); Source = $OutputFile; Level = [IO.Compression.CompressionLevel]::NoCompression }
    )
    if ($pdbs.Count -gt 0) {
        $symbols = [IO.Path]::ChangeExtension($OutputFile, ".appxsym")
        $symbolEntries = foreach ($pdb in $pdbs) {
            $relative = [IO.Path]::GetRelativePath($PublishDir, $pdb.FullName) -replace '\\', '/'
            @{ Name = $relative; Source = $pdb.FullName; Level = [IO.Compression.CompressionLevel]::Optimal }
        }
        New-ZipArchive -Destination $symbols -Entries $symbolEntries
        $uploadEntries += @{ Name = [IO.Path]::GetFileName($symbols); Source = $symbols; Level = [IO.Compression.CompressionLevel]::NoCompression }
    }
    New-ZipArchive -Destination $upload -Entries $uploadEntries
    if ($symbols -and (Test-Path -LiteralPath $symbols)) {
        Remove-Item -LiteralPath $symbols -Force
    }

    if ($generatedCer) {
        Copy-Item -LiteralPath (Join-Path $tempRoot "dev.cer") -Destination $generatedCer -Force
    }

    Assert-PackageContents -PackagePath $OutputFile -ExpectedVersion $mapped.Version -ExpectedPublisher $Publisher -ExpectedDisplayName $packageDisplayName -ExpectedArchitecture $architecture
    $size = (Get-Item -LiteralPath $OutputFile).Length
    if ($size -lt 20MB) {
        throw "The package is $size bytes. A self-contained Sextant build is larger than that, so the layout is incomplete."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $OutputFile
    if ($signature.Status -in @("NotSigned", "HashMismatch") -or -not $signature.SignerCertificate) {
        throw "The package signature is $($signature.Status)."
    }

    Write-Host "Built $OutputFile"
    Write-Host "Built $upload"
    Write-Host "Package identity: $PackageName $($mapped.Version) $Publisher"
    if ($storeMode) {
        Write-Host "Upload $upload in Partner Center. The Store re-signs it."
    } else {
        Write-Host "Sideload signer: $generatedCer"
        Write-Host "Trust that certificate in Cert:\CurrentUser\TrustedPeople, then Add-AppxPackage. This identity is not a Store submission."
    }
} finally {
    if ($devThumbprint) {
        $installed = "Cert:\CurrentUser\My\$devThumbprint"
        if (Test-Path -LiteralPath $installed) {
            Remove-Item -LiteralPath $installed -Force
        }
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
