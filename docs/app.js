/*
 * Ficha técnica del equipo — generación de PDF en el navegador del cliente.
 *
 * Flujo: el kiosko codifica las specs del equipo en el #hash de la URL (JSON → gzip → Base64Url).
 * Aquí hacemos el camino inverso, pintamos la ficha y la exportamos a PDF con html2pdf.
 * No hay servidor: los datos nunca salen del dispositivo (el #hash no se envía en la petición HTTP).
 */

// Texto "qué es" por componente (estático). Enriquece el PDF sin engordar el QR.
// Si el id no está aquí, simplemente no se muestra explicación.
const FRIENDLY = {
  cpu: "El cerebro del equipo: cuanto más potente, más fluido va todo.",
  gpu: "La tarjeta gráfica: juegos, edición de vídeo y aceleración de IA.",
  ram: "La memoria de trabajo: más RAM = más cosas abiertas a la vez sin tirones.",
  storage: "El disco: dónde se guarda todo. SSD = arranque y cargas casi instantáneos.",
  screen: "La pantalla: resolución y fluidez de imagen.",
  battery: "La batería: autonomía sin enchufe.",
  wifi: "Conectividad inalámbrica: WiFi y Bluetooth.",
  camera: "La cámara para videollamadas.",
  ports: "Las conexiones físicas disponibles.",
  os: "El sistema operativo incluido."
};

const ORDER = ["cpu", "gpu", "ram", "storage", "screen", "battery", "wifi", "camera", "ports", "os"];

function setStatus(msg, isError) {
  const el = document.getElementById("status");
  el.textContent = msg;
  el.classList.toggle("error", !!isError);
  el.hidden = false;
}

function base64UrlToBytes(b64url) {
  let b64 = b64url.replace(/-/g, "+").replace(/_/g, "/");
  while (b64.length % 4) b64 += "=";
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function decodePayload() {
  const hash = (location.hash || "").replace(/^#/, "");
  if (!hash) throw new Error("Esta página debe abrirse escaneando el código QR del equipo.");
  const bytes = base64UrlToBytes(hash);
  const json = pako.ungzip(bytes, { to: "string" });
  return JSON.parse(json);
}

function fmtPrice(raw) {
  if (raw == null || raw === "") return null;
  const n = Number(String(raw).replace(",", "."));
  if (!isFinite(n)) return String(raw);
  return new Intl.NumberFormat("es-ES", { style: "currency", currency: "EUR", maximumFractionDigits: 0 }).format(n);
}

function escapeHtml(s) {
  return String(s == null ? "" : s)
    .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
}

function render(data) {
  // Identidad
  const title = [data.ch, data.mo].filter(Boolean).join(" ") || "Equipo";
  document.getElementById("title").textContent = title;
  const sub = [data.fa, data.sk ? "SKU " + data.sk : null].filter(Boolean).join(" · ");
  document.getElementById("subtitle").textContent = sub;

  // Precio
  const now = fmtPrice(data.dp) || fmtPrice(data.pr);
  if (now) {
    document.getElementById("priceBlock").hidden = false;
    document.getElementById("priceNow").textContent = now;
    if (data.dp && data.pr) {
      document.getElementById("priceOld").textContent = fmtPrice(data.pr);
      const p = Number(String(data.pr).replace(",", ".")), d = Number(String(data.dp).replace(",", "."));
      if (isFinite(p) && isFinite(d) && p > 0) {
        document.getElementById("priceDisc").textContent = "-" + Math.round((1 - d / p) * 100) + "%";
      }
    }
  }

  // Componentes (orden canónico; lo no listado va al final en su orden recibido)
  const comps = Array.isArray(data.c) ? data.c.slice() : [];
  comps.sort((a, b) => {
    const ia = ORDER.indexOf(a.i), ib = ORDER.indexOf(b.i);
    return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
  });

  const tbody = document.querySelector("#compTable tbody");
  tbody.innerHTML = comps.map(c => {
    const explain = FRIENDLY[c.i] ? `<div class="explain">${escapeHtml(FRIENDLY[c.i])}</div>` : "";
    const detail = c.d ? `<div class="detail">${escapeHtml(c.d)}</div>` : "";
    return `<tr>
      <td class="c-label">${escapeHtml(c.l || c.i || "")}</td>
      <td class="c-value">
        <div class="value">${escapeHtml(c.v || "")}</div>
        ${detail}${explain}
      </td>
    </tr>`;
  }).join("");

  // Footer
  document.getElementById("shopline").textContent =
    [data.sh, data.ad].filter(Boolean).join(" · ");
  document.getElementById("generated").textContent =
    "Generado el " + new Date().toLocaleDateString("es-ES") + " · specs reales detectadas en tienda";

  document.getElementById("status").hidden = true;
  document.getElementById("sheet").hidden = false;
  document.getElementById("actions").hidden = false;

  return title;
}

function downloadPdf(title) {
  const safe = (title || "equipo").replace(/[^\w\-]+/g, "_").slice(0, 60);
  const opt = {
    margin: 0,
    filename: `ficha_${safe}.pdf`,
    image: { type: "jpeg", quality: 0.98 },
    // Fondo oscuro de marca (si no, html2canvas pintaría blanco bajo las esquinas redondeadas).
    html2canvas: { scale: 2, useCORS: true, backgroundColor: "#04020a" },
    jsPDF: { unit: "mm", format: "a4", orientation: "portrait" }
  };
  return html2pdf().set(opt).from(document.getElementById("sheet")).save();
}

// Espera a que las fuentes y el logo estén listos para que el PDF no salga sin estilo.
function waitForAssets() {
  const fontsReady = (document.fonts && document.fonts.ready) ? document.fonts.ready : Promise.resolve();
  const logo = document.querySelector(".logo");
  const logoReady = (logo && !logo.complete)
    ? new Promise(res => { logo.onload = logo.onerror = res; })
    : Promise.resolve();
  return Promise.all([fontsReady, logoReady]);
}

(async function main() {
  let title;
  try {
    const data = decodePayload();
    title = render(data);
  } catch (err) {
    setStatus(err.message || "No se pudo leer la ficha del equipo.", true);
    return;
  }

  document.getElementById("downloadBtn").addEventListener("click", () => downloadPdf(title));

  // Descarga automática al abrir, una vez cargadas fuentes y logo.
  await waitForAssets();
  setTimeout(() => { downloadPdf(title).catch(() => {}); }, 200);
})();
