$ErrorActionPreference = 'Stop'
$avisoRoot = Split-Path -Parent $PSScriptRoot
$avisoCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $avisoCompiler)) { throw 'Se necesita el compilador C# de .NET Framework de 64 bits.' }
$avisoDist = Join-Path $avisoRoot 'dist'
New-Item -ItemType Directory -Path $avisoDist -Force | Out-Null
$avisoSource = Join-Path $avisoRoot 'src\AvisoTransparente.cs'
$avisoManifest = Join-Path $avisoRoot 'src\app.manifest'
$avisoOutput = Join-Path $avisoDist 'AvisoTransparente.exe'
& $avisoCompiler /nologo /optimize+ /target:winexe /platform:x64 "/win32manifest:$avisoManifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$avisoOutput" $avisoSource
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar la app.' }
Write-Output "Compilado: $avisoOutput"
