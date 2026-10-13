# Verificación del asistente 1.5.9

Informe local de esta candidata; no acredita publicación ni aceptación completa
en VM. El procedimiento vigente está en la [guía del asistente](../ASISTENTE-EQUIPOS.md).

Revisión del 12 de octubre de 2026 sobre la base de 1.5.8. La corrección de
`System`/`machine` de 1.5.8 se conserva. El incidente anterior no sirve como
medición del rendimiento de la versión corregida.

## Cambios y efecto esperado

| Cambio | Efecto verificable | Cuándo reduce la espera |
| --- | --- | --- |
| Reutilizar el módulo oficial 1.12.440 | Si se puede importar, no se consulta NuGet ni PSGallery; se mantiene la reparación a 1.29.380 y la activación posterior | WinGet necesita reparación y el módulo ya está disponible |
| Compartir el fallo de transporte dentro de una ejecución | Diez apps con HTTP 429 persistente hacen tres peticiones en total, frente a treinta con el comportamiento anterior | GitHub limita consultas, no responde o falla la conexión |
| Cancelación hasta COM y HTTP | El timeout cancela la consulta subyacente; una respuesta tardía solo modifica su copia | Consulta lenta, cancelación o respuesta que ignora el token |
| Tiempos locales por fase | Se puede distinguir preparación, catálogo, comprobación, instalación y verificación | Permite localizar el siguiente cuello de botella real |

No se añade una caché global de indisponibilidad. Cada trabajador nuevo vuelve
a consultar el repositorio. El cliente HTTP puede reutilizar conexiones, pero
el estado de fallo pertenece al motor de esa ejecución. Un 404 o un manifiesto
incompatible no impiden comprobar otra aplicación. Se mantienen tres intentos,
45 segundos por petición y el respeto de `Retry-After`; un plazo superior a un
minuto termina la consulta sin esperar ni reintentar antes de lo permitido.

Un fallo de red no selecciona silenciosamente una versión anterior. El pack
parcial solo instala aplicaciones comprobadas. La instalación nativa sigue
siendo secuencial, con verificación antes de pasar a la siguiente. La reparación
oficial tampoco se mata al cancelar: se espera a su salida para no interrumpir
la instalación de dependencias.

Un timeout de comprobación no introduce una marca nueva de instalador activo.
Las marcas anteriores del mismo arranque y las instalaciones activas detectadas
por una comprobación terminada siguen bloqueando la cola. Si una actualización
adicional del catálogo se cancela, no se reutiliza ese catálogo como si se
hubiera actualizado correctamente para las apps siguientes.

## Diagnósticos

Los trabajadores nuevo y legacy escriben `timings.jsonl` en la carpeta de logs
de esa ejecución bajo `%ProgramData%\ClinicaPC\Setup`. Cada línea contiene
solo `atUtc`, `phase`, `application`, `durationMs` y `result`. No registra
credenciales, cuerpos o cabeceras HTTP, ni textos de excepciones.

Las fases son `Bootstrap`, `CatalogRefresh`, `Preflight`, `Install` y
`Verification`. `Install` incluye su verificación; el registro separado de
`Verification` permite conocer ese coste, por lo que no deben sumarse ambos
como si fueran fases consecutivas. Los resultados normales incluyen `Pending`,
`AlreadyInstalled`, `Succeeded` y `VerificationPending`; los errores y las
cancelaciones se registran como `Failed` y `Cancelled`. Si no se puede escribir
el diagnóstico, se conserva el resultado de la operación.

## Verificación automatizada

SDK usado: `C:\Users\zits\.dotnet\dotnet.exe`, .NET 10.0.100.

```powershell
& 'C:\Users\zits\.dotnet\dotnet.exe' build KioskClinicaPC.sln --no-restore -m:1 --disable-build-servers -p:UseSharedCompilation=false -nologo -clp:ErrorsOnly
& 'C:\Users\zits\.dotnet\dotnet.exe' test KioskClinicaPC.sln --no-restore -m:1 --disable-build-servers -p:UseSharedCompilation=false -nologo --verbosity minimal
```

La solución compila. Pasan 191 pruebas del cliente y 306 del servidor. En el
entorno restringido, seis pruebas existentes de named pipes del asistente fallan
al conectar bajo las ACL de Windows. Al repetir la suite con los permisos
normales de Windows pasan las 76 pruebas, sin cambiar el código de esos pipes:

