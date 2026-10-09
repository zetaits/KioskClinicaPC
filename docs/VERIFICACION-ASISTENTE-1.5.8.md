# Verificación del asistente 1.5.8

## Causa comprobada

La ejecución del 9 de octubre de 2026 a las 20:36 instaló IZArc 4.6, pero no la
dio por verificada y detuvo Chrome, VLC y K-Lite. El registro HKLM de Windows
contiene IZArc 4.6. Una consulta directa a la API COM, antes de corregir la
verificación, devolvió:

```json
{"wingetId":"IZArc.IZArc","installedVersion":"4.6","installedScope":"System","versionComparison":"Equal","verified":false,"nativeInstallActive":false}
```

El motor comparaba InstalledScope con `machine`. El código oficial de Microsoft
explica que la API devuelve `System` en lugar de `Machine`:
[PackageVersionInfo.cpp, versión 1.29.380](https://github.com/microsoft/winget-cli/blob/v1.29.380/src/Microsoft.Management.Deployment/PackageVersionInfo.cpp#L71-L87).
No se trataba de un instalador activo ni de una demora de actualización del catálogo.

## Cambio y comprobación con datos reales

La política InstalledPackage usa `System`/`User` para el ámbito COM y el comparador
de WinGet para ordenar versiones. Es la misma política en comprobación previa,
omisión antes de instalar y verificación posterior. No cambia la validación
`machine` de los manifiestos YAML ni permite un ámbito/versión desconocido.

Tras corregirla, la consulta real de IZArc devuelve `verified: true` y el comando
`inspect IZArc.IZArc 4.6` responde «Ya instalada y verificada (todo el equipo)».
La consulta real de LibreOffice detecta 7.6.2.1 con ámbito `System` y comparación
`Lesser` contra 26.8.1.1: requiere actualizarse y no debe confundirse con una
instalación por usuario.

Se reprodujeron las comprobaciones de la última selección guardada con
`audit-last-run-json`, usando la API COM real y las políticas reales de reanudación
y ejecución. El resultado fue:

| Aplicación | Resultado |
| --- | --- |
| IZArc 4.6 | Ya instalada y verificada; protección anterior despejada |
| Chrome 155.0.8059.40 | Comprobación previa correcta |
| VLC 3.0.24 | Comprobación previa correcta |
| K-Lite Mega 20.0.5 | Comprobación previa correcta |

El diagnóstico devolvió `preflightReady: true` y `blockedByNativeState: false`.
SHA-256 de `last-run.json` era idéntico antes y después. El backend de auditoría
rechaza cualquier llamada a instalar y su callback de persistencia rechaza
cualquier escritura. No se ejecutó Kiosk.

## Reproducir el diagnóstico

Conservar DLL nativa y WinMD junto al EXE del trabajador. Tras publicar el
trabajador localmente, ejecutar en PowerShell:

```powershell
& .\equipment-build\verification-worker\KioskSetupHelper.exe inspect-installed-json IZArc.IZArc 4.6
& .\equipment-build\verification-worker\KioskSetupHelper.exe inspect IZArc.IZArc 4.6
& .\equipment-build\verification-worker\KioskSetupHelper.exe audit-last-run-json
```

La auditoría conserva las versiones del último snapshot; no descarga el catálogo
autorizado del panel ni sustituye la validación de revisión del asistente.
Consulta manifiestos públicos y no prepara/repara WinGet, instala aplicaciones
ni modifica `last-run.json`.

## Cobertura y límites

Las pruebas de política cubren la evidencia nativa de IZArc/LibreOffice, versión
igual/superior/anterior, ámbito User, ámbito ausente, ámbito de manifiesto usado
por error y versión no comparable. Las pruebas de ejecución conservan parada
ante instalador activo, cancelación/resultado ambiguo y continuación parcial
ante rechazos ordinarios.

La prueba en el PC real verifica detección y reanudación de la instalación fallida.
La instalación nueva de Chrome, VLC y K-Lite con sus fabricantes debe verificarse
con el asistente publicado. Los tests y la auditoría de lectura no equivalen a
haber instalado esas tres aplicaciones ni a una validación en VMs limpias.
