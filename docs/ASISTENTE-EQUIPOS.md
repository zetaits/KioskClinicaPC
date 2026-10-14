# Asistente para preparar equipos

Guía del comportamiento actual del repositorio. Las publicaciones y la versión
activa se consultan en `/instalador`. Los resultados fechados están en el
[histórico](historico/README.md); un build local no cambia la descarga del panel.

## Ediciones y requisitos

El asistente privado ofrece solo pack, solo Kiosk o ambos. Su versión es
independiente de la de Kiosk incluida. Se distribuye como EXE WPF autónomo,
con .NET incorporado, para Windows 10 22H2 y Windows 11 x64.

| Edición | Componentes | Conectividad |
| --- | --- | --- |
| Online | Descarga únicamente los componentes seleccionados de la VPS | Necesita conexión para prepararlos |
| Completo para USB | Incluye las mismas versiones de Kiosk y trabajador | Solo Kiosk puede instalarse sin conexión |

El pack necesita internet en ambas ediciones. Estos EXE contienen
aprovisionamiento limitado y se descargan desde el panel autenticado; se publican
como candidatos privados. La instalación inicial de Kiosk se ofrece únicamente
desde estas ediciones del asistente en `/instalador`. El instalador interno de
Kiosk también se usa para las actualizaciones administradas; no se distribuye
como descarga independiente.

## Uso

1. Abrir el EXE descargado de `/instalador` y elegir los componentes. Pack está
   seleccionado inicialmente y Kiosk desmarcado.
2. Revisar las aplicaciones, la selección inicial y la opción de reanudación.
   Un pack vacío permite volver a componentes.
3. Pulsar **Instalar**. Windows solicita una elevación del trabajador. Se vuelve
   a comprobar la revisión y los identificadores del catálogo autorizado; un
   cambio devuelve la operación a revisión antes de preparar WinGet o instalar.
4. Se preparan y verifican los componentes seleccionados. Después se comprueban
   todas las aplicaciones, se instala Kiosk si se eligió y se ejecuta el pack.
   Inno actúa como motor silencioso de Kiosk.
5. Revisar el resultado. **Finalizar y abrir Kiosk** se ofrece cuando los
   componentes elegidos están verificados, el trabajador ha terminado y Kiosk
   no ha pedido reinicio. Se puede desmarcar la apertura. Si Windows indica un
   reinicio pendiente tras una preparación completada, **Reiniciar ahora** lo
   solicita solo tras confirmarlo. Si la preparación queda incompleta, revisa el
   diagnóstico y cualquier instalador activo antes de reiniciar manualmente.

Solo Kiosk omite el catálogo de aplicaciones y WinGet. El autostart se registra
para el usuario original, incluso si UAC utiliza otra cuenta administradora.

La opción **Instalar las aplicaciones disponibles aunque alguna falle la
comprobación** está marcada inicialmente. Los rechazos ordinarios quedan
pendientes y las aplicaciones comprobadas pueden continuar. Si todas fallan,
Kiosk seleccionado puede instalarse de forma independiente. Un instalador
activo o de estado incierto detiene la cola. Un resultado parcial conserva los
errores y devuelve código 2.

Al cancelar o cerrar durante una instalación, la ventana espera al trabajador.
No mata instaladores nativos ni borra sus recursos mientras se usan. Un resultado
ambiguo exige comprobar el equipo o reiniciar manualmente antes de reintentar.

La pantalla final distingue visualmente preparación completa, reinicio pendiente,
preparación parcial, detenida y sin completar. Si hay pack, resume cuántas
aplicaciones están verificadas, pendientes o con error. Muestra primero las que
requieren atención y deja el detalle completo en un desplegable. El código de
salida no ocupa el resumen visible.

El pack comprueba también si Windows tiene un reinicio pendiente, aunque WinGet
no lo indique: operaciones de archivos aplazadas, mantenimiento de Windows o
Windows Update. Conserva asimismo los códigos nativos 3010 y 1641. Un reinicio
pendiente puede proceder de una instalación anterior; no se atribuye únicamente
a la última aplicación. El aviso no detiene las otras aplicaciones ni convierte
una versión verificada en fallo. Se muestra al finalizar, pero permite abrir
Kiosk si está verificado y su propio instalador no ha pedido reinicio. El
reinicio se puede hacer ahora o más tarde; solo se inicia al pulsar el botón y
confirmar. Si el instalador de Kiosk pide reinicio, se espera antes de abrirlo.

