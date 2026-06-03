# Web de ficha PDF (destino del QR)

Página estática que el cliente abre al **escanear el QR** del kiosko. Las specs del equipo viajan
**dentro del propio QR** (en el `#hash` de la URL), así que:

- No hay backend, base de datos ni almacenamiento.
- El kiosko **no necesita internet** en el momento del escaneo.
- Los datos del equipo **nunca se envían al servidor** (el `#hash` no forma parte de la petición HTTP).
- Cada equipo genera un QR distinto automáticamente (sus specs son distintas).

El PDF se genera **en el móvil del cliente** (con sus datos móviles), con `html2pdf.js`.

## Archivos

- `index.html` — estructura de la ficha.
- `styles.css` — estilo de la hoja A4.
- `app.js` — decodifica el `#hash`, pinta la ficha y exporta el PDF.

## Despliegue con GitHub Pages (gratis, HTTPS)

Esta carpeta es `docs/` precisamente para que GitHub Pages la sirva:

1. Repo en GitHub → **Settings → Pages**.
2. **Source: Deploy from a branch** → rama `master` → carpeta **`/docs`** → **Save**.
3. Espera ~1 min. URL resultante: `https://<usuario>.github.io/<repo>/`
   (p. ej. `https://zetaits.github.io/KioskClinicaPC/`).
4. En el kiosko: **Ajustes → Código QR · ficha PDF → URL BASE** = esa URL (con `/` final).

> Funciona bajo subruta `/<repo>/` porque las rutas de `index.html` son relativas.
> Pages sirve HTTPS por defecto (los móviles lo prefieren para descargar archivos).
> Para tu propio dominio: Settings → Pages → Custom domain.

## Formato del payload (referencia)

`#hash` = `Base64Url( gzip( JSON ) )`. JSON con claves cortas (ver `Core/EquipmentPayload.cs` en el kiosko):

```
{ "v":1, "ch":marca, "mo":modelo, "fa":familia, "sk":sku,
  "pr":precio, "dp":precioRebajado, "sh":tienda, "ad":direccion,
  "c":[ { "i":id, "l":etiqueta, "v":valor, "d":detalle, "t":tecnico }, ... ] }
```

## Personalización

- Marca del encabezado: se toma de la tienda (`sh`) si viene; si no, el valor por defecto en `index.html` (`CLINICAPC`).
- Textos "qué es" por componente: editar el objeto `FRIENDLY` en `app.js`.
- Las librerías (`pako`, `html2pdf`) se cargan por CDN; para uso 100% offline, descárgalas y sírvelas localmente.
