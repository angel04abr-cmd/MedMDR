# Interfaz nueva de MedMDR (estilo Fluent, WPF)

Estado: **Fases 1 a 4 hechas y cambio de nombre a MedMDR hecho** (versión 2.0.0). Falta la Fase 5 (calidad y publicación). La interfaz nueva
(`src/MedMDR.App`, WPF) **ya es la aplicación**: la de Windows Forms se retiró y el motor `MedMDR.Core` no cambió.

## Fases

| Fase | Contenido | Estado |
|---|---|---|
| 0 | Mejoras Fluent en la ventana de Windows Forms (título, filas redondeadas, botones, menús, pestañas) | Hecha (1.5.0) |
| 1 | Proyecto WPF con tema Fluent, medidas, iconos vectoriales propios y ventana principal que recorre carpetas | Hecha |
| 2 | Abrir y editar archivos comprimidos, panel lateral (accesos, favoritos, recientes), diálogos básicos (mensaje, contraseña, texto, progreso, resultado, visor, sobrescritura) y órdenes: extraer, comprobar, ver, información, informe, eliminar, renombrar, comentario, proteger, bloquear, reparar, autoextraíble, añadir a un archivo abierto y soltar ficheros | Hecha |
| 3 | Diálogos grandes: Añadir (crear archivos nuevos y añadir a uno abierto, con perfiles, contraseña, volúmenes, autoextraíble y recuperación), Extraer con opciones, Configuración (7 pestañas), Asistente, Convertir, Buscar | Hecha |
| 4 | Comportamiento delicado: arrastrar ficheros fuera del archivo, menú contextual, órdenes del menú de Windows (`--add`, `--extract-here`, `--extract-to`, `--test`), actualizaciones | Hecha |
| 5 | Calidad: capturas a 100/150/200 %, teclado y lector de pantalla, contraste alto, rendimiento con 100 000 ficheros, instalador | Pendiente |

## Cómo está hecho `src/MedMDR.App`

- **Tema:** el tema Fluent que trae WPF en .NET 10 (`Application.ThemeMode`, sistema/claro/oscuro). Cambia **en caliente**: todo se
  repinta solo (en Windows Forms hay que reabrir la ventana). El color de énfasis es el de Windows.
- `Themes/Tokens.xaml`: radios, espacios, tipografía (Segoe UI Variable). `Themes/Styles.xaml`: estilos propios (botón de comandos,
  fila de la lista). Los colores no se definen aquí: se usan los del tema (`TextFillColorPrimaryBrush`,
  `CardBackgroundFillColorDefaultBrush`, `SubtleFillColorSecondaryBrush`, `AccentFillColorDefaultBrush`…) para que sigan el tema.
- `Controls/Icon.cs`: iconos **propios** de línea (cuadrícula 24 × 24, trazo 1,5, extremos redondos). El trazo toma el color de texto
  del contenedor y los detalles el color de énfasis. `Controls/CommandButton.cs`: botón icono + texto de la barra de comandos.
- `FileItem.cs`, `Services/ShellInfo.cs`: filas de la lista; iconos de Windows para cada tipo de fichero e iconos propios para
  carpetas y archivos comprimidos.
- `MainWindow.xaml(.cs)`: menús, barra de comandos, barra de direcciones, panel lateral, lista (con orden por columna), menú
  contextual y barra de estado. `MainWindow.Commands.cs`: las órdenes (portadas de la lógica de `MedMDR.exe`, que usa el mismo motor).
- `Services/ArchiveSession.cs`: archivo abierto + contraseña (se pide una vez). `Dialogs/`: `MessageWindow` (`Msg.*`), `TextPromptWindow`
  (texto y contraseña), `ProgressWindow`, `ResultWindow`, `ViewerWindow`, `OverwriteWindow`, `AboutWindow`.
- Fase 3: `AddWindow` (+ `NewPasswordWindow`), `ExtractWindow`, `ConvertWindow`, `FindWindow`, `WizardWindow` y `SettingsWindow` en `Dialogs/`.
  `MainWindow.Create.cs` crea o actualiza archivos (`CreateArchive`, igual que en `MedMDR.exe`); `MainWindow.Settings.cs` aplica la
  configuración (texto e iconos de la barra, siempre visible, título con ruta, carpeta de inicio) y exporta, importa o restablece.
  Soltar ficheros en la ventana (fuera de un archivo) abre «Añadir» para crear uno nuevo.
- Fase 4: `MainWindow.DragOut.cs` (arrastre hacia fuera; desde un archivo extrae antes a una carpeta temporal), menú contextual con
  iconos y atajos (+ «Comprimir en…» y «Extraer en carpeta» sin preguntar), `MainWindow.Quick.cs` (órdenes del menú de Windows sin abrir la
  ventana principal), `Services/Updates.cs` + `Dialogs/UpdateWindow` (Ayuda > Buscar actualizaciones y aviso al abrir, una vez al día).
- **Nombre:** el programa se llama **MedMDR** (`AppInfo.Name`). Los nombres internos (proyecto, carpetas, repositorio, ficheros .exe/.msi)
  se cambiarán en la integración final (Fase 5), cuidando que las copias ya instaladas puedan actualizarse.

## Probar y hacer capturas

```
dotnet run --project src/MedMDR.App -c Release                  abre la ventana
MedMDR.exe --snapshot captura.png --theme light|dark [--then-theme dark|light] [--select N] carpeta
MedMDR.exe --dump-keys claves.txt                           lista los recursos del tema Fluent de WPF

Pruebas automáticas de órdenes (solo desarrollo):
MedMDR.exe --snapshot f.png --auto select:2,open,test,info --shots-dir capturas [--password X] [--yes] [--text T] carpeta
   --auto  órdenes separadas por comas: select:N, open, up, test, info, view, extract, extract-here, delete, rename, add:ruta1|ruta2,
           new:ruta1|ruta2, convert, find, wizard, settings, protect, lock, repair, report
   --add ruta… | --extract-here f | --extract-to f | --test f   órdenes del menú de Windows (sin ventana principal)
   --auto también acepta: dragprep (muestra las rutas que se arrastrarían) y menu (muestra las entradas del menú contextual)
   --accept  tras la captura, cada diálogo se acepta con sus valores (en vez de cancelarse); en el asistente recorre los 3 pasos
             (con --text ruta como origen); con --text, «Buscar» busca ese texto solo
   --shots-dir  guarda una captura de cada diálogo que se abra (y lo cierra)   --yes / --text  responden a las preguntas sin mostrarlas
```

Para capturas de documentación, usa una carpeta de demostración montada con `subst X: ruta` para que no salgan rutas personales.
