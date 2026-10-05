# Servidor de contenido y panel (opcional)

El kiosko funciona **solo**, sin nada más. Este servidor es la capa **para varias máquinas**: un único sitio
que edita el contenido compartido de toda la tienda y sincroniza el bucle de atracción. Si no lo despliegas,
cada PC sigue siendo autónoma (modo local puro).

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

```
dotnet run --project src/Kiosk.Server
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
| `ApiKey` | `Kiosk__ApiKey` | Protege **`/api/*`**. Los kioscos la mandan en la cabecera `X-Api-Key`. Es obligatoria fuera de Development. | vacía solo en Development |
| `InitialSetupKey` | `Kiosk__InitialSetupKey` | Clave de 64 hex separada y limitada al catálogo/descarga del pack durante la instalación inicial. Vacía deshabilita esa función. | vacía |
| `PanelInitialPassword` | `Kiosk__PanelInitialPassword` | Contraseña del panel que se hashea al crear `panel.json`. Mínimo 12 caracteres; después puede quitarse del entorno. | sin valor; el primer arranque falla de forma segura |
| `DataDir` | `Kiosk__DataDir` | Carpeta de los JSON de datos. | `data/` bajo el ContentRoot |
| `AssetsDir` | `Kiosk__AssetsDir` | Carpeta de la biblioteca de imágenes. | `assets/` bajo el ContentRoot |
| `InstallersDir` | `Kiosk__InstallersDir` | Binarios privados del catálogo de aplicaciones. | `installers/` bajo el ContentRoot |
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

Para generar un instalador ya provisionado, sin pedir estos datos al encargado de la tienda:

```powershell
$env:KIOSK_SERVER_API_KEY = '<clave de 64 caracteres>'
.\build-installer.ps1 -ServerUrl 'https://panel.clinicapc.es'
Remove-Item Env:KIOSK_SERVER_API_KEY
```

La clave se pasa solo durante la compilación y no se guarda en el repositorio. El Setup deja la provisión junto
al ejecutable y verifica URL + API key contra `/api/config/version`. Al abrirse bajo el usuario correcto, el
kiosko la copia a su `KioskSettings.json` solo si el perfil es nuevo. Una actualización conserva siempre la
contraseña, identidad y ajustes existentes, incluso si el UAC se aprobó con otra cuenta administradora.

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
| `POST /api/releases` | `X-Release-Publish-Key` | Importa desde CI un instalador y su manifiesto firmado; no lo activa. |
| `GET /api/updates/assignment` | `X-Api-Key` | Devuelve al kiosco su versión objetivo, ventana y token de trabajo. |
| `GET /api/updates/{id}/manifest` | API key + token de actualización | Manifiesto firmado ligado al trabajo. |
| `GET /api/updates/{id}/download` | API key + token de actualización | Descarga reanudable del instalador desde la VPS. |
| `GET /api/updates/{id}/authorize` | API key + token de actualización | Revalida justo antes de instalar que la release y la ventana siguen autorizadas. |
| `POST /api/updates/{id}/status` | API key + token de actualización | Progreso, error y confirmación del runner privilegiado. |
| `GET /api/installations/{id}/manifest` | API key + token de trabajo | Manifiesto inmutable ligado al equipo y paquete. |
| `GET /api/installations/{id}/download` | API key + token de trabajo | Descarga privada con soporte de rangos. |
| `POST /api/installations/{id}/status` | API key + token de trabajo | Progreso y resultado del agente. |
| `GET /api/setup/catalog` | clave de Setup | Aplicaciones permitidas y selección predeterminada del instalador inicial. |
| `POST /api/setup/sessions` | clave de Setup | Crea una sesión auditable y fija los paquetes seleccionados. |
| `GET /api/setup/sessions/{id}/packages/{packageId}/download` | clave + token de sesión | Descarga privada y reanudable para el Setup. |
| `POST /api/setup/sessions/{id}/packages/{packageId}/status` | clave + token de sesión | Resultado de cada aplicación del pack. |
| `GET /panel/setup/download` | cookie | Descarga el último Setup interno cuyo tamaño y SHA-256 sean válidos. |
| `POST /api/maintenance/{id}/status` | API key + token de mantenimiento | Resultado verificado de una autodesinstalación de Kiosk. |
| `POST /panel/installers/upload` | cookie + antiforgery | Añade un MSI/Inno/NSIS al catálogo. |
| `POST /login` · `POST /logout` | cookie | Sesión del panel (con antiforgery + throttle). |
| `GET /panel/assets/{cat}/{fichero}` | cookie | Vista previa de imágenes dentro del panel. |
| `/hub/sync` (SignalR) | — | Reloj maestro del attract (`SyncState`) + aviso `ContentChanged`. Sin datos sensibles. |
| `/` y páginas del panel | cookie | Panel Blazor Server; sin sesión redirige a `/login`. |

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
- `installers.json` + `install-jobs.json` — catálogo y últimos 500 trabajos de instalación.
- `setup-installations.json` — últimas 500 sesiones de instalación inicial y sus resultados.
- `maintenance-jobs.json` — últimos 500 trabajos de mantenimiento/desinstalación y su resultado.
- `kiosk-updates.json` — releases de Kiosk, versión objetivo, ventana y trabajos de actualización.

Bajo `AssetsDir` (`assets/` por defecto): `Brands/` y `SpecImages/` con las imágenes normalizadas de la
biblioteca, más el marcador interno que evita resembrar imágenes borradas deliberadamente.

Bajo `InstallersDir` (`installers/` por defecto): binarios MSI/EXE privados, con nombres internos aleatorios.

Bajo `SetupDir` (`setups/` por defecto): el `Setup-EquipoClinicaPC-*.exe` interno y su
`*.bundle.json`. El panel selecciona la versión válida más alta compatible con WPF y catálogo v2 y nunca sirve un binario cuyo hash no coincida. Conserva los artefactos antiguos sin ofrecerlos como alternativa. Publicación y validación: [Asistente de equipos](ASISTENTE-EQUIPOS.md).

Bajo `UpdatesDir` (`updates/` por defecto): instaladores públicos inmutables importados por CI. Incluye esta
carpeta en las copias de seguridad junto con `data/`, `assets/` e `installers/`.

Son ficheros JSON planos, sin base de datos. Para una copia de seguridad guarda `data/`, `assets/`,
`installers/`, `setups/` y `updates/`. El servidor conserva las diez releases más recientes (además de
cualquier versión activa o todavía en instalación).

<!-- SHOT (opcional): el panel con la carpeta de datos al lado, o un diagrama de despliegue VPS + kioscos -->

---

## Flota e instalación de aplicaciones

La vista **Ordenadores** recibe heartbeats reales por `/hub/fleet` y permite órdenes dirigidas. La página
**Aplicaciones** mantiene un catálogo vacío y administrable: sube MSI, Inno Setup o NSIS, selecciona uno o
varios equipos online y observa descarga, verificación, instalación y resultado.

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

En **Aplicaciones**, **Retirar del catálogo** solo impide nuevos despliegues y archiva sus metadatos. No intenta
desinstalar esa aplicación en los ordenadores donde ya estuviera instalada. Esta separación evita confundir
la gestión del catálogo del servidor con una desinstalación remota del software de terceros.

### Instalador inicial de equipos

Cada aplicación del catálogo puede marcarse como **visible en el Setup**, **preseleccionada** y recibir un
orden. La página **Instalador** permite descargar el Setup interno y consultar el resultado por equipo y
aplicación. El asistente ofrece Kiosk y el pack por separado; descarga e instala el pack secuencialmente con
los mismos controles de tamaño, SHA-256, firma y argumentos silenciosos que el agente remoto.

Genera una clave distinta de la API principal, configúrala como `Kiosk__InitialSetupKey` y usa la misma solo
durante el build interno:

```powershell
$env:KIOSK_SERVER_API_KEY = '<api key de 64 hex>'
.\build-installer.ps1 -ServerUrl 'https://panel.clinicapc.es'
Remove-Item Env:KIOSK_SERVER_API_KEY
```

Este build genera `Setup-KioskClinicaPC-*`, que se publica para auto-update y contiene
la `ApiKey` de la flota para conectar kioscos nuevos y anteriores sin servidor; cualquiera que
descargue la release puede extraerla. El asistente WPF `Setup-EquipoClinicaPC-*` se genera y publica por separado con el workflow manual **Equipment Setup** y añade la clave limitada del pack. Se importa exclusivamente por `/api/releases/setup`, nunca al release público. Actualizar la VPS no reconstruye esta descarga. Rotar la clave del pack
invalida su descarga en Setups internos antiguos, aunque estos siguen pudiendo instalar Kiosk.

En el primer arranque con `ServerUrl`, el agente empareja ese origen HTTPS en
`HKLM\SOFTWARE\ClinicaPC\Kiosk\InstallerServerUrl`; además, el pipe solo acepta al `KioskClinicaPC.exe`
instalado junto al agente. Si se migra el panel a otro dominio, un administrador debe detener el servicio,
borrar ese valor y arrancar de nuevo el kiosko para realizar un nuevo emparejamiento.

### Actualizaciones de Kiosk

GitHub Actions compila y firma un manifiesto, publica la release como respaldo y la importa en la VPS. La
release aparece en **Actualizaciones** y no afecta a la flota hasta pulsar **Activar en toda la flota**. Por
defecto se instala entre las 04:00 y las 05:00, con hasta diez minutos de dispersión por equipo.

Las releases son inmutables: el pipeline nunca sobrescribe assets de una versión existente. Si GitHub llega
a publicar correctamente pero la VPS estaba temporalmente caída, ejecuta manualmente la acción
**Retry VPS release import** indicando `X.Y.Z`; descargará esos mismos assets firmados y reintentará solo la
importación, sin recompilar ni cambiar hashes.

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

Para publicar, crea y sube un tag que coincida exactamente con la versión de los proyectos:

```bash
git tag v1.2.0
git push origin v1.2.0
```

`v1.2.0` es la release puente. Los kioscos antiguos con el actualizador de GitHub descargan
el instalador público y lo aplican con su tarea programada. Al arrancar, la nueva versión rellena
`ServerUrl` y `ServerApiKey` **solo si ambos estaban vacíos**; conserva contraseña, identidad y
ajustes locales. Los perfiles que aún usan la política de contraseña antigua deben renovarla
en el primer arranque; el kiosco se registra después de completar ese paso. Comprueba en
**Ordenadores** que aparecen antes de gestionar sus siguientes
actualizaciones desde **Actualizaciones**. Un equipo sin el actualizador antiguo o sin acceso a
GitHub necesita instalar la versión nueva encima manualmente, sin desinstalar la anterior.

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

Genera dos secretos **distintos** con `openssl rand -hex 32`, guárdalos en un gestor de contraseñas y
sustituye los marcadores del fichero. `AllowedHosts` incluye el dominio público, el host técnico y loopback para la sonda local. Arranca:

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
