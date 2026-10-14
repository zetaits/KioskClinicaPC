# Desfase del MSI de Chrome en el pack

Fecha: 14 de octubre de 2026.

Estado: corrección implementada y validada localmente en código. La instalación
completa con el nuevo trabajador y la publicación de una nueva versión del
asistente siguen pendientes. El EXE 1.5.12 ya distribuido no incorpora el cambio.
El builder y el workflow quedan preparados con 1.5.13 como próxima versión.

## Evidencia del incidente

En una ejecución local que seleccionó LibreOffice y Chrome, WinGet actualizó su
catálogo y resolvió `Google.Chrome` 155.0.8059.40. El MSI servido desde
`https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi`
declaraba `Google Chrome` 155.0.8059.80 y tenía firma Authenticode válida de
Google LLC. WinGet esperaba SHA-256
`32BA438DEA18F0FED1EC621BFB274A2669ADEC67BE2AA91D96B3CC6C64779276`;
el archivo descargado tenía
`2734DC5577DCBAB49BE037E5A074AEF9948FACA14453B52DEFB01224C196A3F5`.
Devolvió `DownloadError`, código extendido `0x8A150011` (hash diferente), sin
iniciar el MSI de Chrome. El archivo descargado tenía 169.848.832 bytes.

LibreOffice 26.8.1.1 se instaló y verificó. Su MSI devolvió 3010 y Windows
tenía un reinicio pendiente. El resultado parcial del pack fue salida 2.
Los datos proceden de `last-run.json`, `*.native-result.jsonl`, `timings.jsonl`
y el diagnóstico `WinGetCOM-2026-10-14-11-57-01.261.log` del equipo de prueba.
No se dispone de los registros del equipo del trabajador; su captura es compatible
con el mismo fallo, pero no lo demuestra.

## Corrección

Solo para `Google.Chrome` y solo ante `DownloadError` con `0x8A150011`, el
trabajador aplaza la instalación hasta que terminen las demás operaciones.
Descarga el MSI empresarial x64 desde la URL oficial de Google, limita el tamaño,
exige HTTPS en `dl.google.com`, valida firma y firmante Google LLC, consulta
`ProductName` y `ProductVersion` del MSI y exige una versión igual o superior
a la resuelta. Lo ejecuta con `msiexec /qn /norestart` y vuelve a comprobar la
versión y el ámbito mediante WinGet. Si la instalación nativa queda incierta,
conserva la protección contra reintentos del mismo arranque. Los demás errores
y aplicaciones conservan la política WinGet normal.

Además, un `DownloadError` confirmado antes de iniciar un instalador permite un
solo reintento WinGet, salvo cuando el hash no coincide. Ese caso necesita una
vía específica de confianza para el editor o una versión corregida del catálogo.
Si persiste el error, el resultado muestra el HRESULT; no se omite el hash ni se
instala una versión distinta sin la comprobación previa correspondiente.

Guías vigentes: [Asistente](../ASISTENTE-EQUIPOS.md) y [Pack](../PACK-WINGET.md).

## Validación local y pendiente

- Build de la solución: correcto, cero errores y advertencias.
- Pruebas `PackPipelineTests`, `PackExecutionTests` y `PackResumePolicyTests`:
  44 correctas, incluida la espera del trabajo nativo anterior, la verificación
  final del aplazado y la protección ante resultado incierto.
- Batería Release: 191 pruebas del cliente y 360 del servidor correctas. Las 82
  pruebas del asistente también pasaron al ejecutarlas fuera del aislamiento;
  seis pruebas de procesos hijos y canales locales fallaron solo en el entorno
  aislado, donde el trabajador no pudo conectar.
- El validador nuevo aceptó, en solo lectura, el MSI real descargado durante el
  fallo y devolvió `155.0.8059.80`.
- Publicación local Release de `Kiosk.PackWorker` correcta en
  `installer/Output/chrome-fallback-worker-local/`. El EXE resultante devolvió
  `wingetActivated: true` al ejecutar `--diagnose-winget-json` sin instalar nada.
- Pendiente: ejecución integral de un EXE nuevo en Windows 10/11, incluyendo
  descarga, instalación de Chrome, verificación, cancelación, reanudación y
  reintento de un fallo transitorio de otra aplicación.
- Pendiente: generar, publicar y activar un asistente con nueva versión. Ninguna
  prueba local cambia la descarga activa del panel.
