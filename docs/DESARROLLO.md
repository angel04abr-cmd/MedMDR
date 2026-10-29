# Guía de desarrollo de MedMDR

MedMDR es un gestor de archivos comprimidos para Windows (C#, .NET 10). Esta guía explica cómo está organizado el código y cómo
probarlo. Para usarlo, mira el [README](../README.md).

## Proyectos

```
src/MedMDR.Core     Motor sin interfaz: formatos, cifrado, recuperación, volúmenes, autoextraíbles, búsqueda, actualizador
src/MedMDR.App      Aplicación de escritorio (WPF con el tema Fluent de .NET 10) → MedMDR.exe
src/MedMDR.Cli      Línea de comandos → mdr.exe
src/MedMDR.Sfx      Módulo autoextraíble (ligero y «completo», con .NET incluido)
tests/MedMDR.Tests  Pruebas automáticas del motor, del actualizador y de la seguridad de los borrados
installer/          Instalador MSI (WiX 6) y su aviso de licencia
docs/FORMATO-MDR.md Especificación del formato .mdr
```

## Cómo está hecha la interfaz (`src/MedMDR.App`)

- **Tema:** el tema Fluent de WPF en .NET 10 (`Application.ThemeMode`: sistema, claro u oscuro). Cambia en caliente, sin reiniciar.
  Los colores no se definen en el proyecto: se usan los del tema (`TextFillColorPrimaryBrush`, `CardBackgroundFillColorDefaultBrush`,
  `SubtleFillColorSecondaryBrush`, `AccentFillColorDefaultBrush`…) para que sigan el tema y el color de énfasis de Windows.
- `Themes/Tokens.xaml` (radios, espacios, tipografía) y `Themes/Styles.xaml` (botón de comandos, fila de la lista, textos de formulario).
- `Controls/Icon.cs`: iconos **propios** de línea (cuadrícula 24 × 24, trazo 1,5, extremos redondos), cada uno con su color.
  `Controls/CommandButton.cs`: botón icono + texto de la barra de comandos (admite sin texto e iconos pequeños).
- `MainWindow.xaml(.cs)`: menús, barra de comandos, barra de direcciones, panel lateral (accesos, favoritos, recientes), lista y estado.
  El resto de `MainWindow` va por temas: `.Commands` (órdenes), `.Create` (crear o actualizar archivos), `.DragOut` (arrastrar fuera),
  `.Quick` (órdenes del menú de Windows sin abrir la ventana), `.Settings` (aplicar, exportar, importar y restablecer la configuración).
- `Dialogs/`: una ventana por diálogo (`AddWindow`, `ExtractWindow`, `ConvertWindow`, `FindWindow`, `WizardWindow`, `SettingsWindow`,
  `BenchmarkWindow`, `UpdateWindow`, mensajes, contraseñas, progreso, resultados, visor…). Todas se abren con `Ask(...)`/`Show(...)`
  y funcionan también sin ventana principal visible.
- **Arrastrar fuera** (`MainWindow.DragOut.cs`, `Services/OleDrag.cs` y, en el motor, `Services/VirtualFiles.cs`): `VirtualFilesDataObject` entrega los formatos de
  Windows «FileGroupDescriptorW» y «FileContents» (ficheros virtuales); `ArchiveDragSource` prepara la lista y extrae a una carpeta temporal solo lo que el destino
  pide (en archivos sólidos, todo de una pasada). `OleDrag` llama a `DoDragDrop` de Windows porque WPF solo arrastra datos que ya existen. La pulsación sobre una fila
  ya seleccionada no colapsa la selección (se decide al soltar). Ajuste: `AppSettings.DragAsVirtualFiles` (Configuración → Interfaz).
- `Services/`: `ArchiveSession` (archivo abierto + contraseña, se pide una vez), `Updates` (actualizaciones), `ShellInfo`, `Recycle`.

## Compilar, probar y empaquetar

```
dotnet test tests\MedMDR.Tests                                           pruebas
powershell -ExecutionPolicy Bypass -File build.ps1                       compila todo en dist\MedMDR
powershell -ExecutionPolicy Bypass -File build.ps1 -Test -Zip -Installer pruebas + .zip + .msi + SHA256SUMS.txt (en dist\)
```

`build.ps1 -Installer` descarga WiX 6 en `.tools\` la primera vez (se elige la 6.x a propósito: la 7 exige aceptar una cuota OSMF).

### Publicar una versión

1. Sube `<Version>` en los cuatro `.csproj` (formato `1.2.3`).
2. `git commit`, `git tag v1.2.3` y `git push origin main --tags`.
3. La acción `release.yml` compila, prueba y crea la release con el `.msi`, el `.zip` y `SHA256SUMS.txt`.
   La acción `msi-test.yml` instala y desinstala el MSI en un Windows limpio y prueba la actualización real desde la versión publicada anterior.

## Probar la ventana sin tocarla (solo desarrollo)

La aplicación acepta opciones para hacer capturas y ejecutar órdenes sin intervención:

```
MedMDR.exe --snapshot captura.png --theme light|dark [--then-theme dark|light] [--select N] carpeta
MedMDR.exe --snapshot f.png --auto select:2,open,test,info --shots-dir capturas [--password X] [--yes] [--text T] carpeta
MedMDR.exe --dialog about|update|add|settings|wizard|convert|find|password|benchmark --shots-dir capturas
MedMDR.exe --dump-keys claves.txt                        lista los recursos del tema Fluent de WPF
```

- `--auto`: órdenes separadas por comas: `select:N, open, up, test, info, view, extract, extract-here, delete, rename, add:ruta1|ruta2,
  new:ruta1|ruta2, convert, find, wizard, settings, benchmark, protect, lock, repair, report, dragprep, menu`. Requiere `--snapshot`.
- `--shots-dir`: guarda una captura de cada diálogo que se abra y lo cierra. Con `--accept` lo acepta con sus valores en vez de cancelarlo
  (en el asistente recorre los tres pasos, con `--text ruta` como origen). `--yes` y `--text` responden a las preguntas sin mostrarlas.
- Órdenes del menú de Windows (sin ventana principal): `--add ruta…`, `--extract-here f`, `--extract-to f`, `--test f`.

Cuidado al probar: usa una carpeta de demostración montada con `subst X: ruta` para que no salgan rutas personales en las capturas, entrecomilla
las rutas con espacios que pases como argumentos y limpia después `%APPDATA%\MedMDR\settings.json` (historial y recientes).

## Buscar errores (pruebas, agentes y skill)

Las pruebas de xunit no ven los fallos que solo ocurren al **ejecutar** la ventana (el cierre al borrar era un error de memoria nativo, el «(1)» de
«Comprimir en…» un nombre mal formado). Por eso hay tres capas:

1. `dotnet test tests\MedMDR.Tests` — el motor, el actualizador y la seguridad de los borrados.
2. `powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1` — **prueba de humo**: abre la aplicación de verdad en ~35 escenarios (borrar, comprimir,
   extraer, convertir, buscar, asistente, configuración, menú de Windows, archivo roto…) y la ida y vuelta de cada formato con `mdr.exe`. Mira el código de
   salida, los cuelgues, los fallos que Windows anota para `MedMDR.exe` y `error.log`, y comprueba el efecto real en disco. Trabaja en una carpeta temporal
   con `MEDMDR_DATA_DIR` (no toca tu configuración); el único rastro es un fichero `medmdr-smoke-borrar-*.txt` en la Papelera. `-Only texto` repite uno,
   `-SkipBuild` no compila, `-KeepData` conserva la carpeta temporal. Cada fallo nuevo debe acabar como un escenario aquí o una prueba de xunit.
   Además, `ShellDropTests` (opt-in con `MEDMDR_SHELL_DROP=1`) suelta lo arrastrado sobre el destino de soltar REAL del Explorador para una carpeta y compara los ficheros copiados.
3. Claude Code: la skill `/buscar-bugs [interfaz|formatos|instalador|todo]` (`.claude/skills/buscar-bugs`) ordena todo el proceso y reparte el trabajo entre
   tres agentes del proyecto (`.claude/agents/`): `medmdr-revisor` (lee el código buscando fallos reales; solo lectura), `medmdr-probador` (usa la
   aplicación con los modos de prueba y mira las capturas) y `medmdr-formatos` (estresa los formatos y escribe pruebas). Sus informes son pistas: cada
   hallazgo se reproduce antes de arreglarlo (en la primera revisión, 1 de 8 resultó falso).

Lo que **no** se puede comprobar así: arrastrar con el ratón al Explorador, menús desplegables abiertos, escalas de pantalla del 150 % o 200 %, alto contraste.
