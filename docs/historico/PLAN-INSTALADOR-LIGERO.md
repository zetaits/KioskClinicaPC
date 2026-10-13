# Implementación autorizada: instalador ligero y edición USB

Plan histórico que dio origen a las ediciones Online y Completo. El encargo
original se conserva a continuación como contexto; no es una tarea pendiente
ni una autorización para ejecutar acciones ahora. El funcionamiento actual está
en la [guía del asistente](../ASISTENTE-EQUIPOS.md).

El usuario ha aprobado este plan y ha pedido implementarlo en una sesión fresca. Implementar, no volver a proponer el plan ni pedir confirmación para cambios reversibles. Leer AGENTS.md, inspeccionar el código y preservar cambios ajenos. No leer `.env` ni `.env.*`. No ejecutar el kiosco en el equipo de desarrollo: activa protección de escritorio. Esta tarea es implementar y verificar; la puesta en producción descrita abajo es el procedimiento que debe quedar soportado y documentado, sin desplegar ni publicar binarios de producción durante las pruebas.

## Contexto verificado

- Repositorio en C:\Users\zits\Documents\Proyectos\KioskClinicaPC. Estado limpio antes de crear este documento, commit 4a0f38e. No hay implementación de este nuevo plan realizada todavía.
- SDK: C:\Users\zits\.dotnet\dotnet.exe. La solución incluye también Kiosk.EquipmentSetup, Kiosk.EquipmentCore y Kiosk.EquipmentSetup.Tests; inspeccionar la solución real.
- Asistente actual 1.4.0. El catálogo API v3 y la instalación parcial del pack ya están implementados: selección por IDs, últimas versiones compatibles WinGet, actualización acotada ante cambios, reanudación y salvaguardas frente a instaladores nativos activos. Preservar esos comportamientos.
- EXE actual: 349,7 MiB. Incluye instalador Kiosk de 150,2 MiB y worker.zip de 66,6 MiB. El resto corresponde aproximadamente al asistente y su runtime.
- Prueba no funcional de tamaño, sin componentes y con EnableCompressionInSingleFile=true: src/Kiosk.EquipmentSetup/bin/Release/size-probe/Setup-EquipoClinicaPC.exe, 64.674.272 bytes (61,7 MiB). Compiló correctamente; no demuestra todavía instalación ni conectividad.
- Preferencias expresamente elegidas: conservar WPF y runtime autónomo; online como descarga principal; incluir finalmente también edición completa USB; componentes fijados a un conjunto probado por versión del asistente; incluir trabajador WinGet sin WPF. Las aplicaciones del pack siguen necesitando internet en ambas ediciones.
- Payload actual: esquema 2, catálogo 3, recursos Equipment.*, SHA-256 incluidos. UseDirectory sirve al despliegue por red; mantener ese origen sin obligarlo a descargar desde la VPS.
- worker.zip requiere KioskSetupHelper.exe/.dll/.runtimeconfig.json y Microsoft.Management.Deployment.winmd físico; pack-worker.json esquema 1, catálogo 3. Mantener estos nombres/protocolo en el nuevo trabajador. Su ZIP tiene controles contra traversal, enlaces, duplicados, exceso de tamaño y archivos; preservar y probarlos.
- SetupHelper es WPF; el motor WinGet y bootstrap no requieren interfaz. EquipmentWorker y LocalState dependen de Program.StateRoot/ReadLastRun/Save: extraer esa persistencia compartida. InstallerCore ya es una biblioteca sin WPF. Los tests Server.Tests enlazan ManifestPolicy, ExportDiagnostics y CatalogExportReader como fuentes; actualizar sus rutas al moverlos sin añadir dependencia Windows al servidor.
- La subida existente POST /api/releases/setup recibe manifiesto y un EXE; store InitialSetupBundleStore esquema 2. Panel /panel/setup/download autentica por cookie. La nueva API debe coexistir con todo ello. Almacén persistente VPS /var/lib/kiosk-server/setups. Límite actual agregado de petición aproximadamente 1 GiB.
- build-equipment-setup.ps1 construye Kiosk, publica SetupHelper en carpeta, crea ZIP, incrusta ambos componentes y publica frontend self-contained single-file. --diagnose-json comprueba actualmente ambos recursos incluidos; habrá que adaptarlo a la edición online.
- GitHub Actions Equipment Setup comprueba catálogo 3 en /health/ready y publica usando secretos production; conservar separación entre claves de publicación y de descarga. No imprimir ni copiar secretos a este documento ni al código.

