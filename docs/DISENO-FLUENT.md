# Interfaz nueva de Medrar (estilo Fluent, WPF)

Estado: **Fase 1 hecha** (base + ventana principal en construcción). La aplicación completa sigue siendo `Medrar.exe`
(Windows Forms, con el aspecto Fluent «fase 0» de la 1.5.0). El proyecto nuevo es `src/Medrar.Wpf` y comparte el motor
(`Medrar.Core`) sin tocarlo.

## Fases

| Fase | Contenido | Estado |
|---|---|---|
| 0 | Mejoras Fluent en la ventana de Windows Forms (título, filas redondeadas, botones, menús, pestañas) | Hecha (1.5.0) |
| 1 | Proyecto WPF con tema Fluent, medidas, iconos vectoriales propios y ventana principal que recorre carpetas | Hecha |
| 2 | Ventana principal completa: abrir/editar archivos, panel lateral (favoritos, recientes), todas las órdenes y menús | Pendiente |
| 3 | Diálogos: Añadir, Extraer, Configuración, Asistente, Convertir, Buscar, Contraseña, Progreso, Reparar | Pendiente |
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
- `MainWindow.xaml(.cs)`: menús, barra de comandos, barra de direcciones, lista (con orden por columna) y barra de estado. Las
  órdenes aún no hacen nada: avisan de que llegan en la Fase 2.

## Probar y hacer capturas

```
dotnet run --project src/Medrar.Wpf -c Release                  abre la ventana
Medrar.Wpf.exe --snapshot captura.png --theme light|dark [--then-theme dark|light] [--select N] carpeta
Medrar.Wpf.exe --dump-keys claves.txt                           lista los recursos del tema Fluent de WPF
```

Para capturas de documentación, usa una carpeta de demostración montada con `subst X: ruta` para que no salgan rutas personales.