```powershell
& 'C:\Users\zits\.dotnet\dotnet.exe' test tests/Kiosk.EquipmentSetup.Tests/Kiosk.EquipmentSetup.Tests.csproj --no-build --no-restore -m:1 --disable-build-servers -nologo --verbosity minimal
```

Total: 573 pruebas superadas. Las nuevas pruebas cubren módulo existente,
ausente/no importable, proveedor existente/ausente, fallo de importación tras
instalar sin bucles, reparación fallida sin reinstalación y cancelación antes
y durante el proceso. El script PowerShell real se ejecuta con funciones falsas:
no instala ni repara nada en el PC de desarrollo.

Se comprueban fallos HTTP 408/429/503, transporte y timeout compartidos, reinicio
del estado en una ejecución nueva, errores particulares 404/403/incompatibilidad,
`Retry-After` relativo y absoluto, cancelación, consultas no cooperativas,
estados guardados sin mutaciones tardías, actualización de catálogo cancelada
y las protecciones nativas y políticas de versión/reanudación existentes.

## Comprobación real de solo lectura

Con el trabajador 1.5.9 empaquetado, y los permisos normales de Windows,
`--diagnose-winget-json` devuelve `wingetActivated: true` (254 ms, incluyendo
el arranque del proceso). `inspect-installed-json IZArc.IZArc 4.6` devuelve
`installedScope: System`, `versionComparison: Equal`, `verified: true` y
`nativeInstallActive: false` (1.886 ms). Esto verifica las consultas COM que
ahora reciben el token, conservando la corrección de 1.5.8.

`audit-last-run-json` comprueba la selección guardada de IZArc, Chrome, VLC y
K-Lite en 1.782 ms, con `preflightReady: true` y `blockedByNativeState: false`.
IZArc ya está verificada y las otras tres apps pasan la comprobación. El hash
SHA-256 de `last-run.json` permanece idéntico tras ambos diagnósticos.
Esta auditoría usa las versiones del snapshot guardado: no prepara WinGet,
no actualiza el catálogo, no resuelve versiones nuevas ni instala aplicaciones.
Estos tiempos describen una ejecución de lectura en este PC; no son una
comparación antes/después ni un benchmark de una instalación completa.

## Artefactos locales

Generados con `build-equipment-setup.ps1 -Version 1.5.9`, sin `-Publish`, con
`https://setup.invalid` y claves ficticias. El asistente y el trabajador son
1.5.9; Kiosk conserva 1.2.2. El build usa los cambios de este árbol de trabajo;
`sourceCommit` identifica su commit base, no una release con estos cambios
guardados. No se han publicado ni activado en el panel.

| Archivo | Bytes | MiB |
| --- | ---: | ---: |
| `installer/Output/Setup-EquipoClinicaPC-1.5.9.exe` | 64.694.951 | 61,70 |
| `installer/Output/Setup-EquipoClinicaPC-1.5.9-Completo.exe` | 265.591.459 | 253,29 |
| `equipment-build/resources/worker.zip` | 43.017.442 | 41,02 |

Ambas ediciones pasan el diagnóstico empaquetado: Online no contiene los
binarios opcionales y Completo verifica los dos recursos incrustados. El
trabajador incluye DLL nativa y WinMD y no depende de WPF. Se cumplen los límites
de tamaño del build y los SHA-256 de ambos EXE coinciden con el bundle generado.
El log está en `installer/Output/build-1.5.9-local.log`.

La consulta de vulnerabilidades NuGet no estuvo disponible en el entorno
restringido (NU1900). Los paquetes necesarios estaban disponibles y no impidió
compilar ni ejecutar las pruebas.

## Límites

No se ha medido una instalación completa en una VM Windows limpia ni se ha
reparado WinGet o instalado aplicaciones reales en el PC de desarrollo. Queda
pendiente probar Windows 10 22H2 y Windows 11 x64 con módulo presente, ausente
y dañado, y con pérdida de red durante la comprobación. Los tests no sustituyen
esa aceptación ni permiten prometer un porcentaje de mejora en una red sana.

No se implementa descarga paralela de instaladores: la API pública usada inicia
la instalación al encolarla y no ofrece una separación suficiente para mantener
la verificación y la parada ante resultados ambiguos de esta cola. Tampoco se
eliminan validaciones de integridad, ámbito, versión o revisión del catálogo.