## Resultado previsto

Publicar dos ediciones del asistente 1.5.0:

- Online: interfaz WPF y runtime; obtiene de la VPS únicamente los componentes seleccionados.
- Completo para USB: incluye los mismos componentes; Kiosk puede instalarse sin conexión. El pack requiere internet para obtener sus aplicaciones.

Objetivo: edición online inferior a 100 MB. Ambas ediciones usan exactamente las mismas versiones probadas de Kiosk y del trabajador, no las últimas versiones de componentes elegidas dinámicamente.

## Asistente y trabajador

- Activar EnableCompressionInSingleFile conservando distribución autónoma, sin exigir instalar .NET previamente.
- Proveedor de componentes con tres orígenes: remoto online, recursos incluidos USB y archivos locales del despliegue por red.
- Nueva fase de preparación después de validar la selección y antes del bootstrap o cualquier instalación. Obtener trabajador únicamente si hay pack, Kiosk únicamente si está seleccionado. Verificar todos los componentes requeridos antes de ejecutar alguno. Añadir un punto de preparación inyectable a EquipmentExecution sin romper consumidores existentes.
- Comprobar tamaño, SHA-256 y compatibilidad antes de ejecutar. Fallo de descarga: detener preparación con mensaje que identifique el componente y permita reintentar. Mantener instalación parcial de aplicaciones, reanudación, exclusión entre procesos, UAC con otra cuenta, usuario original y salvaguardas de instaladores nativos.
- Crear Kiosk.WinGetCore para motor, bootstrap, protocolo y persistencia compartidos; Kiosk.PackWorker como ejecutable sin WPF, self-contained en carpeta, AssemblyName KioskSetupHelper. Mantener metadatos WinGet físicos y protocolo actual. No introducir trimming/AOT en esta mejora.
- Helper existente conserva comandos antiguos y ventana de progreso, reutilizando núcleo compartido. Exportación del índice sigue operativa.

## Descargas, caché e interfaces

- Manifiesto del asistente esquema 3: edición, versión del protocolo de componentes y descriptores de Kiosk/trabajador con versión, tamaño y SHA-256. Mantener lectura de manifiestos anteriores/locales.
- GET /api/setup/v3/components/{kind}/{sha256}, autenticado con X-Setup-Key y limitado a componentes publicados. Archivos inmutables con ETag y Range. Protocolo de componentes inicial 1, independiente de catálogo 3; anunciarlo en /health/ready.
- Derivar rutas del servidor configurado; HTTPS en producción (HTTP loopback para tests), sin redirecciones ni URLs arbitrarias que puedan filtrar credenciales.
- Caché %ProgramData%\ClinicaPC\Setup\cache, escritura exclusiva administradores/SYSTEM, identificada por hash y reverificada antes del uso. Defender contra reparse points y caminos manipulados como en los directorios de trabajo existentes.
- Descarga a temporal; promoción atómica tras verificar. Reanudar solo con ETag y respuesta Range correctos; respuesta 200 a Range reinicia el archivo. Un parcial nunca puede ejecutarse. Hash incorrecto invalida el parcial.
- Tres intentos ante errores transitorios; dos minutos máximos sin progreso y treinta minutos por componente. Progreso y cancelación tanto interfaz como ejecución silenciosa.
- Caché 1 GiB, limpieza primero de archivos antiguos sin uso; parciales abandonados siete días. No borrar registros, estados ni archivos usados por procesos/operaciones activos.

