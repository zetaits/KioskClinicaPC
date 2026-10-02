/* Compatibilidad indefinida para QR antiguos ya impresos. El fragmento contiene la ficha y
 * el navegador no lo envía a GitHub ni a la VPS como parte de la petición HTTP. */
if (location.hostname === "zetaits.github.io") {
  location.replace("https://panel.clinicapc.es/ficha/" + location.hash);
}
