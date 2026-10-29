# Prueba de humo de MedMDR: compila, abre la aplicación de verdad y comprueba que NADA se cae, se cuelga o deja un error.
#
# Qué detecta que las pruebas de xunit no ven: cierres de la ventana por errores nativos (por ejemplo el de «Borrar», que cerraba
# el programa con 0xC0000005), cuadros que se quedan esperando, nombres de fichero mal formados («(1)» de más), diálogos que no abren.
# Qué hace:
#   1. Compila (salvo -SkipBuild) y crea datos de prueba (con ñ, tildes, ficheros vacíos y binarios) en una carpeta temporal.
#   2. Lanza MedMDR.exe con sus modos de prueba (--auto, --accept, --snapshot…) en unos 30 escenarios y mira: código de salida,
#      si se colgó, si Windows anotó un fallo de MedMDR.exe y si se escribió algo en error.log. Además comprueba el efecto
#      real en disco (el fichero borrado ya no está, el archivo creado existe y se comprueba, los nombres son los esperados…).
#   3. Prueba de ida y vuelta con mdr.exe: crear → comprobar → extraer → comparar el contenido, en cada formato y con contraseña,
#      sólido, recuperación, volúmenes y autoextraíble.
# No toca tu configuración (usa MEDMDR_DATA_DIR) ni deja nada: borra la carpeta temporal y cierra lo que haya lanzado.
# Único rastro: el escenario de borrar manda a la Papelera un fichero llamado medmdr-smoke-borrar-*.txt.
# Uso:  powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1 [-SkipBuild] [-KeepData] [-Only <texto>]
# Código de salida: 0 si todo bien; 1 si algo falló (con el detalle en pantalla).

param([switch]$SkipBuild, [switch]$KeepData, [string]$Only = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$app = Join-Path $repo 'src\MedMDR.App\bin\Release\net10.0-windows\MedMDR.exe'
$cli = Join-Path $repo 'src\MedMDR.Cli\bin\Release\net10.0\mdr.exe'

if (-not $SkipBuild) {
    dotnet build (Join-Path $repo 'MedMDR.slnx') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
}
if (-not (Test-Path $app) -or -not (Test-Path $cli)) { throw 'Faltan MedMDR.exe o mdr.exe: compila primero (sin -SkipBuild).' }

$w = Join-Path ([IO.Path]::GetTempPath()) ('medmdr-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $w | Out-Null
$data = Join-Path $w 'data'
New-Item -ItemType Directory -Force $data | Out-Null
$oldEnv = $env:MEDMDR_DATA_DIR
$env:MEDMDR_DATA_DIR = $data      # configuración y error.log aparte: no se toca la del usuario
$results = New-Object System.Collections.Generic.List[object]
$started = New-Object System.Collections.Generic.List[int]

function Q([string]$s) { '"' + $s + '"' }
function Utf8([string]$path, [string]$text) { [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false))) }

