# Servidor de contenido y panel (opcional)

El kiosko funciona **solo**, sin nada más. Este servidor es la capa **para varias máquinas**: un único sitio
que edita el contenido compartido de toda la tienda y sincroniza el bucle de atracción. Si no lo despliegas,
cada PC sigue siendo autónoma (modo local puro).

Las guías de operación y el historial de verificaciones están en el [índice de documentación](README.md).

- **Proyecto:** `src/Kiosk.Server` (ASP.NET Core, net10.0). Todo el cableado está en `Program.cs`.
- **Qué expone:** API de contenido (`/api/*`), biblioteca de imágenes, hub de sincronización (SignalR) y el
  **panel de administración** (Blazor Server).
- **Reparto de responsabilidades:** el servidor manda en el contenido **compartido** (identidad de tienda,
  slides, textos, marketing, imágenes); cada kiosko conserva lo **local** por-máquina (**precio, estado y
  especificaciones autodetectadas**). El servidor nunca los pisa (`SharedContent`).

> Regla de oro del kiosko: si el servidor no responde, ningún kiosko se queda en negro — usa la última copia
> cacheada y sigue funcionando. El servidor puede caer sin tumbar la tienda.

---

## Ejecutar

En desarrollo (arranca en `http://localhost:5xxx`, lo imprime en consola):

```powershell
. .\tools\resolve-dotnet.ps1
$dotnetExe = Resolve-KioskDotnet
& $dotnetExe run --project src/Kiosk.Server
```

La VPS de producción se actualiza con el [procedimiento único](ACTUALIZAR-PANEL-VPS.txt). El host necesita el runtime ASP.NET Core 10 y ejecuta el bundle como servicio `systemd`.

Sondas sin auth: `GET /health` comprueba que responde el proceso; `GET /health/ready` comprueba además el estado persistente. Ambas devuelven `{"status":"ok"}` cuando pasan.
La ficha pública que abren los QR queda incluida en el mismo bundle: `GET /ficha/`.

---

## Configuración

Se lee de `appsettings.json` o, mejor para producción, de **variables de entorno** `Kiosk__*` (doble guion
bajo). Todas las claves cuelgan de la sección `Kiosk`:

| Clave | Env var | Para qué | Por defecto |
|---|---|---|---|
| `ApiKey` | `Kiosk__ApiKey` | Protege las API de flota y contenido mediante `X-Api-Key`. Setup, publicación y despliegue tienen credenciales independientes. Es obligatoria fuera de Development. | vacía solo en Development |
| `InitialSetupKey` | `Kiosk__InitialSetupKey` | Clave de 64 hex separada y limitada al catálogo/descarga del pack durante la instalación inicial. Vacía deshabilita esa función. | vacía |
| `PanelInitialPassword` | `Kiosk__PanelInitialPassword` | Contraseña del panel que se hashea al crear `panel.json`. Mínimo 12 caracteres; después puede quitarse del entorno. | sin valor; el primer arranque falla de forma segura |
| `DataDir` | `Kiosk__DataDir` | Carpeta de los JSON de datos. | `data/` bajo el ContentRoot |
| `AssetsDir` | `Kiosk__AssetsDir` | Carpeta de la biblioteca de imágenes. | `assets/` bajo el ContentRoot |
| `InstallersDir` | `Kiosk__InstallersDir` | Binarios privados para instalaciones remotas de la flota; el pack WinGet usa otro catálogo. | `installers/` bajo el ContentRoot |
| `SetupDir` | `Kiosk__SetupDir` | Instaladores internos y manifiestos que el panel ofrece para descarga. | `setups/` bajo el ContentRoot |
| `UpdatesDir` | `Kiosk__UpdatesDir` | Releases firmadas que CI importa y la VPS distribuye a los kioscos. | `updates/` bajo el ContentRoot |
| `ReleasePublishKey` | `Kiosk__ReleasePublishKey` | Secreto exclusivo con el que CI importa releases; no se comparte con los kioscos. | vacío; publicación deshabilitada |
| `UpdateTokenKey` | `Kiosk__UpdateTokenKey` | Secreto para derivar los tokens por equipo y trabajo. Debe ser distinto de las otras claves. | `ApiKey` o clave de desarrollo |
| `UpdateSigningKeysDirectory` | `Kiosk__UpdateSigningKeysDirectory` | Carpeta de claves públicas ECDSA permitidas, una por fichero `<keyId>.pem`. | vacío |
| `MaxInstallerBytes` | `Kiosk__MaxInstallerBytes` | Tamaño máximo de cada instalador subido. | `1073741824` (1 GiB) |
| `SlideDurationMs` | `Kiosk__SlideDurationMs` | Duración de cada slide del attract. **Debe coincidir con el default del cliente (5200).** | `5200` |
| `TimeZone` | `Kiosk__TimeZone` | Zona horaria de **la tienda** (no la del VPS) para evaluar la vigencia de los eventos. Id de Windows (p.ej. `Romance Standard Time`) o IANA en Linux (`Europe/Madrid`). Si no resuelve, cae a la hora local del servidor y **avisa en el log**. | zona local del servidor |

