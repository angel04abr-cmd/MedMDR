# Formato .mdr (versión 1)

Especificación del formato de archivo propio de MedMDR. Está pensada para que cualquier programa pueda leerlo
(no hay código oculto ni algoritmos propietarios: Brotli, Deflate, AES-GCM y PBKDF2 son estándar).
Todos los números van en **little-endian**. «CRC32» es el CRC-32 habitual (el de ZIP).

## Estructura general

```
┌────────────────────────────┐ posición 0 del archivo
│ Cabecera                   │
├────────────────────────────┤
│ Datos                      │  ficheros comprimidos (y cifrados, si hay contraseña)
├────────────────────────────┤
│ Directorio                 │  lista de ficheros (JSON + Brotli, cifrado si se piden nombres cifrados)
├────────────────────────────┤
│ Registro de recuperación   │  opcional
├────────────────────────────┤
│ Pie (48 bytes)             │  siempre al final
└────────────────────────────┘
```

Un autoextraíble es un `.exe` con el archivo pegado detrás; se localiza con el pie del propio archivo (ver «Pie»).

## Cabecera

| Bytes | Contenido |
|---|---|
| 8 | Firma `4D 45 44 52 41 52 1A 01` («MEDMDR» + 0x1A + versión 1) |
| 4 | Longitud *N* del JSON |
| N | JSON UTF-8 de la cabecera |
| 4 | CRC32 del JSON |

Campos del JSON: `V` (versión, 1), `App`, `Created` (ticks UTC de .NET), `Solid` (bool), `Encrypted`, `EncryptedNames`,
`Locked`, `Method` (0 = almacenado, 1 = Deflate, 2 = Brotli, 3 = LZMA), y si hay contraseña: `Salt` (Base64, 16 bytes), `Iter`
(iteraciones PBKDF2), `Check` (Base64, 32 bytes).

Los datos empiezan justo después de la cabecera: `8 + 4 + N + 4`.

## Cifrado

- Clave: `PBKDF2-HMAC-SHA256(contraseña UTF-8 normalizada NFC, Salt, Iter, 64 bytes)`.
  Los primeros 32 bytes son la **clave AES-256**; los últimos 32 son `Check` (sirve para saber si la contraseña es correcta).
  MedMDR usa `Iter` = 200 000.
- Cifrado por **bloques de 64 KB** con AES-256-GCM (etiqueta de 16 bytes). Cada bloque de texto claro de hasta 65 536
  bytes se guarda como `[texto cifrado][etiqueta 16 B]`; la longitud total cifrada de *L* bytes de texto claro es
  `L + 16 · máx(1, ⌈L / 65536⌉)`.
- Nonce (12 bytes): `propósito (u32)` + `índice del bloque (u64)`. Propósito: 1 = datos, 2 = directorio.
- Datos asociados (13 bytes): `propósito (u32)` + `índice (u64)` + `1 byte` (1 si es el último bloque, 0 si no).
  Así un archivo truncado o con bloques reordenados falla la autenticación.

## Datos

- **No sólido**: cada fichero se comprime por separado con el método indicado en su entrada del directorio (`M`), uno detrás de otro.
  La entrada da su posición `O` (desplazamiento dentro del flujo de datos *sin cifrar*) y su tamaño comprimido `Z`.
- **Sólido** (`Solid` = true): los ficheros se concatenan y se comprimen como un solo flujo con el método de la cabecera.
  `O` es el desplazamiento del fichero dentro del flujo **descomprimido**.
- Brotli con ventana de hasta 2²⁴; Deflate en bruto (RFC 1951, sin cabecera zlib).
- **LZMA (método 3, nivel «Ultra»):** el flujo empieza con 5 bytes de propiedades LZMA (byte de lc/lp/pb + diccionario de 32 bits
  little-endian, como en el formato .lzma/7z) y sigue el flujo LZMA, que termina con su marca de fin (no se guarda el tamaño).
  Un archivo puede mezclar métodos: en no sólido cada entrada lleva el suyo (`M`); en sólido todo el bloque usa el de la cabecera.
  Las versiones de MedMDR anteriores a la 1.4 no entienden el método 3 y lo dicen con un error («método de compresión desconocido»).