## Aplicaciones y reanudación

La definición procede de `/api/setup/v3/catalog` con `X-Setup-Key` y el encabezado
`X-Setup-Catalog-Version: 3`. Contiene identificadores, orden y selección inicial.
La [gestión del pack](PACK-WINGET.md) y el catálogo de MSI/EXE de la flota son
independientes.

El trabajador actualiza el origen oficial WinGet y utiliza su comparador para
elegir la última versión compatible del canal predeterminado. Exige instalador
aplicable, silencioso, para todo el equipo y sin autenticación interactiva.
Comprueba el manifiesto oficial, conserva los hashes obligatorios y deja que
WinGet resuelva las dependencias. La versión observada en el índice del panel
es orientativa.

Chrome Enterprise tiene una excepción acotada: su URL oficial sirve siempre el MSI
más reciente y puede cambiar antes de que WinGet publique el nuevo hash. Si WinGet
devuelve exactamente un error de hash para `Google.Chrome`, el trabajador termina
primero las demás aplicaciones y descarga el MSI x64 desde `dl.google.com`. Exige
HTTPS en ese dominio, firma Authenticode válida de Google LLC, identidad MSI
`Google Chrome` y versión igual o superior a la resuelta. Después lo instala en
silencio para todo el equipo y verifica versión y ámbito como el resto del pack.
Un fallo de cualquiera de esas comprobaciones deja Chrome con error; no se activa
la opción de WinGet que ignora los hashes.

Para cualquier aplicación, un `DownloadError` definitivo antes de iniciar el
instalador permite un solo reintento de WinGet, salvo si el hash no coincide.
Un hash distinto indica que repetir la misma descarga no resolverá el desfase;
en ese caso se muestra el HRESULT numérico para el diagnóstico. Una instalación
que ya haya empezado no se reintenta automáticamente.

La versión resuelta queda congelada antes de instalar. Una discrepancia entre
catálogo y manifiesto permite como máximo una actualización adicional por
ejecución. Un fallo de red no autoriza usar un catálogo desactualizado como si
estuviera actualizado ni seleccionar silenciosamente una versión anterior.

La cola admite hasta cinco aplicaciones en curso. WinGet solapa hasta tres
descargas (según la capacidad del equipo) y ejecuta una sola instalación nativa.
Una verificación independiente comprueba la versión y el ámbito mientras puede
instalarse la siguiente aplicación. Las verificaciones se procesan de una en una;
las que esperan siguen ocupando una plaza de la cola.

El orden del panel prioriza la entrada en la cola. Entre las aplicaciones
admitidas, WinGet puede instalar primero la que termine de descargarse. La lista
de progreso conserva el orden del catálogo y muestra la fase de cada aplicación.
El detalle muestra una sola frase por aplicación: conserva los bytes descargados y
los errores sin repetir etiquetas como «Instalando: Instalando…».
Este comportamiento se aplica al modo gráfico, al silencioso y al trabajador por
red. La API COM usa `System/User`; los manifiestos YAML
usan `machine/user`. Una versión igual o superior ya instalada para todo el
equipo se omite. Una instalación por usuario, un ámbito desconocido o una
versión no comparable requieren revisión.

Al reanudar se comprueba de nuevo lo instalado. Las aplicaciones verificadas
conservan su versión resuelta; los pendientes vuelven a resolver la última
compatible. Una selección nueva no evita la protección de un instalador anterior
sin verificar. Un reinicio permite despejar una protección del arranque anterior,
pero una instalación activa detectada en la comprobación actual sigue bloqueando.

Cada comprobación tiene un límite de cinco minutos y pasa la cancelación a COM
y HTTP. Trabaja sobre una copia: una respuesta tardía no modifica el estado
guardado. Un timeout de consulta no crea una marca nueva de instalador nativo
activo; las protecciones anteriores del mismo arranque se conservan.

La consulta de manifiestos permite tres intentos y 45 segundos por petición.
Respeta `Retry-After`. Un fallo compartido de transporte, timeout o HTTP 408/429/5xx
agotado, o un `Retry-After` superior a un minuto, evita repetir esas esperas para
las siguientes aplicaciones de esa ejecución. Los 404 e incompatibilidades son
particulares de cada app. Una ejecución nueva vuelve a consultar el repositorio.

Cada operación WinGet tiene un límite de 60 minutos desde su admisión, incluyendo
la cola y la descarga. La verificación posterior tiene un límite total de dos
minutos y hasta seis consultas, separadas por cinco segundos; no espera después
de la última consulta fallida.

Si WinGet confirma el éxito pero falla la verificación, esa aplicación queda
pendiente y se continúan las restantes. No se reinstala automáticamente en esa
ejecución y el pack permanece incompleto. La protección para reanudar se conserva.
Una cancelación, timeout nativo o pérdida del resultado COM cierra la admisión y
cancela las operaciones pendientes. Se espera al instalador que ya se sabe activo,
sin matarlo, dentro del límite de tiempo. Si no se confirma su resultado, se guarda
como incierto y no se reintenta hasta comprobarlo. Las respuestas tardías trabajan
con copias y no cambian el resultado guardado.

Si WinGet necesita preparación, se usa el módulo oficial `Microsoft.WinGet.Client`
1.12.440 y `Repair-WinGetPackageManager` para WinGet 1.29.380. El módulo se reutiliza
si puede importarse; solo se instala, junto con NuGet si falta, cuando hace falta.
Después se verifica la activación del runtime. Si se cancela durante la reparación,
se espera a que termine el proceso nativo.

## Contraseña inicial de Kiosk

Los asistentes publicados con aprovisionamiento válido y Kiosk 1.2.1 o posterior
incluyen un verificador PBKDF2 de la contraseña del panel vigente al generar el
EXE. Un perfil sin contraseña local lo recibe como `KioskPanelPassword.json` y
puede validarlo sin conexión. Las contraseñas locales existentes se conservan.
Cambiar la contraseña del panel después no actualiza perfiles ni EXE anteriores.

El verificador se obtiene en el build desde `GET /api/releases/setup/kiosk-password`
con `X-Release-Publish-Key`. No se incluye en el componente interno de Kiosk ni en
el manifiesto. El pack solo no instala ese archivo. Sin aprovisionamiento válido,
Kiosk mantiene el diálogo de contraseña local. El diagnóstico muestra únicamente
`panelPasswordProvisioned`.

## Estado y diagnósticos

Estado y logs del trabajador: `%ProgramData%\ClinicaPC\Setup\last-run.json`,
`kiosk-run.json` y `logs/`. Los errores de interfaz y autostart se registran en
`%LOCALAPPDATA%\KioskClinicaPC\Setup\logs\assistant-AAAAMMDD.log`. No se registran
claves, verificador de contraseña ni mensajes de excepción que puedan contenerlos.

Después de cada resultado nativo de instalación, `*.log.native-result.jsonl`
registra el estado WinGet, el código del instalador, HRESULT numérico, señal de
reinicio de WinGet, señal de Windows y resultado combinado. La consulta al Registro
es de solo lectura, utiliza la vista de 64 bits y no registra rutas de archivos
pendientes. Si una consulta no está disponible, se deja un diagnóstico sin tratar
ese fallo de lectura como un fallo de la instalación.

Cada ejecución del pack tiene `timings.jsonl` con fecha de inicio y fin, fase,
aplicación, duración y resultado. `Install` mide la operación WinGet, incluida su
consulta inicial, cola y descarga, y excluye la verificación. `Queued`, `Download`,
`NativeInstall` y `PostInstall` son intervalos observados mediante el progreso COM;
WinGet puede omitir notificaciones y agrupar fases. `Verification` mide cada
comprobación final. `PackExecution` mide el tiempo real de la cola completa después
de autorizarla; `PackTotal` añade la comprobación previa y la espera del coordinador
(incluida la instalación de Kiosk si se seleccionó). Bootstrap y actualización del
catálogo conservan sus registros independientes. Las fases solapadas no se suman.
Los archivos `*.verification.jsonl` registran versión
requerida/instalada, ámbito COM, comparación nativa y decisión.

El estado añade `phase` y `nativeInstallSucceeded` sin cambiar los valores de
`state` ni los protocolos de catálogo/componentes. Los estados antiguos se leen
con la política conservadora previa. El progreso de bytes se emite como máximo
cada 250 ms por aplicación; las transiciones y los resultados se emiten y guardan
inmediatamente. Las copias emitidas y guardadas no cambian con callbacks posteriores.

Para comparaciones locales automatizadas, `PackExecutionOptions(MaxInFlight: 1)`
desactiva el solapamiento. El valor normal es cinco; es una opción interna del
motor, sin ajustes adicionales en el asistente.

Estos comandos del trabajador son de solo lectura. Ejecutarlos desde su carpeta
completa, conservando DLL nativa y WinMD junto al EXE:

```powershell
& .\KioskSetupHelper.exe --diagnose-winget-json
& .\KioskSetupHelper.exe inspect-installed-json IZArc.IZArc 4.6
& .\KioskSetupHelper.exe audit-last-run-json
```

El primero comprueba activación y opciones sin reparar WinGet. El segundo consulta
lo instalado. El tercero reproduce las comprobaciones del último snapshot, sin
instalar ni guardar estados; no sustituye la validación del catálogo autorizado
ni la resolución de versiones de una instalación nueva.

## Componentes y caché

El trabajador se publica desde `Kiosk.PackWorker`, sin WPF, con nombre físico
`KioskSetupHelper.exe`. `Kiosk.WinGetCore` comparte el motor, bootstrap, persistencia,
protocolo y exportación con el helper WPF antiguo. Su carpeta contiene EXE, DLL,
runtimeconfig, `Microsoft.Management.Deployment.dll` y
`Microsoft.Management.Deployment.winmd`.

`run.lock` se conserva durante comprobación, Kiosk y pack; `equipment.lock` coordina
instancias del asistente. Los directorios `work/` son exclusivos de administradores
y SYSTEM. Se exige el resultado final y un código de salida coincidente del
trabajador; el cierre normal del canal no invalida un resultado ya recibido.

La caché de componentes en `%ProgramData%\ClinicaPC\Setup\cache` permite escritura
solo a administradores/SYSTEM y rechaza reparse points. Cada uso verifica tamaño
y SHA-256. Las descargas `.part` se promueven tras validar, se reanudan con ETag y
Range y reinician el parcial ante una respuesta 200. Un hash incorrecto lo elimina.

Hay tres intentos ante fallos transitorios, dos minutos sin progreso y treinta
minutos por componente. La limpieza limita la caché a 1 GiB y retira archivos
antiguos disponibles y parciales abandonados de más de siete días; conserva
estados, logs y archivos bloqueados por operaciones activas.

El origen local `Payload.UseDirectory` de la
[instalación por red](INSTALACION-POR-RED.md) prepara los componentes locales sin
descargarlos de la VPS. Se conserva el exportador `export-index` y los comandos
del helper antiguo para compatibilidad.

## Compilación local ficticia

Desde la raíz, con .NET 10 SDK e Inno Setup 6:

```powershell
& .\build-equipment-setup.ps1
```

Los valores por defecto se toman de `build-equipment-setup.ps1` y del proyecto
`Kiosk.Client`. Para elegir otra versión, sustituir `X.Y.Z` por una versión nueva:

```powershell
$assistantVersion = 'X.Y.Z'
& .\build-equipment-setup.ps1 -Version $assistantVersion
& ".\installer\Output\Setup-EquipoClinicaPC-$assistantVersion.exe" --diagnose-json
```

Genera las ediciones Online y Completo y su `.bundle.json` en `installer/Output/`.
Usa `https://setup.invalid` y claves ficticias, sin aprovisionamiento de contraseña
del panel ni publicación. No instala nada; verifica los EXE empaquetados en modo
diagnóstico. Online comprueba manifiesto y ausencia de binarios opcionales;
Completo verifica tamaños, hashes y compatibilidad de los recursos incluidos.

Una versión existente no se sobrescribe. Para repetir un build local, elegir
otra versión o retirar explícitamente solo sus artefactos ficticios anteriores.
`-KioskVersion` permite fijar la versión del componente incluido; el contenido se
compila desde el checkout, no se descarga una release histórica por ese número.

Cada edición genera también un `.exe.sha256`. Para preparar archivos con la
configuración real sin importarlos al panel, PowerShell 7 admite:

```powershell
& .\build-equipment-setup.ps1 -Version $assistantVersion -PrepareRelease
```

Exige las mismas variables de `production` que `-Publish`, comprueba el servidor
y obtiene el aprovisionamiento de contraseña vigente. Genera ambas ediciones,
componentes, hashes y manifiesto; no importa ni activa la candidata. No combinar
`-PrepareRelease` y `-Publish`. Un build sin ninguno de esos modificadores sigue
siendo ficticio y no debe distribuirse.

## Modo completamente silencioso