Ejemplo de variables de entorno (no guardes secretos reales en `appsettings.json` ni en Git):

```text
Kiosk__ApiKey=una-clave-larga-y-secreta
Kiosk__InitialSetupKey=otra-clave-hexadecimal-de-64-caracteres
Kiosk__PanelInitialPassword=otra-contraseña-larga-y-distinta
Kiosk__ReleasePublishKey=secreto-exclusivo-para-ci
Kiosk__UpdateTokenKey=secreto-exclusivo-para-tokens
Kiosk__UpdatesDir=/var/lib/kiosk-server/updates
Kiosk__UpdateSigningKeysDirectory=/etc/kiosk-server/update-keys
Kiosk__TimeZone=Europe/Madrid
```

> ⚠️ En Production el servidor no arranca sin `ApiKey`, ni crea `panel.json` sin una contraseña inicial
> explícita. Usa secretos distintos. Tras el primer arranque correcto puedes borrar
> `Kiosk__PanelInitialPassword` del fichero de entorno: solo queda su hash en `panel.json`.

---

## HTTPS y proxy inverso

El navegador guarda la cookie de sesión del panel; sírvelo por **HTTPS**. En un VPS lo habitual es un proxy
inverso (Nginx / Caddy / IIS) delante de Kestrel, que termina TLS (Let's Encrypt) y reenvía a la app. El panel
y el hub (SignalR, WebSockets) van por el mismo host; asegúrate de que el proxy permite **WebSockets**.

Para subir instaladores, configura también en el proxy un límite de cuerpo igual o superior a
`MaxInstallerBytes` más el pequeño overhead multipart (por ejemplo, `client_max_body_size 1100m;` en Nginx). Los agentes solo aceptan HTTPS;
HTTP queda limitado a `localhost` para desarrollo.

---

## Primer arranque

1. **Contraseña del panel.** Define `Kiosk__PanelInitialPassword` antes de arrancar. Se guarda hasheada en
   `data/panel.json` (`PanelAuthStore`); el texto original se puede retirar del entorno tras ese primer arranque.
   El login está limitado por intentos (`LoginThrottle`): demasiados fallos seguidos desde una IP la bloquean
   un rato.
2. **Contenido inicial.** El servidor sirve `data/KioskConfig.json` como contenido compartido. Edítalo desde
   el panel (no a mano); el formato es el mismo `KioskConfig.json` que ya conoce el cliente.
3. **Imágenes.** En el primer arranque se copian una sola vez las imágenes incluidas con el cliente. Desde
   **Imágenes** se pueden buscar, previsualizar, reemplazar y borrar. Se aceptan PNG, JPEG, WebP, GIF y BMP;
   el servidor valida el contenido, elimina metadatos y lo normaliza a PNG. **SVG está prohibido** a propósito
   para evitar XSS almacenado en la vista previa. Una imagen borrada no reaparece al reiniciar.

---

## Apuntar los kioscos al servidor

En cada kiosko: **Ajustes → servidor** (o lo rellena el instalador del cliente). Escribe:

- `ServerUrl` — la URL pública del servidor: `https://panel.clinicapc.es`.
- `ServerApiKey` — la misma `ApiKey` del servidor.

Se guardan en `KioskSettings.json` del cliente. Con `ServerUrl` **vacío**, el kiosko vuelve al modo local puro.
Al configurar servidor, el cliente pasa el contenido compartido a **solo lectura** (el modo edición libre y el
editor de slides de Ajustes se desactivan; el precio y las specs siguen editándose por-máquina).

El workflow del asistente genera el componente Kiosk ya provisionado, sin pedir
estos datos al encargado de la tienda. Para generar un componente local de prueba:

```powershell
$env:KIOSK_SERVER_API_KEY = '<clave de 64 caracteres>'
.\build-installer.ps1 -ServerUrl 'https://panel.clinicapc.es'
Remove-Item Env:KIOSK_SERVER_API_KEY
```

La clave se pasa solo durante la compilación y no se guarda en el repositorio. El componente deja la provisión junto
al ejecutable y verifica URL + API key contra `/api/config/version`. Al abrirse bajo el usuario correcto, el
kiosko la copia a su `KioskSettings.json` si **tanto `ServerUrl` como `ServerApiKey` están vacíos**, también
en perfiles existentes. Una actualización conserva la contraseña, identidad y demás ajustes, incluso si
el UAC se aprobó con otra cuenta administradora. La clave queda incluida en el componente de Kiosk y es
extraíble por diseño; la clave limitada del asistente del pack es independiente.

---

## Endpoints

| Ruta | Auth | Qué hace |
|---|---|---|
| `GET /health` | — | Sonda de vida. |
| `GET /health/ready` | — | Sonda de contenido y datos persistentes. |
| `GET /ficha/` | — | Aplicación autocontenida que abre el QR y genera el PDF en el móvil. |
| `GET /api/config` | `X-Api-Key` | Contenido **efectivo** (base + evento vigente) que consumen los kioscos. |
| `GET /api/config/version` | `X-Api-Key` | Hash conjunto de contenido e imágenes; el cliente lo sondea para detectar cambios. |
| `GET /api/assets/manifest` | `X-Api-Key` | Inventario versionado con tamaño, dimensiones y SHA-256 de cada imagen. |
| `GET /api/assets/{ruta}` | `X-Api-Key` | Imágenes de la biblioteca (con guardia anti-traversal). |
| `POST /api/releases` | `X-Release-Publish-Key` | Importa desde CI un paquete de actualización de Kiosk y su manifiesto firmado; no lo activa. |
| `GET /api/updates/assignment` | `X-Api-Key` | Devuelve al kiosco su versión objetivo, ventana y token de trabajo. |
| `GET /api/updates/{id}/manifest` | API key + token de actualización | Manifiesto firmado ligado al trabajo. |
| `GET /api/updates/{id}/download` | API key + token de actualización | Descarga reanudable del instalador desde la VPS. |
| `GET /api/updates/{id}/authorize` | API key + token de actualización | Revalida justo antes de instalar que la release y la ventana siguen autorizadas. |
| `POST /api/updates/{id}/status` | API key + token de actualización | Progreso, error y confirmación del runner privilegiado. |
| `GET /api/installations/{id}/manifest` | API key + token de trabajo | Manifiesto inmutable ligado al equipo y paquete. |
| `GET /api/installations/{id}/download` | API key + token de trabajo | Descarga privada con soporte de rangos. |
| `POST /api/installations/{id}/status` | API key + token de trabajo | Progreso y resultado del agente. |
| `GET /api/setup/v3/catalog` | `X-Setup-Key` | Definición actual del pack: identificadores, orden, preselección y política de última versión compatible. |
| `GET /api/setup/v2/catalog` | `X-Setup-Key` | Proyección de versiones concretas para asistentes WinGet antiguos. |
| `POST /api/releases/winget-index` | `X-Release-Publish-Key` | Importa el índice oficial usado para buscar aplicaciones en el panel. |
| `GET /api/setup/catalog` | `X-Setup-Key` | Catálogo del Setup antiguo de binarios privados, conservado por compatibilidad. |
| `POST /api/setup/sessions` | `X-Setup-Key` | Sesión del Setup antiguo de binarios privados. |
| `GET /api/setup/sessions/{id}/packages/{packageId}/download` | clave + token de sesión | Descarga privada y reanudable para ese Setup antiguo. |
| `POST /api/setup/sessions/{id}/packages/{packageId}/status` | clave + token de sesión | Resultado de un paquete de ese Setup antiguo. |
| `GET /panel/setup/download` | cookie | Online de la versión activa; `edition=complete` para USB y `version=X.Y.Z` para candidatas. |
| `GET /panel/deployment/download` | cookie | Instalación por red verificada; `version=X.Y.Z` fija la publicación mostrada en el panel. Sin `version`, sirve la última válida. |
| `GET /api/releases/setup/kiosk-password` | `X-Release-Publish-Key` | Semilla PBKDF2 de la contraseña del panel para perfiles nuevos del asistente privado; no devuelve texto plano. |
| `POST /api/kiosk/password/verify` | `X-Api-Key` | Valida la contraseña del panel al abrir Ajustes; devuelve el verificador solo tras acertar, para la caché sin conexión. Limita los intentos fallidos. |

| `POST /api/releases/setup/v3` | `X-Release-Publish-Key` | Importa manifiesto esquema 3, ambos asistentes y componentes como candidata, sin activarla. |
| `GET /api/setup/v3/components/{kind}/{sha256}` | `X-Setup-Key` | Componentes publicados, inmutables, con ETag y Range. |
| `POST /panel/setup/activate` | cookie + antiforgery | Verifica y activa/restaura una candidata v3. |
| `POST /api/maintenance/{id}/status` | API key + token de mantenimiento | Resultado verificado de una autodesinstalación de Kiosk. |
| `POST /panel/installers/upload` | cookie + antiforgery | Añade un instalador compatible al catálogo privado de la flota. |
| `POST /login` · `POST /logout` | cookie | Sesión del panel (con antiforgery + throttle). |
| `GET /panel/assets/{cat}/{fichero}` | cookie | Vista previa de imágenes dentro del panel. |
| `/hub/sync` (SignalR) | — | Reloj maestro del attract (`SyncState`) + aviso `ContentChanged`. Sin datos sensibles. |
| `/` y páginas del panel | cookie | Panel Blazor Server; sin sesión redirige a `/login`. |

Publica el servidor con la ruta de verificación antes de distribuir el Kiosk que
la utiliza. Una respuesta de contraseña incorrecta no permite recurrir a la
caché local; la caché se usa cuando el servidor no responde o devuelve un error temporal.

El protocolo de estaciones de instalación por red usa credenciales propias y se describe en
[Instalación por red](INSTALACION-POR-RED.md).

Cómo llegan los cambios del panel a los kioscos: al guardar contenido o modificar una imagen, el panel emite
`ContentChanged` por el hub (**push** inmediato); como red de seguridad, cada cliente sondea
`/api/config/version` cada 90 s. El cliente descarga solo las imágenes cuyo tamaño o SHA-256 ha cambiado y las
guarda en una caché separada. Si la red falla conserva la última copia válida; no hay que reiniciar los kioscos.

---

## Datos en disco

Bajo `DataDir` (`data/` por defecto):

- `KioskConfig.json` — contenido compartido servido a todos los kioscos (`ServerConfigStore`).
- `events.json` — eventos programados / overrides temporales (`EventStore`).
- `panel.json` — hash de la contraseña del panel (`PanelAuthStore`).
- `fleet.json` + `fleet-activity.json` — overrides y registro de actividad de la flota (`FleetRegistry`).
- `installers.json` + `install-jobs.json` — catálogo privado y últimos 500 trabajos de instalación remota de la flota.
- `pack-definition-v3.json` — definición vigente del pack WinGet, independiente de las versiones resueltas en cada equipo.
- `winget-index.json` — índice importado para buscar y añadir aplicaciones desde el panel.
- `pack-applications.json` — proyección de compatibilidad para asistentes v2; la migración conserva `pack-applications.json.before-v3.bak`.
- `setup-installations.json` — últimas 500 sesiones del Setup antiguo de binarios privados; no es el registro del asistente WinGet actual.
- `maintenance-jobs.json` — últimos 500 trabajos de mantenimiento/desinstalación y su resultado.
- `kiosk-updates.json` — releases de Kiosk, versión objetivo, ventana y trabajos de actualización.
- `deployment-v1.json` — estaciones, perfiles y trabajos de instalación por red; recuperación y migraciones en [su guía](INSTALACION-POR-RED.md).

Bajo `AssetsDir` (`assets/` por defecto): `Brands/` y `SpecImages/` con las imágenes normalizadas de la
biblioteca, más el marcador interno que evita resembrar imágenes borradas deliberadamente.

Bajo `InstallersDir` (`installers/` por defecto): binarios MSI/EXE privados, con nombres internos aleatorios.

Bajo `SetupDir` (`setups/` por defecto): el `Setup-EquipoClinicaPC-*.exe` interno y su
`*.bundle.json` anteriores permanecen como recuperación. Las publicaciones nuevas están en
`v3/releases/<versión>/` y sus componentes por hash en `v3/components/{kiosk,worker}/`.
`v3/active.json` conserva el puntero activo y anterior; se sustituye atómicamente tras
comprobar ambas ediciones y componentes. Antes de activar v3 se ofrece el asistente
compatible anterior. La publicación no activa descargas. Publicación y validación:
[Asistente de equipos](ASISTENTE-EQUIPOS.md).

Bajo `UpdatesDir` (`updates/` por defecto): paquetes de actualización inmutables importados por CI. Incluye esta
carpeta en las copias de seguridad junto con `data/`, `assets/` e `installers/`.

Son ficheros JSON planos, sin base de datos. Para una copia de seguridad guarda `data/`, `assets/`,
`installers/`, `setups/` y `updates/`. El servidor conserva las diez releases más recientes (además de
cualquier versión activa o todavía en instalación).

<!-- SHOT (opcional): el panel con la carpeta de datos al lado, o un diagrama de despliegue VPS + kioscos -->

---

## Organización del panel

El menú agrupa **Resumen** y **Ordenadores** en Operación; **Instalador**,
**Aplicaciones** e **Instalación de sistemas** en Preparación; **Contenido**,
**Imágenes y marcas** y **Eventos** en Contenido; y **Actualizaciones** y
**Seguridad** en Sistema. **Instaladores de la flota** se abre desde Ordenadores:
sus paquetes privados y trabajos se dirigen a kioscos registrados, mientras que
Aplicaciones configura el pack WinGet para preparar equipos. Instalador reúne
las descargas del asistente de equipos y de la instalación por red.

## Flota e instalación de aplicaciones

La vista **Ordenadores** recibe heartbeats reales por `/hub/fleet` y permite órdenes dirigidas. La página
**Instaladores de la flota** (`/ordenadores/instalaciones`) mantiene un catálogo privado: sube un MSI o un
EXE compatible (Inno Setup, NSIS o Adobe Reader), selecciona equipos online y observa descarga,
verificación, instalación y resultado. **Aplicaciones** (`/aplicaciones`) configura el
[pack WinGet del asistente](PACK-WINGET.md).

El instalador del kiosko registra `KioskClinicaPCInstallerAgent` como servicio `LocalSystem`. La app sin
privilegios solo le entrega un ID/token por named pipe; el agente vuelve al servidor, verifica tamaño,
SHA-256 y Authenticode, y utiliza parámetros silenciosos fijos. Los EXE desconocidos, scripts y argumentos
arbitrarios se rechazan. Un resultado `3010/1641` queda como **reinicio pendiente** y nunca reinicia solo.

Desde **Ordenadores**, **Desinstalar Kiosk** crea un trabajo auditable y exige escribir el nombre exacto del
equipo. Solo se habilita si el ordenador está online y su agente anuncia esta capacidad. El agente valida la
identidad del proceso que llama, localiza el desinstalador Inno registrado en Windows y lanza un ejecutor
temporal fuera de la carpeta de la aplicación. Al terminar comprueba la entrada de Programas, el ejecutable,
el servicio y la tarea programada antes de marcar el equipo como **desinstalado**. También elimina la
configuración del usuario que ejecutaba Kiosk. Si falla, el equipo no se retira de la flota y el panel conserva
el error; no se interpreta como éxito el mero hecho de haber iniciado `unins*.exe`.

En **Instaladores de la flota**, **Retirar del catálogo** solo impide nuevos despliegues y archiva sus metadatos. No intenta
desinstalar esa aplicación en los ordenadores donde ya estuviera instalada. Esta separación evita confundir
la gestión del catálogo del servidor con una desinstalación remota del software de terceros.

En el primer arranque con `ServerUrl`, el agente empareja ese origen HTTPS en
`HKLM\SOFTWARE\ClinicaPC\Kiosk\InstallerServerUrl`; además, el pipe solo acepta al `KioskClinicaPC.exe`
instalado junto al agente. Si se migra el panel a otro dominio, un administrador debe detener el servicio,
borrar ese valor y arrancar de nuevo el kiosko para realizar un nuevo emparejamiento.

### Instalador inicial de equipos

La página **Aplicaciones** configura identificadores, orden y preselección del pack WinGet. Cada equipo
resuelve la última versión compatible y la fija para su ejecución. El asistente permite instalar Kiosk,
el pack o ambos; sus resultados y diagnósticos se conservan en el equipo. Las sesiones del Setup antiguo
de binarios privados no describen este flujo.

La página **Instalador** ofrece las ediciones Online y Completo de `Setup-EquipoClinicaPC-*`. Online
descarga los componentes seleccionados; Completo incorpora trabajador y Kiosk para USB. Las aplicaciones
del pack requieren conexión en ambas. Uso, caché, reanudación y diagnóstico están en
[Asistente de equipos](ASISTENTE-EQUIPOS.md).

La clave limitada `Kiosk__InitialSetupKey` debe ser distinta de la API de la flota y de la clave de
publicación. El workflow privado **Equipment Setup** construye ambas ediciones y las importa como
candidatas; la activación se realiza desde el panel siguiendo el
[procedimiento de publicación](ASISTENTE-EQUIPOS.md#publicación-activación-y-recuperación).
Actualizar la VPS no reconstruye ni activa el asistente. Rotar la clave de Setup invalida las consultas
al catálogo y las descargas de componentes de asistentes que llevan la clave anterior.

`Setup-KioskClinicaPC-*` sigue siendo el componente interno del asistente y el
ejecutable de las actualizaciones administradas. La instalación inicial se
descarga únicamente como asistente desde `/instalador`. La provisión se explica
en [Apuntar los kioscos al servidor](#apuntar-los-kioscos-al-servidor).

### Actualizaciones de Kiosk

GitHub Actions compila y firma un manifiesto, guarda los archivos firmados en
un artefacto cifrado del workflow durante 90 días y los importa en la VPS. El
cifrado usa la clave de publicación ya configurada; no se sube el EXE en claro. La
release aparece en **Actualizaciones** y no afecta a la flota hasta pulsar **Activar en toda la flota**. Por
defecto se instala entre las 04:00 y las 05:00, con hasta diez minutos de dispersión por equipo.

Las releases son inmutables: el pipeline nunca sobrescribe los archivos de una
versión existente. Si la VPS estaba temporalmente caída después de guardar el
artefacto del workflow, ejecuta **Retry VPS release import** indicando `X.Y.Z`
y el ID del run original de **Release Kiosk**. Descifrará ese mismo artefacto
firmado y reintentará solo la importación, sin recompilar ni cambiar hashes.

La firma usa ECDSA P-256. Genera el par una sola vez en una máquina segura:

```bash
openssl ecparam -name prime256v1 -genkey -noout -out kiosk-update-private.pem
openssl ec -in kiosk-update-private.pem -pubout -out kiosk-update-public.pem
```

- Guarda la clave privada únicamente como `KIOSK_UPDATE_SIGNING_PRIVATE_KEY` en el environment protegido
  `production` de GitHub.
- Guarda la pública como `KIOSK_UPDATE_SIGNING_PUBLIC_KEY` y cópiala a
  `/etc/kiosk-server/update-keys/production.pem`.
- Configura la variable GitHub `KIOSK_UPDATE_SIGNING_KEY_ID=production`. Los workflows usan `https://panel.clinicapc.es` para publicar e importar releases.
- Configura el mismo secreto `KIOSK_RELEASE_PUBLISH_KEY` en GitHub y en la VPS.
- Configura el secreto GitHub `KIOSK_SERVER_API_KEY` en el entorno `production` con el mismo valor que `Kiosk__ApiKey` de la VPS. El workflow falla si falta; no guardes la clave en Git.

Para publicar desde un commit verificado, elige una versión nueva y crea un tag `vX.Y.Z`. El workflow
usa esa versión para construir el instalador. Sustituye el marcador antes de ejecutar:

```powershell
$kioskVersion = 'X.Y.Z'
git tag "v$kioskVersion"
git push origin "v$kioskVersion"
```

Los kioscos antiguos con el actualizador de GitHub requieren una migración
manual o mediante una release puente anterior. Al arrancar, la versión actual rellena
`ServerUrl` y `ServerApiKey` **solo si ambos estaban vacíos**; conserva identidad y
ajustes locales. La ventana de renovación de contraseña se ha eliminado: el acceso
a Ajustes de kioskos conectados valida la clave vigente del panel y guarda el
verificador tras acertar para el uso sin conexión. Comprueba en
**Ordenadores** que aparecen antes de gestionar sus siguientes
actualizaciones desde **Actualizaciones**. Un equipo sin actualización administrada
puede instalar la versión nueva encima desde el asistente de `/instalador`, sin
desinstalar la anterior.

---

## Despliegue en Ubuntu 26.04 con Caddy

Esta sección prepara la VPS por primera vez. Para actualizar una instalación
existente desde Windows, usa `deploy-server-vps.ps1` como se explica en
[ACTUALIZAR-PANEL-VPS.txt](ACTUALIZAR-PANEL-VPS.txt). Ese proceso conserva la
versión anterior y la restaura si falla la comprobación de salud.

El bundle es framework-dependent y requiere el runtime ASP.NET Core 10. En Ubuntu 26.04 está disponible
directamente en el repositorio oficial de Ubuntu:

```bash
sudo apt update
sudo apt install -y aspnetcore-runtime-10.0
dotnet --list-runtimes
```

Crea el usuario de servicio y los directorios persistentes:

```bash
sudo useradd --system --home /var/lib/kiosk-server --create-home --shell /usr/sbin/nologin kiosk-server
sudo install -d -o root -g root -m 755 /opt/kiosk-server/app
sudo install -d -o kiosk-server -g kiosk-server -m 750 /var/lib/kiosk-server/data
sudo install -d -o kiosk-server -g kiosk-server -m 750 /var/lib/kiosk-server/assets
sudo install -d -o kiosk-server -g kiosk-server -m 750 /var/lib/kiosk-server/installers
sudo install -d -o kiosk-server -g kiosk-server -m 750 /var/lib/kiosk-server/setups
sudo install -d -o kiosk-server -g kiosk-server -m 750 /var/lib/kiosk-server/updates
sudo install -d -o root -g root -m 700 /etc/kiosk-server
sudo install -d -o root -g kiosk-server -m 750 /etc/kiosk-server/update-keys
```

Sube por SFTP el contenido del paquete a `/home/ubuntu/kiosk-server-upload/` y colócalo en su ruta definitiva:

```bash
sudo cp -a /home/ubuntu/kiosk-server-upload/. /opt/kiosk-server/app/
sudo chown -R root:root /opt/kiosk-server/app
sudo cp /opt/kiosk-server/app/deploy/kiosk-server.service /etc/systemd/system/kiosk-server.service
sudo cp /opt/kiosk-server/app/deploy/kiosk-server.env.example /etc/kiosk-server/kiosk-server.env
sudo chmod 600 /etc/kiosk-server/kiosk-server.env
sudo nano /etc/kiosk-server/kiosk-server.env
```

Genera secretos **distintos** para API de la flota, Setup, publicación y tokens de actualización con
`openssl rand -hex 32`; define además una contraseña inicial del panel. Guárdalos en un gestor de
contraseñas y sustituye los marcadores del fichero. `AllowedHosts` incluye el dominio público, el host
técnico y loopback para la sonda local. Arranca:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now kiosk-server
sudo systemctl status kiosk-server --no-pager
curl --fail http://127.0.0.1:5080/health
```

La sonda debe devolver `{"status":"ok"}`. Si no, revisa `sudo journalctl -u kiosk-server -n 100 --no-pager`.

Instala Caddy desde su repositorio oficial. La plantilla `deploy/ubuntu/Caddyfile.example` se incluye en el bundle como `deploy/Caddyfile.example`; cópiala a `/etc/caddy/Caddyfile` y valida antes de recargar. Sirve `panel.clinicapc.es` y el host técnico. Caddy obtiene y renueva HTTPS automáticamente; `reverse_proxy` soporta WebSockets.

```bash
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo chmod o+r /usr/share/keyrings/caddy-stable-archive-keyring.gpg /etc/apt/sources.list.d/caddy-stable.list
sudo apt update
sudo apt install -y caddy
sudo cp /opt/kiosk-server/app/deploy/Caddyfile.example /etc/caddy/Caddyfile
sudo nano /etc/caddy/Caddyfile
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

Antes de pedir el certificado, el registro DNS `A` del dominio debe apuntar a la IPv4 de la VPS y los puertos
TCP 80/443 deben estar permitidos. Kestrel solo escucha en `127.0.0.1:5080`; no abras ese puerto al exterior.
