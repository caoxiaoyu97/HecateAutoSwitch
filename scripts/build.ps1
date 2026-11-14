param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\bin\HecateAutoSwitch.exe')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | Select-Object -ExpandProperty FullName)
$icon = Join-Path $root 'assets\hecate.ico'

if (-not (Test-Path $compiler)) {
    throw ".NET Framework C# compiler not found: $compiler"
}

$outputDirectory = Split-Path $OutputPath -Parent
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

& $compiler `
    /nologo `
    /codepage:65001 `
    /target:winexe `
    /platform:x64 `
    /win32icon:$icon `
    /out:$OutputPath `
    /r:System.Windows.Forms.dll `
    /r:System.Drawing.dll `
    /r:System.Web.Extensions.dll `
    @source

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$item = Get-Item $OutputPath
Write-Output ("Built {0} ({1} bytes)" -f $item.FullName, $item.Length)
