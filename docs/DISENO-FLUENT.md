# Interfaz nueva de Medrar (estilo Fluent, WPF)

Estado: **Fases 1 y 2 hechas** (base, ventana principal con archivos y panel lateral). La aplicación completa sigue siendo `Medrar.exe`
(Windows Forms, con el aspecto Fluent «fase 0» de la 1.5.0). El proyecto nuevo es `src/Medrar.Wpf` y comparte el motor
(`Medrar.Core`) sin tocarlo.

## Fases

| Fase | Contenido | Estado |
|---|---|---|
| 0 | Mejoras Fluent en la ventana de Windows Forms (título, filas redondeadas, botones, menús, pestañas) | Hecha (1.5.0) |
| 1 | Proyecto WPF con tema Fluent, medidas, iconos vectoriales propios y ventana principal que recorre carpetas | Hecha |
| 2 | Abrir y editar archivos comprimidos, panel lateral (accesos, favoritos, recientes), diálogos básicos (mensaje, contraseña, texto, progreso, resultado, visor, sobrescritura) y órdenes: extraer, comprobar, ver, información, informe, eliminar, renombrar, comentario, proteger, bloquear, reparar, autoextraíble, añadir a un archivo abierto y soltar ficheros | Hecha |
| 3 | Diálogos grandes: Añadir (crear archivos nuevos), Extraer con opciones, Configuración, Asistente, Convertir, Buscar | Pendiente |
| 4 | Comportamiento delicado: arrastrar ficheros fuera del archivo, menú contextual, asociación de archivos, actualizaciones | Pendiente |
| 5 | Calidad: capturas a 100/150/200 %, teclado y lector de pantalla, contraste alto, rendimiento con 100 000 ficheros, instalador | Pendiente |

## Cómo está hecho `src/Medrar.Wpf`

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
  contextual y barra de estado. `MainWindow.Commands.cs`: las órdenes (portadas de la lógica de `Medrar.exe`, que usa el mismo motor).
- `Services/ArchiveSession.cs`: archivo abierto + contraseña (se pide una vez). `Dialogs/`: `MessageWindow` (`Msg.*`), `TextPromptWindow`
  (texto y contraseña), `ProgressWindow`, `ResultWindow`, `ViewerWindow`, `OverwriteWindow`, `AboutWindow`.
- Lo que sigue sin conectar (Fase 3): crear un archivo nuevo, Asistente, Convertir archivos, Buscar ficheros y Configuración.
- **Nombre:** el programa se llama **MedMDR** (`AppInfo.Name`). Los nombres internos (proyecto, carpetas, repositorio, ficheros .exe/.msi)
  se cambiarán en la integración final (Fase 5), cuidando que las copias ya instaladas puedan actualizarse.

## Probar y hacer capturas

```
dotnet run --project src/Medrar.Wpf -c Release                  abre la ventana
Medrar.Wpf.exe --snapshot captura.png --theme light|dark [--then-theme dark|light] [--select N] carpeta
Medrar.Wpf.exe --dump-keys claves.txt                           lista los recursos del tema Fluent de WPF

Pruebas automáticas de órdenes (solo desarrollo):
Medrar.Wpf.exe --snapshot f.png --auto select:2,open,test,info --shots-dir capturas [--password X] [--yes] [--text T] carpeta
   --auto  órdenes separadas por comas: select:N, open, up, test, info, view, extract-here, delete, rename, add:ruta1|ruta2, protect, lock, repair, report
   --shots-dir  guarda una captura de cada diálogo que se abra (y lo cierra)   --yes / --text  responden a las preguntas sin mostrarlas
```

Para capturas de documentación, usa una carpeta de demostración montada con `subst X: ruta` para que no salgan rutas personales.
