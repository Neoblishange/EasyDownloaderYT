[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'

if ($Force -and $VerifyOnly) {
    throw 'The -Force and -VerifyOnly switches cannot be used together.'
}
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $repositoryRoot 'Resources\Tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null

$tools = @{
    'yt-dlp.exe' = @{
        Url = 'https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.26/yt-dlp.exe'
        Sha256 = 'f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d'
    }
    'deno.exe' = @{
        Url = 'https://github.com/denoland/deno/releases/download/v2.9.8/deno-x86_64-pc-windows-msvc.zip'
        ArchiveSha256 = 'cba84b26e0df5ae7f74fc66b86a95b866872a86292ebd84fa1e959927edd93bf'
        Sha256 = '924397800533d3eb0ed0c889b20ad70e18b970d93acd8135bc043f425e528266'
    }
    'ffmpeg.exe' = @{
        Sha256 = '1326dde4c84ff1f96fe6b8916c5bed29e163e9b5dccf995f6f3db069d143ec5e'
    }
    'ffprobe.exe' = @{
        Sha256 = 'b49ccc7c6547b141ad5a2f6ec69cc04323d7133d7704d70b331b904c63eecb07'
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-FileSha256([string]$Path, [string]$ExpectedSha256) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $actualSha256 = Get-Sha256 $Path
    return ($actualSha256 -eq $ExpectedSha256.ToLowerInvariant())
}

function Assert-ExistingFile([string]$Name, [string]$ExpectedSha256) {
    $path = Join-Path $toolsDirectory $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        if ($VerifyOnly) {
            throw "$Name is missing."
        }
        return $false
    }

    $actualSha256 = Get-Sha256 $path
    if ($actualSha256 -ne $ExpectedSha256.ToLowerInvariant()) {
        if ($VerifyOnly) {
            throw "$Name failed verification. Expected $ExpectedSha256; got $actualSha256."
        }
        Write-Warning "$Name has an incorrect SHA-256 and will be replaced."
        return $false
    }

    Write-Host "$Name is present and verified."
    return $true
}

function Download-VerifiedFile([string]$Url, [string]$Destination, [string]$ExpectedSha256) {
    $temporaryPath = "$Destination.$([guid]::NewGuid().ToString('N')).download"
    $attemptLimit = 3

    for ($attempt = 1; $attempt -le $attemptLimit; $attempt++) {
        try {
            Invoke-WebRequest -Uri $Url -OutFile $temporaryPath -UseBasicParsing -TimeoutSec 120
            break
        }
        catch {
            if (Test-Path -LiteralPath $temporaryPath) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }

            if ($attempt -eq $attemptLimit) {
                throw "Network error while downloading $Url after $attemptLimit attempts: $($_.Exception.Message)"
            }

            Write-Warning "Network error on attempt $attempt/$attemptLimit. Retrying in 2 seconds..."
            Start-Sleep -Seconds 2
        }
    }

    try {
        $actualSha256 = Get-Sha256 $temporaryPath
        if ($actualSha256 -ne $ExpectedSha256.ToLowerInvariant()) {
            throw "SHA-256 mismatch for $Url. The downloaded file may be corrupted. Expected $ExpectedSha256; got $actualSha256."
        }

        $destinationDirectory = Split-Path -Parent $Destination
        if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Force
        }
        Move-Item -LiteralPath $temporaryPath -Destination $Destination
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Expand-VerifiedArchive([string]$ArchivePath, [string]$Destination) {
    try {
        if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
            throw "Archive $ArchivePath is missing."
        }

        $signature = [System.IO.File]::ReadAllBytes($ArchivePath)
        if ($signature.Length -lt 4 -or $signature[0] -ne 0x50 -or $signature[1] -ne 0x4B) {
            throw "Archive $ArchivePath is corrupted or is not a valid ZIP archive."
        }

        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $Destination -Force
    }
    catch {
        throw "Corrupted archive or archive extraction failed for ${ArchivePath}: $($_.Exception.Message)"
    }
}

