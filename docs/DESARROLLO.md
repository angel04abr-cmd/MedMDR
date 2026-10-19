# Guía de desarrollo de MedMDR

MedMDR es un gestor de archivos comprimidos para Windows (C#, .NET 10). Esta guía explica cómo está organizado el código y cómo
probarlo. Para usarlo, mira el [README](../README.md).

## Proyectos

```
src/MedMDR.Core     Motor sin interfaz: formatos, cifrado, recuperación, volúmenes, autoextraíbles, búsqueda, actualizador
src/MedMDR.App      Aplicación de escritorio (WPF con el tema Fluent de .NET 10) → MedMDR.exe
src/MedMDR.Cli      Línea de comandos → mdr.exe
src/MedMDR.Sfx      Módulo autoextraíble (ligero y «completo», con .NET incluido)
tests/MedMDR.Tests  Pruebas automáticas del motor, del actualizador y de la migración desde «Medrar»
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
- `Services/`: `ArchiveSession` (archivo abierto + contraseña, se pide una vez), `Updates` (actualizaciones), `ShellInfo`, `Recycle`.

## Compilar, probar y empaquetar

```
dotnet test tests\MedMDR.Tests                                           pruebas
powershell -ExecutionPolicy Bypass -File build.ps1                       compila todo en dist\MedMDR
powershell -ExecutionPolicy Bypass -File build.ps1 -Test -Zip -Installer pruebas + .zip + .msi + SHA256SUMS.txt (en dist\)
```

`build.ps1 -Installer` descarga WiX 6 en `.tools\` la primera vez (se elige la 6.x a propósito: la 7 exige aceptar una cuota OSMF).
Además del `MedMDR-<versión>-x64.msi` crea una copia idéntica `Medrar-<versión>-x64.msi`: las copias instaladas cuando el programa
se llamaba Medrar solo buscan ese nombre para actualizarse. **Hay que seguir publicándola** mientras queden copias antiguas.

### Publicar una versión

1. Sube `<Version>` en los cuatro `.csproj` (formato `1.2.3`).
2. `git commit`, `git tag v1.2.3` y `git push origin main --tags`.
3. La acción `release.yml` compila, prueba y crea la release con el `.msi`, su copia con el nombre antiguo, el `.zip` y `SHA256SUMS.txt`.
   La acción `msi-test.yml` instala y desinstala el MSI en un Windows limpio y prueba la actualización real desde Medrar 1.5.0.

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

## Nombres que se conservan a propósito

El programa se llamaba **Medrar** hasta la versión 1.5. Para no dejar sin actualizar ni sin configuración a quien lo tenía instalado:

- La configuración se copia de `%APPDATA%\Medrar` a `%APPDATA%\MedMDR` la primera vez (`SettingsStore.MigrateLegacy`).
- Las claves antiguas del Explorador (`Medrar.Archive`, `Medrar.Add`…) se quitan al integrar o desintegrar (`ShellIntegration`).
- El actualizador acepta instaladores `MedMDR-*` y `Medrar-*` (`UpdateService`); un token guardado antes se sigue leyendo (`TokenProtector`).
- El instalador conserva su `UpgradeCode` (así actualiza y quita Medrar) y detecta las instalaciones 1.2.x de usuario (`HKCU\Software\Medrar`).
- GitHub redirige la dirección antigua del repositorio a la nueva: **no se debe crear otro repositorio llamado «Medrar»**, porque rompería esa
  redirección y las copias antiguas dejarían de actualizarse.
