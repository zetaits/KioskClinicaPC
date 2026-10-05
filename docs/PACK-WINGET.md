# Pack de aplicaciones para ordenadores independientes

## Uso cotidiano

1. En `/instalador`, buscar una aplicación y pulsar **Añadir**. Su versión queda fijada automáticamente y se preselecciona. Se pueden retirar aplicaciones, cambiar orden y desmarcar su selección inicial.
2. **Actualizar todas las versiones** fija las versiones del último índice oficial; las aplicaciones no elegibles conservan su versión anterior y muestran un aviso. No se cambia el pack automáticamente al renovar el índice.
3. En el PC de destino, descargar y ejecutar el instalador interno, aceptar UAC, dejar **solo pack** o marcar también Kiosk, elegir aplicaciones e iniciar. El progreso, resultado y botón de reintento se muestran localmente.

No se registran equipos independientes en la flota, ni se envían nombres, estados o logs al panel. El cliente solo lee `/api/setup/v2/catalog` con la clave limitada del Setup. El snapshot de esa lectura se conserva durante la ejecución. La aplicación necesita Internet para WinGet y los manifiestos oficiales, no para informar al panel.

Solo admite paquetes del origen oficial `winget` con versión fija, ámbito machine y modo silent. MSStore, MSIX por usuario, portable, autenticación interactiva, ámbito desconocido y versiones instaladas desconocidas se rechazan. No se infieren parámetros ejecutando EXE arbitrarios. Para una aplicación no admitida hay que mejorar su manifiesto oficial o mantenerla fuera del pack; no se promete automatización universal de cualquier EXE.

## Activación en producción (no realizada por cambiar el código)

- Desplegar el servidor actualizado siguiendo `ACTUALIZAR-PANEL-VPS.txt`.
- Configurar `Kiosk__InitialSetupKey` y el secret GitHub **production** `KIOSK_INITIAL_SETUP_KEY` con el mismo secreto de 64 dígitos hexadecimales; no imprimirlo ni guardarlo en Git. Mantener `Kiosk__ReleasePublishKey` / `KIOSK_RELEASE_PUBLISH_KEY` existentes para importaciones CI.
- Ejecutar manualmente **Official WinGet catalogue** (`.github/workflows/winget-index.yml`) la primera vez. Luego se ejecuta diariamente. Windows exporta mediante la API COM oficial y contrasta los modos del repositorio `microsoft/winget-pkgs`. Linux solo lee el JSON local. Una exportación incompleta o una importación inválida no sustituye el índice anterior.
- Si falla la exportación, consultar el paso **Export structured catalogue** y el artifact **winget-index-diagnostics**: incluye stdout, stderr y un log con fases, recuentos y excepción completa con HRESULT. No registra variables de entorno ni claves. Tras actualizar el workflow, usar **Run workflow** sobre `master`; **Re-run jobs** de una ejecución antigua reutiliza su código anterior.
- Publicar una nueva versión mediante el workflow de release. `build-installer.ps1 -Publish` requiere la clave de Setup, genera el instalador interno y lo importa directamente por `/api/releases/setup`. **Nunca** lo sube a los assets públicos de GitHub: contiene credenciales de aprovisionamiento. El instalador Kiosk-only y sus updates públicos siguen separados.
- Comprobar `/instalador`: fecha reciente del índice, búsqueda, aplicaciones elegibles y descarga interna disponible. El pack comienza vacío: no se migran ni presuponen los cinco binarios antiguos.

Los archivos persistentes son `data/pack-applications.json`, `data/winget-index.json` y `setups/*.bundle.json` con su EXE. El catálogo anterior de binarios y sus APIs se conservan para instalaciones remotas y Setups antiguos; su página ya no configura el pack nuevo. El permiso de binarios sin firma sigue siendo una política del **agente remoto antiguo**, no una opción del pack WinGet.

El Setup descargado sigue siendo un único EXE. Internamente extrae el helper y `Microsoft.Management.Deployment.winmd` juntos en la carpeta temporal: el marshaler de Windows necesita esos metadatos junto al ejecutable. No quitar ese archivo al distribuir o probar el helper por separado. Se ha contrastado este requisito con [el diagnóstico del proyecto oficial de Microsoft](https://github.com/microsoft/windows-rs/issues/3352#issuecomment-2504588632).

## Seguridad y recuperación local

El Setup eleva una vez bajo un usuario interactivo, no SYSTEM. Si falta WinGet o es anterior a 1.29.380, prepara el módulo oficial Microsoft.WinGet.Client fijado en 1.12.440 y utiliza Repair-WinGetPackageManager para instalar WinGet 1.29.380. El SDK COM de compilación está fijado en esa misma versión. El catálogo acepta los acuerdos de origen y de paquete; no se ignoran hashes, no se fuerzan instalaciones, no se modifican argumentos del fabricante y se resuelven dependencias nativamente.

Se comprueba toda la selección antes de copiar Kiosk o instalar aplicaciones. Después se instala secuencialmente, se comparan versiones con WinGet y se verifica el registro de instalación **machine-wide**, no solo exit=0. Una app ya instalada con versión igual o superior se omite. Un fallo ordinario no impide intentar las demás; cualquier resultado sin verificar mantiene el pack incompleto.

Estado y logs: `%ProgramData%\ClinicaPC\Setup\last-run.json` y `logs/`. Estos datos no contienen claves de panel. Se excluyen ejecuciones simultáneas mediante un bloqueo de archivo. Antes de empezar, el asistente ofrece reanudar el último snapshot pendiente sin volver a consultar el panel; el motor vuelve a consultar el estado real, aunque antes hubiera marcado éxito. No se muestran diálogos de reanudación después de pulsar Instalar. No reinicia Windows automáticamente.

Una instalación nativa tiene un límite de 60 minutos. WinGet puede cancelar una descarga, pero no garantiza detener un instalador ya activo: en caso de cancelación/timeout se detiene toda la cola, se marca verificación pendiente y se exige verificación o reinicio manual antes de reintentar. No hay rollback ficticio ni reintentos ciegos del EXE. Las consultas de manifiestos y catálogo permiten hasta tres intentos ante problemas transitorios; errores de autorización no se reintentan.

Modo desatendido: ejecutar el Setup interno con `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Se carga explícitamente el catálogo y su preselección, sin depender de eventos de botones. Si falla la comprobación previa, se aborta antes de instalar; un pack incompleto devuelve exit code 2. Puede indicarse `/COMPONENTS="pack,kiosk"` para incluir Kiosk de forma explícita.

## Validación pendiente antes de producción

La compilación y los tests de catálogo/API/orquestación no sustituyen las pruebas de fabricantes reales. Validar en VMs limpias Windows 10 22H2 y Windows 11 x64: pack-only sin servicios/tareas/config Kiosk, combinación opcional, UAC, bootstrap sin WinGet, aplicación instalada anterior/superior/por usuario, versión retirada, fallo de red/hash, solicitud de reinicio, instalador lento, cancelación, reanudación tras reinicio y modo completamente silencioso. No ejecutar el cliente Kiosk en el escritorio de desarrollo: activa protección fullscreen.

Referencias primarias: [SDK COM oficial](https://www.nuget.org/packages/Microsoft.WindowsPackageManager.ComInterop/1.29.380), [módulo Microsoft.WinGet.Client](https://www.powershellgallery.com/packages/Microsoft.WinGet.Client/1.12.440), [contratos de la API](https://github.com/microsoft/winget-cli/blob/master/src/Microsoft.Management.Deployment/PackageManager.idl) y [manifiestos oficiales](https://github.com/microsoft/winget-pkgs).