- Con contraseña, todo el flujo de datos está cifrado con el esquema anterior; las posiciones `O` se refieren al flujo
  de datos ya descifrado (antes de añadir las etiquetas).

## Directorio

Brotli de un JSON UTF-8:

```json
{ "Comment": "texto opcional", "PayloadLength": 123456,
  "Entries": [ { "P": "carpeta/fichero.txt", "D": false, "S": 1000, "Z": 400, "O": 0, "M": 2, "C": 123456789, "T": 639000000000000000 } ] }
```

`P` ruta con `/`; `D` es carpeta; `S` tamaño original; `Z` tamaño comprimido (en modo sólido es una estimación proporcional);
`O` posición; `M` método; `C` CRC32 del contenido; `T` fecha de modificación (ticks UTC de .NET).
`PayloadLength` es la longitud (sin cifrar) del flujo de datos.

Con «nombres cifrados» el bloque Brotli va cifrado (propósito 2) y su longitud física es la del texto cifrado.

## Registro de recuperación (opcional)

Códigos Reed-Solomon sobre GF(2⁸) (polinomio 0x11D) con bloques de 16 384 bytes. Protege todo lo anterior (cabecera,
datos y directorio). Los datos se dividen en grupos de *G* bloques (hasta 200); cada grupo lleva *M* bloques de paridad
(el porcentaje pedido, mínimo 2). Se pueden reconstruir hasta *M* bloques dañados o perdidos de cada grupo.

```
8 bytes   firma "MDRRC\0\2\0"
u32       tamaño de bloque (16384)
u32       G (bloques de datos por grupo)
u32       porcentaje de recuperación
u32       número total de bloques de datos
u64       longitud protegida
u32[n]    CRC32 de cada bloque de datos (el último bloque se rellena con ceros hasta 16384)
[bloques de paridad: por cada grupo, M_g bloques de 16384 bytes], M_g = máx(mín(k,2), ⌈k·porcentaje/100⌉), k = bloques del grupo
u32[m]    CRC32 de cada bloque de paridad
u32       CRC32 de todo el registro anterior
```

Coeficiente de la fila de paridad *i* y el bloque de datos *j* del grupo: `1 / ((G + i) XOR j)` en GF(2⁸) (matriz de Cauchy).
`paridad_i = Σ_j coef(i, j) · dato_j`. Para otros formatos (ZIP, 7Z…) el mismo registro se guarda en un fichero aparte
llamado `nombre.ext.mdrrec`, que protege el archivo entero.

## Pie (48 bytes, al final del archivo)

| Bytes | Contenido |
|---|---|
| 8 | Firma `4D 44 52 45 4E 44 00 01` |
| 8 | Longitud total del archivo (de la cabecera al pie, incluido) |
| 8 | Posición del directorio (desde el inicio del archivo) |
| 4 | Longitud física del directorio |
| 8 | Posición del registro de recuperación (0 si no hay) |
| 4 | Longitud del registro (0 si no hay) |
| 4 | CRC32 de los 40 bytes anteriores |
| 4 | Relleno |

El inicio del archivo es `tamaño del fichero − longitud total`. Así se localiza aunque vaya detrás de un ejecutable.

## Autoextraíbles

`[ejecutable][archivo (cualquier formato)][pie de 32 bytes]`. El pie: `u64` inicio del archivo, `u64` longitud, `u32` formato
(0 = mdr, 1 = zip, 2 = tar, 3 = tar.gz, 4 = gz, 5 = 7z), `u32` 0, y la firma `4D 44 52 53 46 58 00 01`.

## Lector mínimo (pseudocódigo)

```
leer los últimos 48 bytes → pie; inicio = tamaño − pie.longitudTotal
leer la cabecera en inicio; si Encrypted: derivar claves y comprobar Check
leer el directorio (descifrar si EncryptedNames, luego Brotli → JSON)
para cada entrada: posicionarse en datos+O (o descomprimir el flujo sólido hasta O), descomprimir S bytes, comprobar CRC32
```
