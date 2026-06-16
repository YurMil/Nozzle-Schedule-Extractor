$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$sources = Get-ChildItem -Path $root -Recurse -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\Gui\\' -and $_.FullName -notmatch '\\Tests\\' } |
    Sort-Object FullName |
    ForEach-Object { $_.FullName }
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe for .NET Framework 4 was not found." }
$out = Join-Path $bin 'NozzleScheduleExtractor.exe'
& $csc /nologo /target:exe "/out:$out" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $sources
