$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$AssetsDir = Join-Path $RootDir "assets"
$BinDir = Join-Path $RootDir "bin"
$ExtIconsDir = Join-Path $RootDir "browser-extension\icons"

New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null
New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
New-Item -ItemType Directory -Force -Path $ExtIconsDir | Out-Null

$CscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $CscPath)) {
    $CscPath = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $CscPath)) {
    throw "Could not find .NET Framework csc.exe compiler at $CscPath"
}

Write-Host "[1/2] Generating multi-resolution MHSManager.ico and WebExtension icons..." -ForegroundColor Cyan
$IconGenExe = Join-Path $AssetsDir "GenerateIcon.exe"
$IconGenSrc = Join-Path $RootDir "tools\GenerateIcon.cs"
$IcoFile = Join-Path $AssetsDir "MHSManager.ico"

& $CscPath /nologo /target:exe /optimize+ /r:System.Drawing.dll "/out:$IconGenExe" "$IconGenSrc"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to compile GenerateIcon.cs"
}

& $IconGenExe "$IcoFile" "$ExtIconsDir"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to execute GenerateIcon.exe"
}
Remove-Item -Force $IconGenExe -ErrorAction SilentlyContinue

Write-Host "[2/2] Compiling MHSManager.exe (x64 Native Windows GUI)..." -ForegroundColor Cyan
$OutExe = Join-Path $BinDir "MHSManager.exe"
$ManifestPath = Join-Path $RootDir "src\app.manifest"
$Sources = Get-ChildItem -Path (Join-Path $RootDir "src") -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }

$CompileArgs = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/win32icon:$IcoFile",
    "/win32manifest:$ManifestPath",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Web.Extensions.dll",
    "/out:$OutExe"
) + $Sources

& $CscPath @CompileArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation of MHSManager.exe failed with exit code $LASTEXITCODE"
}

$ExeInfo = Get-Item $OutExe
$ExeHash = (Get-FileHash -Path $OutExe -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("SUCCESS: Built {0} ({1:N0} bytes)" -f $ExeInfo.FullName, $ExeInfo.Length) -ForegroundColor Green
Write-Host ("SHA-256: {0}" -f $ExeHash) -ForegroundColor DarkGray