# ---------------------------------------------------------------- datos de prueba
function New-Demo([string]$root) {
    $v = Join-Path $root 'Vacaciones'
    New-Item -ItemType Directory -Force (Join-Path $v 'Fotos'), (Join-Path $v 'Documentos'), (Join-Path $v 'vacía') | Out-Null
    $rnd = New-Object Random 7
    $b = New-Object byte[] 40000; $rnd.NextBytes($b); [IO.File]::WriteAllBytes((Join-Path $v 'Fotos\playa.bin'), $b)
    Utf8 (Join-Path $v 'Documentos\itinerario.txt') (('Itinerario del viaje: hola mundo. ') * 300)
    Utf8 (Join-Path $v 'Documentos\ñandú café.txt') (('Texto con ñ, tildes y acentos: canción. ') * 100)
    Utf8 (Join-Path $v 'notas.txt') (('Notas del viaje. ') * 200)
    Utf8 (Join-Path $v 'presupuesto.csv') "Nombre,Precio`nHotel,120`nVuelo,340`n"
    [IO.File]::WriteAllBytes((Join-Path $v 'vacio.dat'), (New-Object byte[] 0))
    Utf8 (Join-Path $root 'suelto1.txt') 'primero'
    Utf8 (Join-Path $root 'suelto2.txt') 'segundo'
}
function Copy-Demo([string]$name) {
    $d = Join-Path $w $name
    New-Item -ItemType Directory -Force $d | Out-Null
    Copy-Item (Join-Path $w 'demo\*') $d -Recurse -Force
    $d
}
function Tree-Hash([string]$dir) {
    $dir = (Resolve-Path $dir).Path.TrimEnd('\')
    (Get-ChildItem $dir -Recurse -File | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($dir.Length + 1).Replace('\', '/') + ':' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    }) -join "`n"
}
New-Demo (Join-Path $w 'demo')
$demoSrc = Join-Path $w 'demo\Vacaciones'

# ---------------------------------------------------------------- lanzar la aplicación y vigilarla
function Run-App([string[]]$argv, [int]$timeoutSec = 90, [int]$expectExit = 0) {
    $t0 = Get-Date
    $logBefore = 0; $logPath = Join-Path $data 'error.log'
    if (Test-Path $logPath) { $logBefore = (Get-Item $logPath).Length }
    $argLine = ($argv | ForEach-Object { if ($_ -match '[\s"]') { Q $_ } else { $_ } }) -join ' '
    $p = Start-Process $app -ArgumentList $argLine -PassThru
    $started.Add($p.Id)
    $problems = @()
    if (-not $p.WaitForExit($timeoutSec * 1000)) {
        $problems += "se quedó esperando más de $timeoutSec s (¿un cuadro sin cerrar?)"
        try { $p.Kill() } catch { }
    } elseif ($p.ExitCode -ne $expectExit) {
        $problems += ('terminó con el código 0x{0:X} (se esperaba {1})' -f $p.ExitCode, $expectExit)
    }
    Start-Sleep -Milliseconds 400
    $crash = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $t0 } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in 'Application Error', '.NET Runtime' -and $_.Message -match 'MedMDR\.exe' } | Select-Object -First 1
    if ($crash) { $problems += 'Windows anotó un fallo de MedMDR.exe: ' + (($crash.Message -split "`n" | Select-Object -First 3) -join ' ') }
    if (Test-Path $logPath) {
        $len = (Get-Item $logPath).Length
        if ($len -gt $logBefore) {
            $fs = [IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite'); $fs.Position = $logBefore
            $txt = (New-Object IO.StreamReader($fs)).ReadToEnd(); $fs.Close()
            $problems += 'error.log: ' + (($txt -split "`n" | Where-Object { $_.Trim() } | Select-Object -First 2) -join ' | ')
        }
    }
    $problems     # cada problema sale como una línea de texto (los escenarios los recogen)
}

function Scenario([string]$name, [scriptblock]$body) {
    if ($Only -and $name -notlike "*$Only*") { return }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $problems = @()
    try { $problems = @(& $body) | Where-Object { $_ } }
    catch { $problems = @('excepción en la prueba: ' + $_.Exception.Message) }
    $ok = $problems.Count -eq 0
    $results.Add([pscustomobject]@{ Prueba = $name; Resultado = $(if ($ok) { 'OK' } else { 'FALLA' }); Segundos = [math]::Round($sw.Elapsed.TotalSeconds, 1); Detalle = ($problems -join ' / ') })
    Write-Host ('{0,-6} {1}  ({2:N1} s){3}' -f $(if ($ok) { 'OK' } else { 'FALLA' }), $name, $sw.Elapsed.TotalSeconds, $(if ($ok) { '' } else { "`n         " + ($problems -join "`n         ") })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
function Mdr([string[]]$argv) {
    # mdr.exe escribe los errores por stderr: con $ErrorActionPreference = 'Stop' PowerShell 5.1 lo convertiría en excepción
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $o = & $cli @argv 2>&1 | ForEach-Object { "$_" }; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $prev }
    [pscustomobject]@{ Code = $code; Out = ($o -join "`n") }
}
function Entries([string]$archive, [string]$pw = '') {
    $a = @('l'); if ($pw) { $a += "-p$pw" }; $a += $archive
    (Mdr $a).Out
}

try {
    Write-Host "`n== Aplicación: abrir, borrar, crear, convertir, buscar… ==" -ForegroundColor Cyan

    Scenario 'Ventana: carpeta (claro y oscuro)' {
        foreach ($t in 'light', 'dark') {
            $png = Join-Path $w "carpeta-$t.png"
            Run-App @((Join-Path $w 'demo'), '--snapshot', $png, '--theme', $t)
            if (-not (Test-Path $png) -or (Get-Item $png).Length -lt 5000) { "no se generó la captura $t" }
        }
    }

    Scenario 'Borrar un fichero de una carpeta (Papelera)' {
        $d = Copy-Demo 's-borrar'
        Utf8 (Join-Path $d 'medmdr-smoke-borrar-a.txt') 'a'; Utf8 (Join-Path $d 'medmdr-smoke-borrar-b.txt') 'b'
        # filas: «..», carpeta Vacaciones, luego los ficheros por nombre
        Run-App @($d, '--snapshot', (Join-Path $w 'b1.png'), '--auto', 'select:2,delete', '--yes')
        $left = @(Get-ChildItem $d -File | ForEach-Object Name)
        if ($left -contains 'medmdr-smoke-borrar-a.txt') { 'el fichero seleccionado no se borró' }
        if ($left -notcontains 'medmdr-smoke-borrar-b.txt') { 'se borró un fichero que no estaba seleccionado' }
    }

    Scenario 'Borrar dentro de un archivo' {
        $d = Copy-Demo 's-borrar-dentro'
        $arc = Join-Path $d 'datos.mdr'
        $r = Mdr @('a', $arc, (Join-Path $d 'Vacaciones')); if ($r.Code -ne 0) { 'no se pudo crear el archivo de prueba' }
        $before = ([regex]::Match((Entries $arc), '(\d+) fichero')).Groups[1].Value
        Run-App @($arc, '--snapshot', (Join-Path $w 'b2.png'), '--auto', 'select:1,open,select:4,delete', '--yes')   # dentro de Vacaciones: 3 carpetas y luego los ficheros; la fila 4 es el primer fichero
        $after = ([regex]::Match((Entries $arc), '(\d+) fichero')).Groups[1].Value
        if ([int]$after -ne [int]$before - 1) { "antes $before ficheros, después $after (debía quedar uno menos)" }
        if ((Mdr @('t', $arc)).Code -ne 0) { 'el archivo quedó dañado tras borrar' }
    }

    Scenario 'Comprimir en… (nombre sin «(1)» salvo que exista)' {
        $d = Copy-Demo 's-comprimir'
        Run-App @($d, '--snapshot', (Join-Path $w 'c1.png'), '--auto', 'select:1,compress')
        if (-not (Test-Path (Join-Path $d 'Vacaciones.mdr'))) { 'no se creó Vacaciones.mdr (¿con otro nombre?): ' + ((Get-ChildItem $d -File | ForEach-Object Name) -join ', ') }
        if (Test-Path (Join-Path $d 'Vacaciones (1).mdr')) { 'añadió «(1)» aunque no existía ningún archivo con ese nombre' }
        Run-App @($d, '--snapshot', (Join-Path $w 'c2.png'), '--auto', 'select:1,compress')
        if (-not (Test-Path (Join-Path $d 'Vacaciones (1).mdr'))) { 'la segunda vez no creó «Vacaciones (1).mdr»' }
        if ((Mdr @('t', (Join-Path $d 'Vacaciones.mdr'))).Code -ne 0) { 'el archivo creado no pasa la comprobación' }
    }

    Scenario 'Extraer con opciones' {
        $d = Copy-Demo 's-extraer'
        $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', '-s', $arc, (Join-Path $d 'Vacaciones')))
        Run-App @($arc, '--snapshot', (Join-Path $w 'e1.png'), '--auto', 'extract', '--accept', '--shots-dir', (Join-Path $w 'shots-e'), '--yes')
        $dest = Join-Path $d 'v'
        if (-not (Test-Path $dest)) { 'no se creó la carpeta de destino' }
        elseif ((Tree-Hash (Join-Path $dest 'Vacaciones')) -ne (Tree-Hash (Join-Path $w 'demo\Vacaciones'))) { 'lo extraído no es idéntico al original' }
    }

    Scenario 'Convertir (.zip → .mdr)' {
        $d = Copy-Demo 's-convertir'
        $zip = Join-Path $d 'v.zip'; [void](Mdr @('a', '-fzip', $zip, (Join-Path $d 'Vacaciones')))
        Run-App @($zip, '--snapshot', (Join-Path $w 'cv.png'), '--auto', 'convert', '--accept', '--shots-dir', (Join-Path $w 'shots-cv'), '--yes')
        $out = Join-Path $d 'v.mdr'
        if (-not (Test-Path $out)) { 'no se creó v.mdr' } elseif ((Mdr @('t', $out)).Code -ne 0) { 'el .mdr convertido no pasa la comprobación' }
    }

    Scenario 'Añadir a un archivo abierto' {
        $d = Copy-Demo 's-anadir'
        $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', $arc, (Join-Path $d 'Vacaciones')))
        Run-App @($arc, '--snapshot', (Join-Path $w 'a1.png'), '--auto', ('add:' + (Join-Path $d 'suelto1.txt')), '--accept', '--shots-dir', (Join-Path $w 'shots-a'), '--yes')
        if ((Entries $arc) -notmatch 'suelto1\.txt') { 'el fichero no aparece dentro del archivo' }
        if ((Mdr @('t', $arc)).Code -ne 0) { 'el archivo quedó dañado' }
    }

    Scenario 'Crear un archivo nuevo (Añadir)' {
        $d = Copy-Demo 's-nuevo'
        Run-App @($d, '--snapshot', (Join-Path $w 'n1.png'), '--auto', ('new:' + (Join-Path $d 'Vacaciones')), '--accept', '--shots-dir', (Join-Path $w 'shots-n'), '--yes')
        $made = @(Get-ChildItem $d -Filter '*.mdr' -File)
        if ($made.Count -ne 1) { 'se esperaba un .mdr y hay ' + $made.Count } elseif ((Mdr @('t', $made[0].FullName)).Code -ne 0) { 'el archivo creado no pasa la comprobación' }
    }

    Scenario 'Asistente (crear)' {
        $d = Copy-Demo 's-asistente'
        Run-App @($d, '--snapshot', (Join-Path $w 'w1.png'), '--auto', 'wizard', '--text', (Join-Path $d 'Vacaciones'), '--accept', '--shots-dir', (Join-Path $w 'shots-w'), '--yes')
        if (-not (Test-Path (Join-Path $d 'Vacaciones.mdr'))) { 'el asistente no creó Vacaciones.mdr' }
    }

    Scenario 'Asistente: reemplazar un archivo que ya existe' {
        $d = Copy-Demo 's-reemplazar'
        Utf8 (Join-Path $d 'Vacaciones.mdr') 'archivo viejo que se reemplaza'
        Run-App @($d, '--snapshot', (Join-Path $w 'rp.png'), '--auto', 'wizard', '--text', (Join-Path $d 'Vacaciones'), '--accept', '--shots-dir', (Join-Path $w 'shots-rp'), '--yes')
        if ((Mdr @('t', (Join-Path $d 'Vacaciones.mdr'))).Code -ne 0) { 'el archivo nuevo no está o no pasa la comprobación' }
        if (Test-Path (Join-Path $d 'Vacaciones.mdr.medmdr-anterior')) { 'quedó la copia apartada del archivo anterior' }
    }

    Scenario 'Buscar dentro de carpeta y archivos' {
        $d = Copy-Demo 's-buscar'; [void](Mdr @('a', (Join-Path $d 'v.mdr'), (Join-Path $d 'Vacaciones')))
        Run-App @($d, '--snapshot', (Join-Path $w 'f1.png'), '--auto', 'find', '--text', 'Itinerario', '--shots-dir', (Join-Path $w 'shots-f'))
        if (@(Get-ChildItem (Join-Path $w 'shots-f') -Filter '*.png' -ErrorAction SilentlyContinue).Count -eq 0) { 'no se abrió la ventana de búsqueda' }
    }

    Scenario 'Configuración (aceptar) y fichero válido' {
        Run-App @((Join-Path $w 'demo'), '--snapshot', (Join-Path $w 's1.png'), '--auto', 'settings', '--accept', '--shots-dir', (Join-Path $w 'shots-s'))
        $f = Join-Path $data 'settings.json'
        if (-not (Test-Path $f)) { 'no se guardó settings.json' } else { try { [void](Get-Content $f -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { 'settings.json no es JSON válido' } }
    }

    Scenario 'Archivo abierto: comprobar, información, ver, informe' {
        $d = Copy-Demo 's-ver'; $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', '-rr5', $arc, (Join-Path $d 'Vacaciones')))
        Run-App @($arc, '--snapshot', (Join-Path $w 'v1.png'), '--auto', 'select:1,open,select:4,view,test,info', '--shots-dir', (Join-Path $w 'shots-v'), '--yes')
    }

    Scenario 'Archivo roto o ilegible: avisa y no se cierra' {
        $d = Copy-Demo 's-roto'
        Utf8 (Join-Path $d 'rota.zip') 'esto no es un zip'
        Run-App @('--test', (Join-Path $d 'rota.zip'), '--yes') 60 1          # menú de Windows: sale con 1 (no pudo) tras mostrar el aviso
        # filas: «..», Vacaciones, rota.zip, suelto1.txt…; Alt+T sobre el roto desde la ventana
        Run-App @($d, '--snapshot', (Join-Path $w 'r1.png'), '--auto', 'select:2,test', '--yes')
    }

    Scenario 'Archivo con contraseña (pide la clave)' {
        $d = Copy-Demo 's-clave'; $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', '-pClave1', $arc, (Join-Path $d 'Vacaciones')))
        Run-App @($arc, '--snapshot', (Join-Path $w 'p1.png'), '--password', 'Clave1', '--auto', 'select:1,open,test', '--shots-dir', (Join-Path $w 'shots-p'), '--yes')
    }

    Scenario 'Diálogos sueltos (about, update, add, settings, wizard, convert, find, password)' {
        foreach ($dlg in 'about', 'update', 'add', 'settings', 'wizard', 'convert', 'find', 'password') {
            $s = Join-Path $w "shots-d-$dlg"
            Run-App @('--dialog', $dlg, '--shots-dir', $s, '--theme', $(if ($dlg -in 'about', 'add', 'wizard') { 'dark' } else { 'light' }))
            if (@(Get-ChildItem $s -Filter '*.png' -ErrorAction SilentlyContinue).Count -eq 0) { "el diálogo $dlg no se abrió" }
        }
    }

    Scenario 'Prueba de velocidad' {
        $s = Join-Path $w 'shots-bench'
        Run-App @('--dialog', 'benchmark', '--shots-dir', $s) 120
        if (@(Get-ChildItem $s -Filter '*.png' -ErrorAction SilentlyContinue).Count -eq 0) { 'la ventana no terminó la prueba' }
    }

    Scenario 'Preparar el arrastre hacia fuera y menú contextual' {
        $d = Copy-Demo 's-arrastre'; $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', $arc, (Join-Path $d 'Vacaciones')))
        Run-App @($arc, '--snapshot', (Join-Path $w 'd1.png'), '--auto', 'select:1,dragprep,menu')
        Run-App @($d, '--snapshot', (Join-Path $w 'd2.png'), '--auto', 'select:2,menu')
    }

    Scenario 'Arrastre con ficheros virtuales (carpeta y fichero de un archivo)' {
        $d = Copy-Demo 's-virtual'; $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', '-s', $arc, (Join-Path $d 'Vacaciones')))
        # dentro de Vacaciones: «..», 3 carpetas (Documentos, Fotos, vacía) y luego los ficheros
        Run-App @($arc, '--snapshot', (Join-Path $w 'dv1.png'), '--auto', 'select:1,open,select:1,dragvirtual,select:4,dragvirtual')
    }

    Write-Host "`n== Órdenes del menú de Windows (sin ventana principal) ==" -ForegroundColor Cyan

    Scenario 'Menú de Windows: --test' {
        $d = Copy-Demo 's-w-test'; $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', $arc, (Join-Path $d 'Vacaciones')))
        Run-App @('--test', $arc, '--shots-dir', (Join-Path $w 'shots-wt'))
    }
    Scenario 'Menú de Windows: --extract-here' {
        $d = Copy-Demo 's-w-aqui'; $arc = Join-Path $d 'v.mdr'; [void](Mdr @('a', $arc, (Join-Path $d 'Vacaciones')))
        Remove-Item (Join-Path $d 'Vacaciones') -Recurse -Force
        Run-App @('--extract-here', $arc, '--yes')
        if (-not (Test-Path (Join-Path $d 'Vacaciones\notas.txt'))) { 'no extrajo junto al archivo' }
    }
    Scenario 'Menú de Windows: --extract-named (carpeta con el nombre del archivo)' {
        $d = Copy-Demo 's-w-nombre'; $arc = Join-Path $d 'mis fotos.mdr'; [void](Mdr @('a', $arc, (Join-Path $d 'Vacaciones')))
        Run-App @('--extract-named', $arc, '--yes')
        $dest = Join-Path $d 'mis fotos'
        if (-not (Test-Path (Join-Path $dest ('Vacaciones' + [char]92 + 'notas.txt')))) { 'no extrajo en una carpeta con el nombre del archivo' }
        elseif ((Tree-Hash (Join-Path $dest 'Vacaciones')) -ne (Tree-Hash (Join-Path $w 'demo\Vacaciones'))) { 'lo extraído no es idéntico al original' }
    }
    Scenario 'Menú de Windows: --add' {
        $d = Copy-Demo 's-w-add'
        Run-App @('--add', (Join-Path $d 'Vacaciones'), '--accept', '--shots-dir', (Join-Path $w 'shots-wa'), '--yes')
        if (@(Get-ChildItem $d -Filter '*.mdr' -File).Count -ne 1) { 'no creó el archivo' }
    }

    Write-Host "`n== Ida y vuelta con mdr.exe: crear → comprobar → extraer → comparar ==" -ForegroundColor Cyan
    $expected = Tree-Hash $demoSrc
    $matrix = @(
        @{ N = 'mdr'; A = @('-m3'); P = '' }, @{ N = 'mdr sólido'; A = @('-m4', '-s'); P = '' },
        @{ N = 'mdr Ultra'; A = @('-m6', '-s'); P = '' }, @{ N = 'mdr Deflate'; A = @('-deflate'); P = '' },
        @{ N = 'mdr con clave y nombres cifrados'; A = @('-hpClave1'); P = 'Clave1' }, @{ N = 'mdr con recuperación'; A = @('-rr5'); P = '' },
        @{ N = 'mdr en volúmenes'; A = @('-v20k'); P = ''; Ext = '.mdr.001'; Vol = $true },
        @{ N = 'zip'; A = @('-fzip'); P = '' }, @{ N = 'zip con clave'; A = @('-fzip', '-pClave1'); P = 'Clave1' },
        @{ N = '7z'; A = @('-f7z'); P = '' }, @{ N = '7z con clave y nombres cifrados'; A = @('-f7z', '-hpClave1'); P = 'Clave1' },
        @{ N = 'tar.gz'; A = @('-ftgz'); P = '' }, @{ N = 'tar'; A = @('-ftar'); P = '' }
    )
    $i = 0
    foreach ($m in $matrix) {
        $i++
        $name = 'Formato: ' + $m.N
        Scenario $name {
            $d = Join-Path $w "m$i"; New-Item -ItemType Directory $d | Out-Null
            $base = Join-Path $d 'salida'
            $ext = if ($m.A -contains '-fzip') { '.zip' } elseif ($m.A -contains '-f7z') { '.7z' } elseif ($m.A -contains '-ftgz') { '.tar.gz' } elseif ($m.A -contains '-ftar') { '.tar' } else { '.mdr' }
            $arc = $base + $ext
            $open = if ($m.Vol) { $arc + '.001' } else { $arc }
            $r = Mdr (@('a') + $m.A + @($arc, $demoSrc)); if ($r.Code -ne 0) { 'crear falló: ' + $r.Out; return }
            $pw = @(); if ($m.P) { $pw = @("-p$($m.P)") }
            $t = Mdr (@('t') + $pw + @($open)); if ($t.Code -ne 0) { 'comprobar falló: ' + $t.Out; return }
            $out = Join-Path $d 'fuera'
            $x = Mdr (@('x') + $pw + @("-o$out", $open)); if ($x.Code -ne 0) { 'extraer falló: ' + $x.Out; return }
            $got = Tree-Hash (Join-Path $out 'Vacaciones')
            if ($got -ne $expected) { 'el contenido extraído no es idéntico al original' }
            if ($m.P) { $bad = Mdr @('t', '-pMala', $open); if ($bad.Code -eq 0) { 'con una clave incorrecta no debería comprobar bien' } }
        }
    }
    Scenario 'Formato: autoextraíble (.exe se genera y lista)' {
        $d = Join-Path $w 'sfx'; New-Item -ItemType Directory $d | Out-Null
        Copy-Item (Join-Path (Split-Path $app) 'MedMDR.Sfx.exe') $d -ErrorAction SilentlyContinue
        $sfx = Join-Path $repo 'src\MedMDR.Sfx\bin\Release\net10.0-windows\MedMDR.Sfx.exe'
        if (-not (Test-Path $sfx)) { return 'falta MedMDR.Sfx.exe (compila sin -SkipBuild)' }
        Copy-Item $sfx (Join-Path (Split-Path $cli) 'MedMDR.Sfx.exe') -Force
        $r = Mdr @('a', '-sfx', (Join-Path $d 'auto.exe'), $demoSrc); if ($r.Code -ne 0) { return 'crear falló: ' + $r.Out }
        if ((Mdr @('t', (Join-Path $d 'auto.exe'))).Code -ne 0) { 'el autoextraíble no pasa la comprobación' }
    }
}
finally {
    foreach ($id in $started) { try { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue } catch { } }
    $env:MEDMDR_DATA_DIR = $oldEnv
    if (-not $KeepData) { Remove-Item $w -Recurse -Force -ErrorAction SilentlyContinue } else { Write-Host "Datos de la prueba en: $w" }
}

$fail = @($results | Where-Object Resultado -eq 'FALLA')
Write-Host ''
Write-Host ('Resumen: {0} pruebas, {1} bien, {2} con fallos.' -f $results.Count, ($results.Count - $fail.Count), $fail.Count) -ForegroundColor $(if ($fail.Count) { 'Red' } else { 'Green' })
if ($fail.Count) { $fail | Format-Table Prueba, Detalle -AutoSize -Wrap | Out-String -Width 200 | Write-Host; exit 1 }
exit 0