## Construcción, publicación y panel

- build-equipment-setup.ps1 construye componentes una sola vez y dos EXE comprimidos del mismo conjunto. Online Setup-EquipoClinicaPC-1.5.0.exe; completo Setup-EquipoClinicaPC-1.5.0-Completo.exe. Manifiesto de publicación esquema 3 enumera ambos y sus componentes con hashes/tamaños/versiones/commit.
- POST /api/releases/setup/v3 con X-Release-Publish-Key importa manifiesto, ambos asistentes y componentes. Validación en disco, límite agregado existente, reintentos idénticos idempotentes, versión existente con contenido diferente rechazada.
- Componentes por hash bajo setups; conservar los referenciados por publicaciones previas. No eliminar almacenes antiguos. Un fallo de subida nunca cambia versión activa.
- Nueva publicación candidata; activación explícita desde el panel únicamente tras comprobar ambos asistentes y componentes. Puntero activo persistente y cambio atómico; poder restaurar versión anterior. Antes de primera activación v3, mantener oferta del artefacto compatible actual 1.4.0.
- /panel/setup/download por defecto online de versión activa; edition=complete para USB; permitir descarga de candidata por versión. Panel muestra dos opciones, tamaños y necesidad de internet para pack. Activación/restauración requiere cookie admin y antiforgery. No mezclar ediciones de versiones diferentes ni cambiar silenciosamente componentes a latest.
- Actualizar Actions: tests, diagnósticos distintos para ambas ediciones, tamaños y verificación del protocolo nuevo antes de publicar candidata. No activar automáticamente durante publicación.
- --diagnose-json online valida manifiesto/config y ausencia de binarios incrustados sin descargarlos ni afirmar que sus hashes fueron verificados. Completo valida realmente ambos recursos. Actualizar smoke tests de empaquetado.
- Documentar procedimiento: servidor primero, publicar candidata 1.5.0, probar equipos piloto, activar; mantener 1.4.0 para recuperación. Despliegue del servidor no reconstruye asistentes.

## Pruebas y aceptación

- Kiosk solo, pack solo y ambos: no descargar desmarcados; validación de selección antes de preparación; ningún bootstrap ni instalación si preparación falla.
- Caché válida/corrupta, Range/reanudación, ETag distinto, respuesta 200, cancelación, fallo de red, 401/404/5xx, tamaños/hash incorrectos, ZIP inseguro e incompatible; ningún archivo rechazado se ejecuta.
- Publicación incompleta, reintento idéntico/diferente, autenticación, Range servidor, activación atómica, persistencia tras reinicio y rollback.
- Protocolo/resultado del nuevo worker, exportación de catálogo y comandos legacy conservados; regresiones de instalación parcial, última versión compatible, reanudación y proceso nativo activo.
- Build con SDK indicado; tests relevantes de EquipmentSetup y Server, luego suite y smoke tests de artefactos finales. No maquillar fallos ni relanzar tests hasta obtener verde sin investigar.
- Validar en VM Windows 10/11 sin .NET: UAC con otra cuenta, WinGet ausente, reinicio/reanudación, Kiosk USB sin conexión. No activar kiosk en PC de desarrollo. Si no hay VM disponible, reportar esa comprobación como pendiente con procedimiento reproducible.
- Compatibilidad del origen local usado por despliegue por red, sin rediseñar distribución PXE.
- Online <100 MB; completo menor que actual; worker.zip menor que 66,6 MiB; registrar tamaños reales y arranque. No prometer tamaño final del completo antes de medir.
- Fuera de alcance: reescritura WPF, runtimes compartidos entre productos y repositorio offline de aplicaciones.

Implementar de principio a fin y comunicar avances. Al terminar, informar qué cambió, tamaños medidos, verificaciones que pasaron y límites pendientes. No detenerse después de preparar un esqueleto ni reemplazar la implementación por otro plan.
