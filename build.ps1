# Compila Medrar y deja todo listo en la carpeta "dist\Medrar".
#   Medrar.exe          -> la aplicaciÃ³n de escritorio
#   Medrar.Sfx.exe      -> mÃ³dulo autoextraÃ­ble ligero (necesita .NET 10 donde se abra)
#   Medrar.Sfx.Full.exe -> mÃ³dulo autoextraÃ­ble completo (incluye .NET; no necesita nada instalado)
#   mdr.exe             -> versiÃ³n de lÃ­nea de comandos
# Requisitos: .NET SDK 10 (https://dotnet.microsoft.com/download).
# Con -Installer genera ademÃ¡s el instalador Medrar-<versiÃ³n>-x64.msi (WiX 6; se descarga solo en .tools\).
# Con -Zip o -Installer escribe tambiÃ©n dist\SHA256SUMS.txt.
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

dotnet publish (Join-Path $root 'src\Medrar.Sfx\Medrar.Sfx.csproj') @light -o (Join-Path $root 'dist\_sfx');            if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar el autoextraÃ­ble.' }
dotnet publish (Join-Path $root 'src\Medrar.Sfx\Medrar.Sfx.csproj') @full -o (Join-Path $root 'dist\_sfxfull');         if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar el autoextraÃ­ble completo.' }
dotnet publish (Join-Path $root 'src\Medrar.Cli\Medrar.Cli.csproj') @light -o (Join-Path $root 'dist\_cli');            if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar mdr.exe.' }
dotnet publish (Join-Path $root 'src\Medrar\Medrar.csproj') @light -o $dist;                                             if ($LASTEXITCODE -ne 0) { throw 'Fallo al compilar Medrar.exe.' }

Copy-Item (Join-Path $root 'dist\_sfx\Medrar.Sfx.exe') $dist
Copy-Item (Join-Path $root 'dist\_sfxfull\Medrar.Sfx.exe') (Join-Path $dist 'Medrar.Sfx.Full.exe')
Copy-Item (Join-Path $root 'dist\_cli\mdr.exe') $dist
Remove-Item (Join-Path $root 'dist\_sfx'), (Join-Path $root 'dist\_sfxfull'), (Join-Path $root 'dist\_cli') -Recurse -Force
foreach ($f in 'README.md', 'THIRD-PARTY-NOTICES.md') { if (Test-Path (Join-Path $root $f)) { Copy-Item (Join-Path $root $f) $dist } }

$version = ([xml](Get-Content (Join-Path $root 'src\Medrar\Medrar.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if ($Installer) {
    # WiX 6 (Ãºltima versiÃ³n sin cuota OSMF). Se instala en .tools\ la primera vez.
    $wix = Join-Path $root '.tools\wix.exe'
    if (-not (Test-Path $wix)) {
        dotnet tool install wix --version 6.0.2 --tool-path (Join-Path $root '.tools'); if ($LASTEXITCODE -ne 0) { throw 'No se pudo instalar WiX.' }
    }
    & $wix extension add WixToolset.UI.wixext/6.0.2 | Out-Null
    & $wix extension add WixToolset.Netfx.wixext/6.0.2 | Out-Null
    $msi = Join-Path $root "dist\Medrar-$version-x64.msi"
    Push-Location $root
    try {
        & $wix build installer\Medrar.wxs -arch x64 -d "Version=$version" -d "Source=dist\Medrar" -ext WixToolset.UI.wixext -ext WixToolset.Netfx.wixext -culture es-ES -o $msi
        if ($LASTEXITCODE -ne 0) { throw 'Fallo al crear el instalador MSI.' }
    } finally { Pop-Location }
    Write-Host "Instalador: $msi" -ForegroundColor Green
}

if ($Zip) {
    $zipPath = Join-Path $root "dist\Medrar-$version-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath }
    Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zipPath
    Write-Host "Paquete: $zipPath" -ForegroundColor Green
}

if ($Zip -or $Installer) {
    # Sumas SHA-256 de lo publicable: la app las usa para comprobar la descarga antes de actualizar.
    $sums = Get-ChildItem (Join-Path $root 'dist') -File | Where-Object { $_.Extension -in '.msi', '.zip' -and $_.Name -like "Medrar-$version-*" } | Sort-Object Name |
        ForEach-Object { '{0} *{1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
    [IO.File]::WriteAllLines((Join-Path $root 'dist\SHA256SUMS.txt'), $sums, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Sumas: dist\SHA256SUMS.txt" -ForegroundColor Green
}

Write-Host "`nListo: $dist" -ForegroundColor Green
Get-ChildItem $dist | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } } | Format-Table -AutoSize


