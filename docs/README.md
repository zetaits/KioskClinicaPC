# Web de ficha PDF (destino del QR)

Aplicación web estática que se abre al escanear el QR de un kiosko. En producción se publica junto al servidor en:

`https://vps-9c7061ff.vps.ovh.net/ficha/`

Las especificaciones viajan dentro del `#hash` de la URL. El servidor solo entrega HTML, CSS, JavaScript y fuentes: el fragmento no forma parte de la petición HTTP, no se registra en el proxy y no se almacena en el servidor. El PDF se genera en el propio móvil.

## Despliegue

`Kiosk.Server.csproj` enlaza esta carpeta en `wwwroot/ficha` y copia también las fuentes y dependencias locales. Al publicar el servidor se obtiene un único bundle autocontenido:

```powershell
& "C:\Users\zits\.dotnet\dotnet.exe" publish src\Kiosk.Server -c Release
```

Después del despliegue verifica:

- `GET /health` responde `{"status":"ok"}`.
- `GET /ficha/` carga sin autenticación.
- La consola del navegador no intenta acceder a CDNs ni a Google Fonts.

El cliente construye el destino mediante `FichaPdfUrl`: usa `/ficha/` en el mismo origen HTTPS configurado como `ServerUrl` y conserva como respaldo la URL pública de la VPS.

## Compatibilidad con QR antiguos

GitHub Pages se mantiene únicamente como puente. `legacy-redirect.js` detecta `zetaits.github.io` y redirige a la VPS copiando íntegro el fragmento. No desactives Pages ni cambies ese script mientras puedan quedar QRs antiguos impresos.

La fuente sigue siendo esta carpeta `docs/`; no hay dos versiones de la ficha que mantener.

## Archivos

- `index.html` — estructura de la ficha y carga de recursos relativos.
- `styles.css` — diseño de la hoja A4 y fuentes locales.
- `app.js` — decodificación, presentación y exportación a PDF.
- `legacy-redirect.js` — compatibilidad del dominio anterior.
- `vendor/` — copias versionadas de `pako` y `html2pdf`, con sus licencias.

## Formato del payload

Los payloads actuales usan `Base64Url(deflateRaw(JSON))`; `app.js` sigue aceptando `Base64Url(gzip(JSON))` para QR antiguos. El JSON usa claves cortas (véase `Core/EquipmentPayload.cs`):

```json
{ "v":1, "ch":"marca", "mo":"modelo", "fa":"familia", "sk":"sku",
  "pr":"precio", "dp":"precioRebajado", "sh":"tienda", "ad":"direccion",
  "c":[ { "i":"id", "l":"etiqueta", "v":"valor", "d":"detalle", "t":"tecnico" } ] }
```

## Personalización

- Encabezado: reemplaza `logo.png`.
- Tema y diseño de impresión: `styles.css`.
- Explicaciones de componentes: objeto `FRIENDLY` de `app.js`.
- Datos de contacto predeterminados: objeto `SHOP` de `app.js`.

Todos los recursos necesarios están alojados en la propia VPS; el móvil necesita conexión para abrir la página, pero no depende de servicios de terceros.
