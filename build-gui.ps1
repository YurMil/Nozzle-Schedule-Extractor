$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$sources = Get-ChildItem -Path $root -Recurse -Filter '*.cs' |
    Where-Object { $_.Name -ne 'Program.cs' -and $_.FullName -notmatch '\\Tests\\' } |
    Sort-Object FullName |
    ForEach-Object { $_.FullName }
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
if (!(Test-Path $csc)) { throw "csc.exe for .NET Framework 4 was not found." }
$swRoot = 'C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS'
$swSldWorks = Join-Path $swRoot 'SolidWorks.Interop.sldworks.dll'
$swConst = Join-Path $swRoot 'SolidWorks.Interop.swconst.dll'
if (!(Test-Path $swSldWorks)) { throw "SolidWorks interop not found: $swSldWorks" }
if (!(Test-Path $swConst)) { throw "SolidWorks interop not found: $swConst" }
$out = Join-Path $bin 'NozzleScheduleExtractor.Gui.exe'
& $csc /nologo /target:winexe "/out:$out" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/r:$swSldWorks" "/r:$swConst" $sources
Copy-Item -LiteralPath $swSldWorks -Destination (Join-Path $bin 'SolidWorks.Interop.sldworks.dll') -Force
Copy-Item -LiteralPath $swConst -Destination (Join-Path $bin 'SolidWorks.Interop.swconst.dll') -Force
