param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPackagePath,

    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $UnityPackagePath)) {
    throw "UnityPackagePath not found: $UnityPackagePath"
}
if (-not (Test-Path -LiteralPath $ProjectRoot)) {
    throw "ProjectRoot not found: $ProjectRoot"
}

$tmp = Join-Path $env:TEMP ("unitypackage_import_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    tar -xf $UnityPackagePath -C $tmp

    Get-ChildItem -LiteralPath $tmp -Directory | ForEach-Object {
        $dir = $_.FullName
        $pathnameFile = Join-Path $dir "pathname"
        if (-not (Test-Path -LiteralPath $pathnameFile)) { return }

        $rel = (Get-Content -LiteralPath $pathnameFile -Raw).Trim()
        if ([string]::IsNullOrWhiteSpace($rel)) { return }

        # Unity package pathnames are usually like "Assets/...."
        $dest = Join-Path $ProjectRoot $rel
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $destDir)) {
            New-Item -ItemType Directory -Force -Path $destDir | Out-Null
        }

        $assetFile = Join-Path $dir "asset"
        $metaFile = Join-Path $dir "asset.meta"

        if (Test-Path -LiteralPath $assetFile) {
            Copy-Item -LiteralPath $assetFile -Destination $dest -Force
        }
        else {
            # Folder assets sometimes have no "asset" file, only meta.
            if (-not (Test-Path -LiteralPath $dest)) {
                New-Item -ItemType Directory -Force -Path $dest | Out-Null
            }
        }

        if (Test-Path -LiteralPath $metaFile) {
            Copy-Item -LiteralPath $metaFile -Destination ($dest + ".meta") -Force
        }
    }
}
finally {
    # Leave the temp folder for debugging? No—clean up to avoid clutter.
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output "Imported unitypackage contents into project."

