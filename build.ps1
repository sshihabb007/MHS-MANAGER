$ErrorActionPreference = 'Stop'
$shihabProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$mehediBinDir = Join-Path $shihabProjectRoot 'bin'
$shihabAssetsDir = Join-Path $shihabProjectRoot 'assets'
$mehediIconIco = Join-Path $shihabAssetsDir 'mdm.ico'
$shihabIconPng = Join-Path $shihabAssetsDir 'mdm.png'

New-Item -ItemType Directory -Force -Path $mehediBinDir | Out-Null

if (-not (Test-Path $mehediIconIco) -or -not (Test-Path $shihabIconPng)) {
    throw "Required official icon assets missing in $shihabAssetsDir"
}

# Compile x64 mdm.exe (MHS Download Manager) using .NET Framework 4.8 csc.exe with embedded Win32 & managed icons
$shihabCsc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$mehediOutExe = Join-Path $mehediBinDir 'mdm.exe'
$shihabSources = @(
    (Join-Path $shihabProjectRoot 'src\ShihabDownloadModels.cs'),
    (Join-Path $shihabProjectRoot 'src\MehediMediaMuxer.cs'),
    (Join-Path $shihabProjectRoot 'src\MehediDynamicSegmentEngine.cs'),
    (Join-Path $shihabProjectRoot 'src\MehediBrowserBridgeAndNativeHost.cs'),
    (Join-Path $shihabProjectRoot 'src\MehediDialogsAndProgress.cs'),
    (Join-Path $shihabProjectRoot 'src\ShihabToolsWindows.cs'),
    (Join-Path $shihabProjectRoot 'src\ShihabMainIdmWindow.cs'),
    (Join-Path $shihabProjectRoot 'src\MehediProgramEntry.cs')
)

& $shihabCsc /nologo /target:winexe /platform:x64 /optimize+ `
    /r:System.dll `
    /r:System.Core.dll `
    /r:System.Drawing.dll `
    /r:System.Windows.Forms.dll `
    /r:System.Runtime.Serialization.dll `
    /r:System.Xml.dll `
    "/win32icon:$mehediIconIco" `
    "/resource:$mehediIconIco,ShihabMehediIDM.mdm_icon.ico" `
    "/resource:$shihabIconPng,ShihabMehediIDM.mdm_icon.png" `
    "/out:$mehediOutExe" `
    @shihabSources

if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}

$shihabHash = (Get-FileHash $mehediOutExe -Algorithm SHA256).Hash.ToLowerInvariant()
$mehediSize = (Get-Item $mehediOutExe).Length
Write-Output "BUILD SUCCESS: $mehediOutExe ($mehediSize bytes, SHA256=$shihabHash)"
