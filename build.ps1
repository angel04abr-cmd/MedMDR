# Compila Medrar y deja todo listo en la carpeta "dist\Medrar".
#   Medrar.exe          -> la aplicación de escritorio
#   Medrar.Sfx.exe      -> módulo autoextraíble ligero (necesita .NET 10 donde se abra)
#   Medrar.Sfx.Full.exe -> módulo autoextraíble completo (incluye .NET; no necesita nada instalado)
#   mdr.exe             -> versión de línea de comandos
# Requisitos: .NET SDK 10 (https://dotnet.microsoft.com/download).
# Con -Installer genera además el instalador Medrar-Setup-<versión>.exe (necesita Inno Setup 6 o 7).
# Uso:  powershell -ExecutionPolicy Bypass -File build.ps1 [-Test] [-Zip] [-Installer]

param([switch]$Test, [switch]$Zip, [switch]$Installer)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist\Medrar'
if (Test-Path (Join-Path $root 'dist')) { Remove-Item (Join-Path $root 'dist') -Recurse -Force -Exclude '*.zip' -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force $dist | Out-Null

if ($Test) {
    dotnet test (Join-Path $root 'tests\Medrar.Tests\Medrar.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas han fallado.' }
}

$light = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-p:PublishSingleFile=true', '-p:DebugType=none', '-p:DebugSymbols=false')
$full = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:EnableCompressionInSingleFile=true',
          '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none', '-p:DebugSymbols=false')

dotnet publish (Join-Path $root 'src\Medrar.Sfx\Medrar.Sfx.csproj') @light -o (Join-Path $root 'dist\_sfx');            if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar el autoextraíble.' }
dotnet publish (Join-Path $root 'src\Medrar.Sfx\Medrar.Sfx.csproj') @full -o (Join-Path $root 'dist\_sfxfull');         if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar el autoextraíble completo.' }
dotnet publish (Join-Path $root 'src\Medrar.Cli\Medrar.Cli.csproj') @light -o (Join-Path $root 'dist\_cli');            if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar mdr.exe.' }
dotnet publish (Join-Path $root 'src\Medrar\Medrar.csproj') @light -o $dist;                                             if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar Medrar.exe.' }

Copy-Item (Join-Path $root 'dist\_sfx\Medrar.Sfx.exe') $dist
Copy-Item (Join-Path $root 'dist\_sfxfull\Medrar.Sfx.exe') (Join-Path $dist 'Medrar.Sfx.Full.exe')
Copy-Item (Join-Path $root 'dist\_cli\mdr.exe') $dist
Remove-Item (Join-Path $root 'dist\_sfx'), (Join-Path $root 'dist\_sfxfull'), (Join-Path $root 'dist\_cli') -Recurse -Force
foreach ($f in 'README.md', 'THIRD-PARTY-NOTICES.md') { if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $dist } }

$version = ([xml](Get-Content (Join-Path $root 'src\Medrar\Medrar.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if ($Installer) {
    $iscc = @("$env:ProgramFiles\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { $c = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($c) { $iscc = $c.Source } }
    if (-not $iscc) { throw 'No se encuentra Inno Setup (ISCC.exe). Instálalo desde https://jrsoftware.org/isdl.php' }
    & $iscc "/DAppVersion=$version" (Join-Path $root 'installer\Medrar.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Fallo al crear el instalador.' }
    Write-Host "Instalador: $(Join-Path $root "dist\Medrar-Setup-$version.exe")" -ForegroundColor Green
}

if ($Zip) {
    $zipPath = Join-Path $root "dist\Medrar-$version-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zipPath
    Write-Host "Paquete: $zipPath" -ForegroundColor Green
}

Write-Host "`nListo: $dist" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize
