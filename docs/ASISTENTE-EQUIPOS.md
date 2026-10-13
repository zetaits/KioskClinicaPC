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
como candidatos privados. Las actualizaciones públicas de Kiosk se generan por
separado y siguen siendo Kiosk-only.

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
   componentes elegidos están verificados, el trabajador ha terminado y no hay
   reinicio pendiente. Se puede desmarcar la apertura. Los reinicios son manuales.

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

La versión resuelta queda congelada antes de instalar. Una discrepancia entre
catálogo y manifiesto permite como máximo una actualización adicional por
ejecución. Un fallo de red no autoriza usar un catálogo desactualizado como si
estuviera actualizado ni seleccionar silenciosamente una versión anterior.

Se instala secuencialmente y se verifica el ámbito y la versión antes de pasar
a la siguiente aplicación. La API COM usa `System/User`; los manifiestos YAML
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

Una instalación nativa tiene un límite de 60 minutos. WinGet puede cancelar una
descarga, pero no garantiza detener un instalador ya activo. Una cancelación,
timeout o resultado sin verificar detiene la cola y conserva el pack incompleto.

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
con `X-Release-Publish-Key`. No se incluye en el instalador público de Kiosk ni en
el manifiesto. El pack solo no instala ese archivo. Sin aprovisionamiento válido,
Kiosk mantiene el diálogo de contraseña local. El diagnóstico muestra únicamente
`panelPasswordProvisioned`.

## Estado y diagnósticos

Estado y logs del trabajador: `%ProgramData%\ClinicaPC\Setup\last-run.json`,
`kiosk-run.json` y `logs/`. Los errores de interfaz y autostart se registran en
`%LOCALAPPDATA%\KioskClinicaPC\Setup\logs\assistant-AAAAMMDD.log`. No se registran
claves, verificador de contraseña ni mensajes de excepción que puedan contenerlos.

Cada ejecución del pack tiene `timings.jsonl` con fecha, fase, aplicación,
duración y resultado. Las fases son `Bootstrap`, `CatalogRefresh`, `Preflight`,
`Install` y `Verification`; `Install` incluye la verificación, por lo que esas dos
duraciones no deben sumarse. Los archivos `*.verification.jsonl` registran versión
requerida/instalada, ámbito COM, comparación nativa y decisión.

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
   recuperación**. Conservar las publicaciones y los componentes referenciados.

Un reintento con los mismos archivos y manifiesto es idempotente. Cambiar el
contenido exige una versión nueva. **Equipment Setup** no crea una release pública
de Kiosk ni activa actualizaciones de la flota.

`GET /panel/setup/download` sirve Online de la versión activa; `edition=complete`
sirve USB y `version=X.Y.Z` permite descargar una candidata concreta. Los componentes
inmutables se obtienen con `X-Setup-Key` desde
`GET /api/setup/v3/components/{kind}/{sha256}`, con ETag y Range.

Las API de Setup anteriores se conservan para compatibilidad. El servidor migra
el pack a `pack-definition-v3.json` y mantiene `pack-applications.json` como proyección
con versiones concretas para `/api/setup/v2/catalog`. Esa proyección tiene revisión
propia y no modifica la definición v3. Las publicaciones esquema 2 usan
`/api/releases/setup` y pueden recuperarse con `version=legacy`.

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
