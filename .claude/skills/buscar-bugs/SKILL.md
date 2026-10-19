---
name: buscar-bugs
description: Busca errores en MedMDR de verdad, no solo leyendo código — compila, ejecuta las pruebas, abre la aplicación con sus modos de prueba y revisa el código con agentes especializados. Úsala cuando el usuario pida revisar, probar o buscar bugs en la aplicación, antes de publicar una versión, o después de cambiar la interfaz, los formatos o el instalador.
argument-hint: "[área: interfaz | formatos | instalador | todo]"
---

# Buscar bugs en MedMDR

Objetivo: encontrar fallos **reproduciéndolos**. Un fallo no cuenta hasta que se ha visto fallar (código de salida, captura, mensaje) y, tras
arreglarlo, se ha visto pasar. El área pedida es `$ARGUMENTS` (si está vacía, `todo`).

## 1. Línea base (siempre, en este orden)

1. `git status` — anota qué hay sin guardar para no mezclar tus cambios con los del usuario.
2. `dotnet test tests\MedMDR.Tests -c Release` — todas deben pasar.
3. `powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1` — abre la aplicación real en ~30 escenarios (borrar, comprimir,
   extraer, convertir, buscar, asistente, configuración, menú de Windows…) y hace la ida y vuelta de cada formato. Vigila código de
   salida, cuelgues, fallos anotados por Windows y `error.log`. **Si algo falla, eso es el primer bug.** (`-Only texto` repite uno.)

## 2. Reparto en paralelo (agentes del proyecto, en `.claude/agents/`)

Lanza los que correspondan al área, **a la vez** y con un encargo concreto cada uno:

| Agente | Para qué | Cuándo |
|---|---|---|
| `medmdr-revisor` | Lee el código buscando las clases de fallo conocidas (ver abajo) en lo que ha cambiado | interfaz, formatos, instalador, antes de publicar |
| `medmdr-probador` | Usa la aplicación con los modos `--auto/--accept/--snapshot`, mira las capturas y busca fallos nuevos fuera del guion | interfaz |
| `medmdr-formatos` | Estresa los formatos (nombres raros, vacíos, claves, volúmenes, autoextraíbles, muchos ficheros) y escribe pruebas | formatos, motor |

No les pases conclusiones: pásales qué mirar y qué comando reproduce. Sus informes son pistas, no verdades: **comprueba cada hallazgo tú**
antes de arreglarlo.

## 3. Clases de fallo que ya han pasado aquí (míralas primero)

- **Llamadas nativas mal declaradas** (P/Invoke): una estructura con `Pack = 1` en 64 bits o un `bool` donde Windows espera 4 bytes
  cerraba el programa con `0xC0000005` al borrar (`Services/Recycle.cs`). No salta en las pruebas de xunit, solo ejecutando.
- **Ayudantes usados sin su condición**: `Extractor.UniqueName` devuelve siempre «nombre (1)»; hay que llamarlo solo si el fichero ya existe.
- **Coste por fichero**: crear un codificador Brotli por fichero costaba ~10 ms (10 000 ficheros = 2 min). Mira bucles que crean objetos pesados.
- **Ventanas sin dueño visible**: `Owner` de una ventana que aún no se ha mostrado lanza excepción (órdenes del menú de Windows). Todas las
  ventanas usan `Ask/Show(owner…)` con la comprobación `owner.IsVisible`.
- **Cuadros que bloquean la automatización**: un `MessageBox` sin respuesta deja el proceso esperando; `--yes`/`--text`/`--accept` los responden.
- **Rutas con espacios** pasadas como argumentos sin comillas (`Start-Process -ArgumentList`): el usuario se llama «Angel Blas».
- **Listas grandes**: cargar 100 000 filas elemento a elemento; probar con carpetas y archivos enormes.
- **Estado compartido**: pruebas que escriben la configuración real. Las pruebas usan `MEDMDR_DATA_DIR` para aislarse.
- **Nombres antiguos**: «Medrar» solo debe quedar en lo que se conserva a propósito (ver `docs/DESARROLLO.md`).

## 4. Al arreglar

- Escribe antes la prueba que falla: de xunit si es del motor (`tests/MedMDR.Tests`), o un escenario nuevo en `tools/Smoke-Test.ps1` si es de la
  ventana. Comprueba que **falla sin el arreglo** y pasa con él.
- Cambios pequeños y específicos; no reordenes código que no sea el del fallo.
- Respeta los finales de línea y la codificación de cada fichero (UTF-8; el repositorio usa CRLF en Windows).

## 5. Límites y cuidado

- No publiques, no etiquetes y no toques GitHub: eso lo decide el usuario.
- No cambies la configuración real del usuario ni borres sus ficheros: los escenarios trabajan en una carpeta temporal. El único rastro de
  `Smoke-Test.ps1` es un fichero `medmdr-smoke-borrar-*.txt` en la Papelera.
- Si dejas procesos `MedMDR.exe` colgados, ciérralos (`Get-Process MedMDR | Stop-Process`).
- Lo que **no** se puede comprobar así (di siempre que no lo has probado): arrastrar con el ratón al Explorador, menús desplegables abiertos,
  escalas de pantalla del 150 % o 200 %, alto contraste, y el aspecto exacto de un cuadro (solo se ve en la captura).

## 6. Informe final (en español sencillo)

Qué ejecutaste, qué falló (con el código o mensaje exacto), qué arreglaste y cómo lo comprobaste, qué quedó sin probar y por qué.