Exige una consola ya elevada bajo un usuario interactivo, nunca SYSTEM. No abre
WPF, no solicita UAC adicional y no lanza el cliente fullscreen. Usar la versión
del EXE descargado o generado en `$assistantVersion`:

```powershell
& ".\Setup-EquipoClinicaPC-$assistantVersion.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack
& ".\Setup-EquipoClinicaPC-$assistantVersion.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=kiosk
& ".\Setup-EquipoClinicaPC-$assistantVersion.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack,kiosk /RESUME
```

Sin `/COMPONENTS`, usa solo pack y la selección inicial del panel. `/RESUME` vuelve a
comprobar el estado anterior. El modo desatendido continúa con las aplicaciones
disponibles ante rechazos ordinarios; conserva la parada ante estado nativo incierto.
No se aceptan URLs, comandos, rutas ni argumentos del fabricante.

| Código | Significado |
| --- | --- |
| 0 | Todos los componentes verificados |
| 1 | Fallo antes de instalar, permisos insuficientes o incompatibilidad |
| 2 | Incompleto, cancelado o sin verificación final |
| 64 | Argumentos no válidos |

## Publicación, activación y recuperación

1. Probar y guardar el código en un commit. Comprobar que el servidor compatible
   está desplegado siguiendo [Actualizar el panel](ACTUALIZAR-PANEL-VPS.txt).
   `/health/ready` debe devolver 200, `{"status":"ok"}` y los encabezados
   `X-Setup-Catalog-Version: 3`, `X-Setup-Component-Protocol: 1` y
   `X-Kiosk-Password-Provisioning: 1`.
2. Ejecutar el workflow manual **Equipment Setup** desde ese commit, con una versión
   nueva del asistente. `kiosk_version` vacío usa la versión del proyecto. El
   entorno `production` aporta `KIOSK_SERVER_API_KEY`, `KIOSK_INITIAL_SETUP_KEY` y
   `KIOSK_RELEASE_PUBLISH_KEY`; la clave de Setup debe coincidir con
   `Kiosk__InitialSetupKey` del servidor. La publicación local con `-Publish` requiere
   PowerShell 7, `KIOSK_SERVER_URL` y esas credenciales. Los EXE quedan privados.
3. El build crea Kiosk y trabajador una vez, genera ambas ediciones y las importa
   por `POST /api/releases/setup/v3`. El manifiesto es esquema 3 y los campos
   multipart son `manifest`, `online`, `complete`, `kiosk` y `worker`. El límite agregado
   sigue siendo 1 GiB más margen multipart. Se comprueban hashes, tamaños,
   nombres, protocolo y ZIP.
4. La publicación queda como candidata. Descargar ambas ediciones de esa versión
   desde `/instalador` y probarlas en los equipos piloto. Los componentes están
   fijados al mismo conjunto probado; no se resuelven dinámicamente a `latest`.
5. Después de aceptar las pruebas, activar esa versión desde el panel. Se vuelven
   a verificar EXE y componentes y se sustituye atómicamente `setups/v3/active.json`.
   La importación por sí sola no cambia la versión activa.
6. Para recuperar, elegir otra publicación validada en **Otras versiones y
   recuperación**. El servidor conserva hasta cinco versiones en total, incluida
   la que aparece en la tarjeta principal. La activa y la anterior de recuperación
   tienen prioridad dentro de ese límite; las plazas restantes corresponden a las
   versiones con numeración más reciente.

La retención se aplica al arrancar el servidor y después de importar o activar
una versión. También se reintenta cada hora. Retira las carpetas de las ediciones
Online/Completo antiguas y después los componentes que ya no referencia ninguna
publicación conservada. Los componentes compartidos se mantienen. Si un archivo
está en uso o no se puede limpiar, se registra el fallo y se reintenta sin hacer
fallar una publicación o activación que ya terminó correctamente.

Una marca mínima en `setups/v3/retired/` impide reutilizar números de versión
retirados. Las versiones eliminadas dejan de estar disponibles para descarga o
recuperación; un EXE Online antiguo sin componentes en caché puede necesitar
sustituirse por una versión conservada. El límite se aplica a las publicaciones
v3 de equipos, conservando los almacenes de instalación por red y del formato
anterior.

Si el puntero activo no se puede leer, se conserva el almacén. Si no se pueden
interpretar todos los manifiestos restantes, se conservan los componentes hasta
que se pueda determinar qué referencias siguen vigentes. La limpieza rechaza
enlaces y rutas redirigidas y utiliza el mismo bloqueo que publicación y activación.

