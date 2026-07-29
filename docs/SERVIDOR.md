# Servidor de contenido y panel (opcional)

El kiosko funciona **solo**, sin nada más. Este servidor es la capa **para varias máquinas**: un único sitio
que edita el contenido compartido de toda la tienda y sincroniza el bucle de atracción. Si no lo despliegas,
cada PC sigue siendo autónoma (modo local puro).

- **Proyecto:** `src/Kiosk.Server` (ASP.NET Core, net8.0). Todo el cableado está en `Program.cs`.
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

Para desplegar, genera el bundle publicable y cópialo al host (mini-PC de la trastienda o un VPS barato):

```
dotnet publish src/Kiosk.Server -c Release -o publish
```

El host solo necesita el **runtime** de ASP.NET Core 8 (no el SDK). Arranca con `dotnet Kiosk.Server.dll`.
En Linux conviene ponerlo como servicio (`systemd`) o en un contenedor; en Windows, como servicio o tarea.

Comprobación de vida (sin auth): `GET /health` → `{"status":"ok"}`.

---

## Configuración

Se lee de `appsettings.json` o, mejor para producción, de **variables de entorno** `Kiosk__*` (doble guion
bajo). Todas las claves cuelgan de la sección `Kiosk`:

| Clave | Env var | Para qué | Por defecto |
|---|---|---|---|
| `ApiKey` | `Kiosk__ApiKey` | Protege **`/api/*`**. Los kioscos la mandan en la cabecera `X-Api-Key`. **Vacía = API abierta** (solo pruebas; avisa en el arranque). | vacía |
| `DataDir` | `Kiosk__DataDir` | Carpeta de los JSON de datos. | `data/` bajo el ContentRoot |
| `AssetsDir` | `Kiosk__AssetsDir` | Carpeta de la biblioteca de imágenes. | `assets/` bajo el ContentRoot |
| `SlideDurationMs` | `Kiosk__SlideDurationMs` | Duración de cada slide del attract. **Debe coincidir con el default del cliente (5200).** | `5200` |
| `TimeZone` | `Kiosk__TimeZone` | Zona horaria de **la tienda** (no la del VPS) para evaluar la vigencia de los eventos. Id de Windows (p.ej. `Romance Standard Time`) o IANA en Linux (`Europe/Madrid`). Si no resuelve, cae a la hora local del servidor y **avisa en el log**. | zona local del servidor |

Ejemplo `appsettings.json`:

```json
{
  "Kiosk": {
    "ApiKey": "una-clave-larga-y-secreta",
    "SlideDurationMs": 5200,
    "TimeZone": "Romance Standard Time"
  }
}
```

> ⚠️ Antes de exponer el servidor a internet, **fija una `ApiKey`**. Con la clave vacía cualquiera puede leer
> `/api/config`. El arranque lo avisa a voces en el log, no lo descubras en producción.

---

## HTTPS y proxy inverso

El navegador guarda la cookie de sesión del panel; sírvelo por **HTTPS**. En un VPS lo habitual es un proxy
inverso (Nginx / Caddy / IIS) delante de Kestrel, que termina TLS (Let's Encrypt) y reenvía a la app. El panel
y el hub (SignalR, WebSockets) van por el mismo host; asegúrate de que el proxy permite **WebSockets**.

---

## Primer arranque

1. **Contraseña del panel.** Se guarda hasheada en `data/panel.json` (`PanelAuthStore`). Cámbiala desde el
   panel en **Seguridad**. El login está limitado por intentos (`LoginThrottle`): demasiados fallos seguidos
   desde una IP la bloquean un rato.
2. **Contenido inicial.** El servidor sirve `data/KioskConfig.json` como contenido compartido. Edítalo desde
   el panel (no a mano); el formato es el mismo `KioskConfig.json` que ya conoce el cliente.
3. **Imágenes.** Sube logos de marca y de componentes desde **Assets**. Solo mapas de bits
   (`.png/.jpg/.jpeg/.webp/.gif`); **el SVG está prohibido** a propósito (XSS almacenado en la vista previa).

---

## Apuntar los kioscos al servidor

En cada kiosko: **Ajustes → servidor** (o lo rellena el instalador del cliente). Escribe:

- `ServerUrl` — la URL pública del servidor (p.ej. `https://panel.mitienda.com`).
- `ServerApiKey` — la misma `ApiKey` del servidor.

Se guardan en `KioskSettings.json` del cliente. Con `ServerUrl` **vacío**, el kiosko vuelve al modo local puro.
Al configurar servidor, el cliente pasa el contenido compartido a **solo lectura** (el modo edición libre y el
editor de slides de Ajustes se desactivan; el precio y las specs siguen editándose por-máquina).

---

## Endpoints

| Ruta | Auth | Qué hace |
|---|---|---|
| `GET /health` | — | Sonda de vida. |
| `GET /api/config` | `X-Api-Key` | Contenido **efectivo** (base + evento vigente) que consumen los kioscos. |
| `GET /api/config/version` | `X-Api-Key` | Hash de versión; el cliente lo sondea para detectar cambios. |
| `GET /api/assets/{ruta}` | `X-Api-Key` | Imágenes de la biblioteca (con guardia anti-traversal). |
| `POST /login` · `POST /logout` | cookie | Sesión del panel (con antiforgery + throttle). |
| `GET /panel/assets/{cat}/{fichero}` | cookie | Vista previa de imágenes dentro del panel. |
| `/hub/sync` (SignalR) | — | Reloj maestro del attract (`SyncState`) + aviso `ContentChanged`. Sin datos sensibles. |
| `/` y páginas del panel | cookie | Panel Blazor Server; sin sesión redirige a `/login`. |

Cómo llegan los cambios del panel a los kioscos: al guardar, el panel emite `ContentChanged` por el hub
(**push** inmediato); como red de seguridad, cada cliente sondea `/api/config/version` cada 90 s. No hay que
reiniciar los kioscos.

---

## Datos en disco

Bajo `DataDir` (`data/` por defecto):

- `KioskConfig.json` — contenido compartido servido a todos los kioscos (`ServerConfigStore`).
- `events.json` — eventos programados / overrides temporales (`EventStore`).
- `panel.json` — hash de la contraseña del panel (`PanelAuthStore`).
- `fleet.json` + `fleet-activity.json` — overrides y registro de actividad de la flota (`FleetRegistry`).

Bajo `AssetsDir` (`assets/` por defecto): `Brands/` y `SpecImages/` con las imágenes de la biblioteca.

Son ficheros JSON planos, sin base de datos. Para una copia de seguridad basta con guardar `data/` y `assets/`.

<!-- SHOT (opcional): el panel con la carpeta de datos al lado, o un diagrama de despliegue VPS + kioscos -->

---

## Panel de flota — en construcción

El panel tiene una vista de **Flota** (estado de cada kiosko, qué muestra, hardware, precio) y la lógica de
`FleetRegistry` (alta por heartbeat, cola de órdenes: reiniciar / apagar / relanzar app / fijar precio /
renombrar en `Sync/FleetCommand.cs`). **Pero el canal que conecta los kioscos con esas órdenes aún no está
cableado**: hoy los datos de la vista son de muestra y los cambios de precio/nombre viven solo en memoria. Es
una vista de monitorización; el propio panel lo indica. No lo trates como control remoto funcional todavía.