function Update-SingleTool([string]$Name) {
    $tool = $tools[$Name]
    $needsUpdate = $Force
    if (-not $needsUpdate) {
        $needsUpdate = -not (Assert-ExistingFile $Name $tool.Sha256)
    }

    if ($needsUpdate) {
        Write-Host "Downloading $Name..."
        Download-VerifiedFile $tool.Url (Join-Path $toolsDirectory $Name) $tool.Sha256
        Write-Host "$Name downloaded and verified."
    }
}

function Update-DenoTool {
    $deno = $tools['deno.exe']
    $needsUpdate = $Force
    if (-not $needsUpdate) {
        $needsUpdate = -not (Assert-ExistingFile 'deno.exe' $deno.Sha256)
    }

    if (-not $needsUpdate) {
        return
    }

    $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "EasyDownloaderYT-tools-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    try {
        $archivePath = Join-Path $temporaryDirectory 'deno.zip'
        Write-Host 'Downloading Deno...'
        Download-VerifiedFile $deno.Url $archivePath $deno.ArchiveSha256

        $extractDirectory = Join-Path $temporaryDirectory 'deno'
        Expand-VerifiedArchive $archivePath $extractDirectory
        $denoExecutable = Get-ChildItem -LiteralPath $extractDirectory -Filter 'deno.exe' -File -Recurse |
            Select-Object -First 1
        if ($null -eq $denoExecutable) {
            throw 'The Deno archive did not contain deno.exe.'
        }

        if (-not (Test-FileSha256 $denoExecutable.FullName $deno.Sha256)) {
            $actualSha256 = Get-Sha256 $denoExecutable.FullName
            throw "SHA-256 mismatch for deno.exe. Expected $($deno.Sha256); got $actualSha256."
        }

        Copy-Item -LiteralPath $denoExecutable.FullName -Destination (Join-Path $toolsDirectory 'deno.exe') -Force
        Write-Host 'deno.exe downloaded and verified.'
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

function Update-FfmpegTools {
    $ffmpegTools = @('ffmpeg.exe', 'ffprobe.exe')
    $toolsToUpdate = @()

    foreach ($name in $ffmpegTools) {
        if ($Force -or -not (Assert-ExistingFile $name $tools[$name].Sha256)) {
            $toolsToUpdate += $name
        }
    }

    if ($toolsToUpdate.Count -eq 0) {
        return
    }

    $ffmpegArchiveUrl = 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip'
    $ffmpegArchiveSha256 = 'db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec'
    $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "EasyDownloaderYT-tools-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    try {
        $archivePath = Join-Path $temporaryDirectory 'ffmpeg.zip'
        Write-Host 'Downloading FFmpeg...'
        Download-VerifiedFile $ffmpegArchiveUrl $archivePath $ffmpegArchiveSha256

        $extractDirectory = Join-Path $temporaryDirectory 'ffmpeg'
        Expand-VerifiedArchive $archivePath $extractDirectory
        $binaryDirectory = Join-Path $extractDirectory 'ffmpeg-8.1.2-essentials_build\bin'

        foreach ($name in $toolsToUpdate) {
            $sourcePath = Join-Path $binaryDirectory $name
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "The FFmpeg archive did not contain $name."
            }

            $expectedSha256 = $tools[$name].Sha256
            if (-not (Test-FileSha256 $sourcePath $expectedSha256)) {
                $actualSha256 = Get-Sha256 $sourcePath
                throw "SHA-256 mismatch for $name. Expected $expectedSha256; got $actualSha256."
            }

            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $toolsDirectory $name) -Force
            Write-Host "$name downloaded and verified."
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
        }
    }
}

function Verify-AllTools {
    foreach ($name in @('yt-dlp.exe', 'deno.exe', 'ffmpeg.exe', 'ffprobe.exe')) {
        [void](Assert-ExistingFile $name $tools[$name].Sha256)
    }
}

function Update-AllTools {
    Update-SingleTool 'yt-dlp.exe'
    Update-DenoTool
    Update-FfmpegTools
}

if ($VerifyOnly) {
    Verify-AllTools
}
else {
    Update-AllTools
}
