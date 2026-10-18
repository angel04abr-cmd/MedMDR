# Componentes de terceros

Medrar usa los siguientes componentes libres. Todos tienen licencia MIT, que exige conservar este aviso al redistribuirlos.

| Componente | Para qué se usa | Copyright |
|---|---|---|
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) 0.50.5 | Leer RAR, 7Z, BZ2, XZ, LZ, CAB, ISO… y crear 7Z | © 2025 Adam Hathcock |
| [System.IO.Hashing](https://www.nuget.org/packages/System.IO.Hashing) 10.0.0 | Suma de control CRC32 | © Microsoft Corporation |
| [System.Security.Cryptography.ProtectedData](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData) 10.0.0 | Guardar cifrado el token de las actualizaciones (DPAPI) | © Microsoft Corporation |
| [.NET](https://dotnet.microsoft.com) 10 (Medrar.Sfx.Full.exe lo incluye entero) | Plataforma de ejecución | © .NET Foundation y colaboradores |

Dependencias del propio .NET (se distribuyen dentro de `Medrar.Sfx.Full.exe`): consulta
<https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT>.

## Texto de la licencia MIT

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## WinRAR

Medrar **no contiene código, imágenes, iconos ni textos de WinRAR** ni de RARLAB. Observar qué funciones ofrece WinRAR
sirvió solo para decidir qué funciones tendría Medrar. Medrar lee archivos RAR con SharpCompress y
**no crea** archivos RAR. «WinRAR» y «RAR» son marcas de sus propietarios.
