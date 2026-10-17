# Medrar

Gestor de archivos comprimidos para Windows, escrito desde cero en C# (.NET 10, Windows Forms).
**Crea** archivos **.mdr** (formato propio), **ZIP** y **7Z** (ambos también con contraseña AES-256), **TAR**, **TAR.GZ** y **GZ**,
y **abre** además RAR (también con contraseña), BZ2, XZ, LZ, CAB, ISO y otros.

> Medrar es una versión propia inspirada en las funciones de un gestor clásico de archivos comprimidos.
> No contiene código, imágenes, iconos ni textos de ningún otro programa: todo está escrito y dibujado aquí.

![Ventana principal](docs/capturas/ventana-archivo.png)

## Descargar e instalar

Ve a **Releases** (a la derecha en GitHub) y baja **`Medrar-Setup-<versión>.exe`**: es el instalador.

1. Ábrelo y sigue el asistente (está en español). Por defecto se instala **solo para tu usuario** y no pide permisos de
   administrador; si quieres instalarlo para todos los usuarios, el asistente te lo permite.
2. Puedes marcar si quieres asociar los archivos `.mdr`, añadir «Añadir a un archivo con Medrar» al menú del botón derecho y
   crear un acceso directo en el escritorio.
3. Si falta el [**.NET 10 Desktop Runtime**](https://dotnet.microsoft.com/download/dotnet/10.0), el instalador te avisa y te
   lleva a la página de descarga.
4. Para **actualizar**, baja el instalador de la versión nueva y ejecútalo encima (conserva tu configuración).
5. Para **desinstalar**: Configuración de Windows → Aplicaciones → Medrar. Tu configuración (`%APPDATA%\Medrar`) no se borra.

> El instalador **no está firmado digitalmente**, así que Windows SmartScreen puede mostrar «Windows protegió su PC»:
> pulsa *Más información → Ejecutar de todos modos*. (Firmar exige un certificado de pago.)
> El repositorio es privado: para descargar hay que iniciar sesión en GitHub con la cuenta que tiene acceso.

**Sin instalar (portable):** la release también trae `Medrar-<versión>-win-x64.zip`; descomprímelo y abre `Medrar.exe`.
Medrar **no se actualiza solo** (no usa la red a propósito): las versiones nuevas se descargan de Releases.

| Fichero | Para qué sirve |
|---|---|
| `Medrar.exe` | La aplicación de escritorio. Haz doble clic. |
| `mdr.exe` | La misma funcionalidad en línea de comandos. |
| `Medrar.Sfx.exe` | Módulo para crear archivos autoextraíbles ligeros. Debe estar junto a `Medrar.exe`. |
| `Medrar.Sfx.Full.exe` | Módulo para autoextraíbles que incluyen .NET (funcionan en cualquier Windows; pesan ~50 MB más). Junto a `Medrar.exe`. |

## Cómo se usa

**Explorar.** La ventana es un explorador de archivos. Doble clic en una carpeta para entrar; doble clic en un archivo
comprimido para abrirlo como si fuera una carpeta. `Retroceso` sube un nivel. Puedes escribir una ruta en la barra de direcciones.

**Crear un archivo.**
1. Selecciona ficheros o carpetas (o arrástralos a la ventana).
2. Pulsa **Añadir** (`Alt+A`).
3. Elige nombre, formato y opciones → **Aceptar**.

![Diálogo Añadir](docs/capturas/dialogo-anadir.png)

**Extraer.** Abre o selecciona el archivo y pulsa **Extraer en** (`Alt+E`). `Alt+W` extrae sin hacer preguntas.
También puedes **arrastrar ficheros desde dentro del archivo** a una carpeta del Explorador.

**Contraseña.** En «Añadir» → *Establecer contraseña*. Vale para **.mdr**, **ZIP** y **7Z** (AES-256; ZIP y 7Z los abren 7-Zip, WinRAR y demás).
En .mdr y 7Z puedes marcar «Cifrar también los nombres» para que ni siquiera se vea la lista de ficheros; el ZIP no puede ocultar
los nombres. Medrar **no guarda contraseñas**. Al abrir un archivo cifrado, Medrar pide la contraseña y comprueba que sea correcta.

**Proteger y reparar.** «Registro de recuperación» añade datos de paridad (códigos Reed-Solomon) para reparar un archivo dañado
(*Herramientas → Reparar archivo*). En .mdr va dentro; en ZIP, 7Z, TAR… va en un fichero aparte `nombre.ext.mdrrec`
(guárdalo junto al archivo). La reparación crea una **copia** y no toca el original. Se pueden reparar varios bloques dañados
(con 5 %: unos 10 bloques de 16 KB por cada ~200), también seguidos, y con el fichero `.mdrrec` incluso un archivo **truncado**.

**Bloquear.** *Órdenes → Bloquear / desbloquear*: impide modificar el archivo desde Medrar (en .mdr con una marca interna;
en los demás formatos con el atributo de solo lectura del fichero).

**Volúmenes.** «Dividir en volúmenes» parte el archivo en `.001`, `.002`… Abre el `.001` y Medrar los une solo.

**Autoextraíbles.** «Crear archivo autoextraíble» genera un `.exe` que se abre con doble clic, para **cualquier formato**.
La versión ligera necesita .NET 10 en el equipo de destino; marcando «Incluir .NET» funciona en cualquier Windows (pesa ~50 MB más).
Un autoextraíble es de solo lectura en Medrar; *Herramientas → Quitar el módulo autoextraíble* devuelve el archivo original.

### Atajos de teclado

| Atajo | Acción | Atajo | Acción |
|---|---|---|---|
| `Alt+A` | Añadir | `Alt+I` | Información del archivo |
| `Alt+E` | Extraer en… | `Alt+R` | Reparar |
| `Alt+T` | Comprobar | `Alt+Q` | Convertir archivos |
| `Alt+V` | Ver fichero | `Alt+X` | Convertir en autoextraíble |
| `Supr` | Eliminar | `Alt+P` | Proteger contra daños |
| `F2` | Renombrar | `Alt+L` | Bloquear / desbloquear |
| `F3` | Buscar | `Ctrl+O` | Abrir archivo |
| `Ctrl+S` | Configuración | `F1` | Ayuda |

### Todas las funciones

- **Órdenes:** añadir, extraer, comprobar, ver (texto, hexadecimal e imágenes), eliminar, renombrar, comentario, proteger, bloquear.
- **Herramientas:** asistente paso a paso, convertir entre formatos, reparar, autoextraíbles, buscar texto o bytes
  (en carpetas y dentro de archivos), información, informe (HTML/TXT), prueba de velocidad.
- **Favoritos**, historial, archivos recientes, perfiles de compresión, importar/exportar configuración.
- **Temas** claro/oscuro/sistema, arrastrar y soltar (dentro y fuera), menú contextual.
- **Integración con Windows** opcional (Configuración → Integración): asociar `.mdr` y menú del botón derecho.
  Solo escribe en tu usuario (HKCU) y se quita con un clic.

### Línea de comandos

```
mdr a -m4 -s -rr5 -hpMiClave datos.mdr carpeta\      crear .mdr (nivel 4, sólido, recuperación 5 %, contraseña y nombres cifrados)
mdr a -fzip -pMiClave datos.zip carpeta\             ZIP con contraseña AES-256
mdr a -f7z -hpMiClave datos.7z carpeta\              7Z con contraseña y nombres cifrados
mdr a -sfxfull -fzip datos carpeta\                  autoextraíble que incluye .NET
mdr x -pMiClave -ocarpeta datos.mdr                  extraer
mdr l datos.mdr     mdr t datos.mdr     mdr i datos.mdr     listar / comprobar / información
mdr d datos.mdr "*.tmp"      mdr c viejo.rar nuevo.mdr      eliminar / convertir
mdr r datos.zip             mdr s datos.7z                  reparar / hacer autoextraíble
mdr -h                                                      ayuda completa
```

## Límites (leer antes de confiar en Medrar)

- **No crea .rar.** El formato RAR es propietario y su licencia no permite reimplementar su compresor. Medrar los **abre y extrae**
  (incluidos sólidos y con contraseña) y los **convierte** a .mdr, ZIP o 7Z. Para compartir, usa ZIP o 7Z.
- **.mdr es un formato propio:** solo lo abren Medrar y su módulo autoextraíble. Está **documentado** en
  [`docs/FORMATO-MDR.md`](docs/FORMATO-MDR.md) para que otros puedan leerlo, pero ningún otro programa lo abre hoy.
- **7Z con contraseña:** el cifrado de 7-Zip (AES-256-CBC) **no autentica** los datos: una clave mala o un archivo dañado se notan por
  la suma de control o porque falla la descompresión, pero no hay una firma como en .mdr o ZIP. Los 7Z con contraseña se crean
  siempre «sólidos» (un único bloque).
- **ZIP con contraseña: los nombres de los ficheros no se cifran** (limitación del formato ZIP). Solo se cifra el contenido.
  En .mdr sí se pueden cifrar los nombres.
- **El bloqueo es una protección contra accidentes, no seguridad:** se deshace desde Medrar o quitando «solo lectura».
- **Recuperación:** repara hasta el porcentaje elegido por grupo de bloques; si el daño es mayor, lo dice y no inventa datos.
  Un **.mdr con el registro dentro** no se puede reparar si pierde el final (ahí está el registro): para ese caso usa un ZIP/7Z con
  su fichero `.mdrrec`, que sí sirve para archivos truncados. Si borras o pierdes el `.mdrrec`, no hay reparación.
- **Autoextraíbles:** el ligero necesita .NET 10; el «completo» no necesita nada pero pesa ~50 MB. No admiten volúmenes.
  Un autoextraíble no lo pueden abrir otros programas como si fuera un archivo normal (sí Medrar).
- **Arrastrar fuera:** con poco contenido (hasta ~64 MB) es inmediato. Con más, Medrar extrae primero (con progreso) y hay
  que volver a arrastrar la selección.
- En modo sólido, el «tamaño comprimido» por fichero es una **estimación proporcional**.
- Editar un archivo (añadir, eliminar, renombrar) lo **reescribe entero** en un temporal y lo sustituye al terminar; con
  archivos grandes necesita espacio libre en disco. RAR y autoextraíbles son de solo lectura.
- **RAR con contraseña:** se abren los de cabeceras cifradas (Medrar pide la clave al abrir) y los de solo contenido cifrado
  (se ve el listado y se pide la clave al extraer). En RAR cifrados no se puede comparar la suma de control normal, así que un
  error solo se detecta si falla el descifrado o la descompresión. Probado con RAR 5 creados por WinRAR 7.23; RAR 4 cifrados, no.
- **Qué se probó a mano en la ventana real:** abrir, ver, comprobar y extraer con **.mdr, ZIP con contraseña y RAR con contraseña**;
  crear **.mdr** y **ZIP con contraseña**; editar (eliminar o renombrar) en **.mdr, ZIP con y sin contraseña, 7Z y TAR.GZ**;
  proteger, reparar un ZIP dañado y arrastrar fuera del archivo. Los autoextraíbles se probaron ejecutándolos (ligero y completo,
  con ZIP, 7Z, .mdr y contraseña). Crear 7Z, TAR.GZ y autoextraíbles se hizo con la línea de comandos y las pruebas automáticas
  (el 7Z, el 7Z con contraseña y el ZIP con contraseña además se abrieron con WinRAR), no desde el diálogo de la ventana.

## Qué guarda y qué envía

- Guarda su configuración en `%APPDATA%\Medrar\settings.json` (tema, favoritos, perfiles, carpetas recientes). **Nunca contraseñas.**
- Los errores inesperados se anotan en `%APPDATA%\Medrar\error.log`.
- **No usa la red**: el programa no incluye ningún componente de red ni de telemetría.
- Al abrir un fichero del archivo con doble clic o arrastrarlo, se extrae a `%TEMP%\Medrar\` y se borra al cerrar Medrar.

## Seguridad

- **.mdr:** AES-256-GCM por bloques de 64 KB (cada bloque se autentica) con clave derivada por PBKDF2-HMAC-SHA256, 200 000
  iteraciones y sal aleatoria de 16 bytes.
- **7Z:** AES-256-CBC de 7-Zip (clave por SHA-256 repetido 2^19 veces con sal aleatoria; IV aleatorio), con cabecera cifrada
  opcional para ocultar los nombres.
- **ZIP:** cifrado AES-256 de WinZip (AE-2: clave por PBKDF2-HMAC-SHA1 con 1 000 iteraciones, **fijadas por el formato**, y
  firma HMAC-SHA1 de cada fichero). Medrar **verifica la firma** al leer, así que un ZIP manipulado se detecta. Es más débil
  frente a ataques de fuerza bruta que .mdr por esas pocas iteraciones: usa contraseñas largas.
- Al extraer, las rutas se limpian: `..`, rutas absolutas, nombres reservados de Windows y caracteres no válidos se neutralizan
  (protección contra «zip slip»). Los enlaces simbólicos no se siguen al comprimir.
- Cada fichero lleva su suma de control y se verifica al extraer y al comprobar.

## Cómo está hecho

```
src/Medrar.Core     Motor (sin interfaz): formatos, cifrado, recuperación, volúmenes, autoextraíbles, búsqueda...
src/Medrar          La aplicación de escritorio (Windows Forms, todos los iconos dibujados por código)
src/Medrar.Cli      mdr.exe, línea de comandos
src/Medrar.Sfx      Módulo autoextraíble
installer/          Script del instalador (Inno Setup)
tests/Medrar.Tests  102 pruebas automáticas del motor
docs/FORMATO-MDR.md Especificación del formato .mdr
docs/ANALISIS.md    Qué se averiguó de WinRAR con ingeniería inversa y cómo se usó
```

### Compilar y probar

Requisitos: [.NET SDK 10](https://dotnet.microsoft.com/download).

```
powershell -ExecutionPolicy Bypass -File build.ps1            compila y deja todo en dist\Medrar
powershell -ExecutionPolicy Bypass -File build.ps1 -Test -Zip -Installer   pruebas + .zip + instalador (necesita Inno Setup)
dotnet test tests\Medrar.Tests                                solo las pruebas
```

Algunas pruebas (leer RAR) necesitan WinRAR instalado para crear los RAR de prueba; si no está, esas pruebas **terminan sin
comprobar nada** (no se marcan como omitidas).

### Publicar una versión nueva

1. Cambia `<Version>` en `src/Medrar/Medrar.csproj` (y en los otros `.csproj` si quieres que coincidan).
2. `git commit`, `git tag v1.1.0` y `git push origin main --tags`.
3. La acción de GitHub (`.github/workflows/release.yml`) compila, ejecuta las pruebas y crea la *release* con el instalador y el `.zip`.

Bibliotecas externas: **SharpCompress** (MIT) y **System.IO.Hashing** (MIT) — ver [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
