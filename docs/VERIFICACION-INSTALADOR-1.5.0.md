# Verificación local del asistente 1.5.0

Fecha: 8 de octubre de 2026. SDK utilizado: `C:\Users\zits\.dotnet\dotnet.exe`
(.NET 10). Artefactos **ficticios**, con `https://setup.invalid` y claves de prueba:
no se han desplegado ni publicado en producción ni ejecutado Kiosk o instaladores
de aplicaciones. El campo SourceCommit señala el HEAD base del trabajo local;
la publicación definitiva debe generarse desde el commit probado y guardado.

| Artefacto | Bytes | MiB | Objetivo |
| --- | ---: | ---: | --- |
| Online 1.5.0 | 64.687.799 | 61,69 | Menos de 100 MB: cumple |
| Completo para USB 1.5.0 | 265.517.543 | 253,22 | Menos de los 366.646.608 bytes anteriores: cumple |
| Trabajador sin WPF 1.5.0, worker.zip | 42.941.299 | 40,95 | Menos de 66,6 MiB: cumple |
| Kiosk fijado 1.2.0 | 157.506.949 | 150,21 | Mismo componente en ambas ediciones |

Los últimos diagnósticos `--diagnose-json` finalizaron en 1.235 ms (online)
y 4.344 ms (completo). El trabajador terminó su diagnóstico en 325 ms.
Son tiempos del proceso de diagnóstico, incluyendo sus comprobaciones;
no son una medida de apertura de la interfaz ni de instalación en una VM.

SHA-256 de los artefactos locales finales:

- Online: `f2336ee76c95c8a33da0497704f1d28dafd1738d32bdcad1887bd2d39d5d302f`.
- Completo: `82bb72f334ed22f8ad6e628a26e8ed0cf0adb8b69a3d97e623ffaa2ccfa1cb91`.
- Trabajador: `cf300ab45655a3dfb0e33a25d76abbc5a4fbad7fdc89628a8029674d5caf3b8f`.
- Kiosk: `dd4ffa4a6dc328eca2c583f76d0062f701a449579643e2151b0710cdaa45f6cc`.

Verificaciones realizadas:

- Compilación de la solución con SDK local y MSBuild en un proceso (`-m:1`).
- Suite Release: **478 pruebas correctas, ninguna fallida ni omitida**:
  182 de cliente/shared, 253 de servidor y 43 de EquipmentSetup.
- Diagnósticos de ambos EXE comprimidos: online valida configuración/manifiesto y
  ausencia de binarios, sin descargar; completo valida hashes, tamaños y trabajador fijado.
- Trabajador autónomo con WinMD físico y sin framework Desktop ni PresentationFramework.
  Argumentos y rutas `export-index` del helper y trabajador comprobados con rechazo
  de un repositorio inexistente antes del bootstrap. Las pruebas del lector/exportador
  se conservan; no se ha exportado el catálogo oficial completo en esta sesión.
- Importación multipart de **los cuatro artefactos finales** en un TestServer aislado,
  con credenciales ficticias y almacenamiento local. Verificación de hashes/compatibilidad
  en disco, candidata sin activar automáticamente, activación y reapertura del puntero.
- Descarga real del worker.zip final desde ese TestServer con ETag/Range, reanudando
  128 KiB de parcial; promoción de caché y SHA-256 correctos. Corrupción posterior de
  caché detectada y sustituida antes de usarla. No se inició el trabajador instalador.
- Junction físico de Windows rechazado en un antecesor de la ruta de componentes.
- Caché, cuota, archivos activos, cancelación, límites de tiempo, errores HTTP,
  origen local de despliegue, ZIP inseguro, expansión y metadatos falsificados,
  compatibilidad, selección parcial, estado nativo y restauración cubiertos en pruebas.

La suite Release encontró una carrera existente entre el resultado del proceso y
el último callback del lector. `PackSession.Install` ahora espera a ambos lectores.
Una regresión mantiene bloqueado el callback aun después de salir el proceso y
comprueba que la operación no declara finalización antes de entregarlo. Los asistentes
se reempaquetaron con esta corrección, conservando el conjunto probado de componentes.

Queda pendiente la aceptación en VM Windows 10/11 sin .NET, UAC con otra cuenta,
WinGet ausente/bootstrap real, exportación oficial completa, reinicio/reanudación
e instalación USB de Kiosk sin conexión. No hay herramientas de VM disponibles en
esta sesión. El procedimiento está en [ASISTENTE-EQUIPOS.md](ASISTENTE-EQUIPOS.md).
La consulta de vulnerabilidades de NuGet devolvió NU1900 por falta de acceso a
api.nuget.org; no se presenta esa auditoría como superada.

Artefactos y registros locales no versionados: `installer/Output/`,
`equipment-build/`, `test-verification-release-final.log` y
`artifact-integration-verification.log` bajo `installer/Output/`.