Un reintento con los mismos archivos y manifiesto es idempotente. Cambiar el
contenido exige una versión nueva. **Equipment Setup** no crea una release de
actualización de Kiosk ni activa actualizaciones de la flota.

`GET /panel/setup/download` sirve Online de la versión activa; `edition=complete`
sirve USB y `version=X.Y.Z` permite descargar una candidata concreta. Los componentes
inmutables se obtienen con `X-Setup-Key` desde
`GET /api/setup/v3/components/{kind}/{sha256}`, con ETag y Range.

Los archivos de publicaciones antiguas se conservan en disco para el historial
y para evitar reutilizar sus números de versión. Ya no se pueden publicar por
`/api/releases/setup`, activar ni descargar con `version=legacy`. El servidor
mantiene temporalmente el catálogo v2 para asistentes antiguos ya descargados;
no es una vía para obtener el instalador de Kiosk.

## Carga del apartado Instalador

`/instalador` muestra primero la estructura de las tarjetas y completa por separado
las secciones de equipos e instalación por red. Durante la consulta, versión,
tamaño y botones se representan con un esqueleto; el mensaje de ausencia de
publicaciones aparece únicamente cuando termina la consulta. Un error permite
reintentar esa sección sin recargar las demás.

El listado lee manifiestos y comprueba compatibilidad, rutas, existencia y tamaño
de los archivos. Abrir la pantalla no calcula sus SHA-256 ni abre los EXE o ZIP.
Las comprobaciones completas siguen ejecutándose al importar, activar y descargar.
Por ello, un archivo alterado que conserve su tamaño puede aparecer en el listado,
pero no supera la validación necesaria para activar o descargar esa publicación.

**Otras versiones y recuperación** consulta sus datos al desplegarse por primera
vez. El contador aparece tras cargar el historial; las siguientes aperturas
reutilizan el resultado durante esa visita. Una visita nueva consulta de nuevo
los manifiestos. Los resultados iniciales también se conservan al pasar del
prerenderizado a la interacción de Blazor.

El enlace de instalación por red fija la versión mostrada mediante
`/panel/deployment/download?version=X.Y.Z`. Si esa publicación desaparece o no
supera la verificación, la descarga se rechaza sin sustituirla por otra. El endpoint
sin `version` conserva la selección de la última publicación válida.

Los fallos de almacenamiento se registran como advertencias y el tiempo de las
consultas como mensajes Debug de `Kiosk.Server.Services.InstallerPageQuery`.
La recepción progresiva de la página debe comprobarse también a través del proxy
al publicar el servidor.

## Verificación y aceptación

```powershell
. .\tools\resolve-dotnet.ps1
$sdk = Resolve-KioskDotnet
& $sdk test .\KioskClinicaPC.sln -c Release -m:1 --disable-build-servers -nologo
```

Las pruebas automatizadas cubren selección, catálogo y cambios de revisión,
resolución de versiones, cancelación y reanudación, instalación parcial, estados
nativos ambiguos, caché e integridad, publicación y comunicación del trabajador.
Los resultados concretos se conservan en el [histórico](historico/README.md).

La instalación completa en VMs limpias Windows 10 22H2 y Windows 11 x64 sigue
pendiente en la última verificación local. La aceptación debe comprobar:

- Frontend autónomo sin .NET/WinGet, una sola elevación y bootstrap real.
- Solo pack, solo Kiosk y ambos; UAC con otra cuenta y autostart del usuario original.
- Versiones instaladas anterior/superior/por usuario y versión retirada entre
  comprobación e instalación, manteniendo la versión congelada.
- Fallo ordinario de una o todas las apps, resultado parcial y continuidad de
  Kiosk independiente; instalador activo o incierto con parada de la cola.
- Pérdida de red, descarga reanudable, hash incorrecto, cancelación, instalador
  lento, reinicio manual y reanudación conservando aplicaciones verificadas.
- Online solicitando solo componentes elegidos; Completo con solo Kiosk sin red.
- Modo silencioso y candidata/activación/recuperación en servidor de laboratorio.

La compilación y los tests no equivalen a haber instalado aplicaciones de sus
fabricantes. Las pruebas de instalación y del cliente fullscreen se realizan
en VM o equipos piloto, fuera del escritorio de desarrollo.
