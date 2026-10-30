# claude-usage-bar installer for Windows
#   irm https://raw.githubusercontent.com/murilo999-666/claude-usage-bar/main/install.ps1 | iex
#
# Downloads the latest release, checks its SHA-256 against checksums.txt and runs "install".

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$repo = 'murilo999-666/claude-usage-bar'
$arch = 'amd64'
if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { $arch = 'arm64' }
$asset = "claude-usage-bar-windows-$arch.exe"
$base = "https://github.com/$repo/releases/latest/download"

$dir = Join-Path ([IO.Path]::GetTempPath()) ('claude-usage-bar-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
    $exe = Join-Path $dir $asset
    $sums = Join-Path $dir 'checksums.txt'
    Write-Host "Downloading $asset ..."
    Invoke-WebRequest -UseBasicParsing -Uri "$base/$asset" -OutFile $exe
    Invoke-WebRequest -UseBasicParsing -Uri "$base/checksums.txt" -OutFile $sums

    $line = Get-Content $sums | Where-Object { ($_ -split '\s+')[-1].TrimStart('*') -eq $asset } | Select-Object -First 1
    if (-not $line) { throw "checksums.txt has no entry for $asset" }
    $want = ($line -split '\s+')[0].ToLower()
    $got = (Get-FileHash -Algorithm SHA256 -Path $exe).Hash.ToLower()
    if ($want -ne $got) { throw "Checksum mismatch for $asset. Nothing was installed." }

    & $exe install @args
    if ($LASTEXITCODE -ne 0) { throw "The installer exited with code $LASTEXITCODE." }
}
finally {
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
}
