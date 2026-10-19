# MedMDR

Gestor de archivos comprimidos para Windows (C#, .NET 10, WPF). Formato propio `.mdr`; lee RAR y 7Z con SharpCompress, no crea RAR.
Todo lo que ve el usuario está en español; habla con el usuario en español sencillo, con pruebas y límites explícitos.

- Guía de desarrollo (proyectos, modos de prueba, publicación, nombres antiguos «Medrar» que se conservan): `docs/DESARROLLO.md`. Léela antes de tocar el código.
- Antes de dar algo por hecho: `dotnet test tests\MedMDR.Tests` y `powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1` (abre la aplicación de verdad).
- Para buscar errores usa la skill `/buscar-bugs` y los agentes `medmdr-revisor`, `medmdr-probador` y `medmdr-formatos` (`.claude/`).
- Reproduce cada fallo antes de arreglarlo y deja una prueba (xunit o un escenario de `tools/Smoke-Test.ps1`) que falle sin el arreglo.
- No publiques versiones ni toques GitHub sin que el usuario lo pida: publicar (etiqueta `vX.Y.Z`) actualiza solas las copias instaladas.
- Nunca borres ni cambies datos reales del usuario; las pruebas usan carpetas temporales y `MEDMDR_DATA_DIR`. Nunca subas ni imprimas tokens.
- Sin código, recursos ni textos de otros gestores de archivos. El repositorio se llama **MedMDR**; no crear otro llamado «Medrar».
