(() => {
  document.addEventListener("click", event => {
    const link = event.target.closest("[data-setup-download]");
    if (!link) return;
    if (link.dataset.busy === "true") { event.preventDefault(); return; }
    if (!window.crypto?.randomUUID) return; // El enlace normal sigue funcionando.

    event.preventDefault();
    const id = window.crypto.randomUUID().replaceAll("-", "");
    const url = new URL(link.href, window.location.href);
    url.searchParams.set("downloadId", id);
    const label = link.querySelector("[data-setup-download-label]");
    const status = link.parentElement.querySelector("[data-setup-download-status]");
    document.cookie = "kioskSetupDownload=; Max-Age=0; Path=/instalador; SameSite=Strict";
    link.dataset.busy = "true";
    link.classList.add("is-loading");
    link.setAttribute("aria-disabled", "true");
    label.textContent = "Preparando descarga…";
    status.textContent = "Comprobando el instalador antes de enviarlo.";
    status.hidden = false;

    let poll;
    let timeout;
    const finish = message => {
      clearInterval(poll);
      clearTimeout(timeout);
      link.dataset.busy = "false";
      link.classList.remove("is-loading");
      link.removeAttribute("aria-disabled");
      label.textContent = "Descargar instalador";
      status.textContent = message;
    };
    poll = setInterval(() => {
      if (!link.isConnected) { clearInterval(poll); clearTimeout(timeout); return; }
      const marker = document.cookie.split("; ").find(cookie => cookie.startsWith("kioskSetupDownload="));
      if (marker?.slice("kioskSetupDownload=".length) !== id) return;
      document.cookie = "kioskSetupDownload=; Max-Age=0; Path=/instalador; SameSite=Strict";
      finish("Descarga iniciada. Revisa las descargas del navegador.");
    }, 250);
    timeout = setTimeout(() => finish("La preparación está tardando. Puedes volver a intentarlo."), 60000);
    window.location.assign(url.href);
  });

  document.addEventListener("change", event => {
    const input = event.target.closest("[data-installer-file]");
    if (!input) return;
    const name = input.files && input.files[0] ? input.files[0].name : "Ningún archivo seleccionado";
    input.closest("form")?.querySelector("[data-selected-file]")?.replaceChildren(name);
  });

  document.addEventListener("submit", event => {
    const form = event.target.closest("form[data-installer-upload]");
    if (!form || !window.XMLHttpRequest) return;
    event.preventDefault();

    const submit = form.querySelector(".upload-submit");
    const label = form.querySelector("[data-upload-label]");
    const progress = form.querySelector("[data-upload-progress]");
    const bar = form.querySelector("[data-upload-bar]");
    const status = form.querySelector("[data-upload-status]");
    const error = form.querySelector("[data-upload-error]");
    submit.disabled = true;
    label.textContent = "Subiendo…";
    progress.hidden = false;
    error.hidden = true;
    bar.style.width = "0%";

    const xhr = new XMLHttpRequest();
    xhr.open("POST", form.action);
    xhr.setRequestHeader("Accept", "application/json");
    xhr.upload.addEventListener("progress", e => {
      if (!e.lengthComputable) return;
      const percent = Math.round((e.loaded / e.total) * 100);
      bar.style.width = percent + "%";
      status.textContent = percent < 100 ? "Subiendo… " + percent + "%" : "Verificando instalador…";
    });
    xhr.addEventListener("load", () => {
      let response;
      try { response = JSON.parse(xhr.responseText); } catch { response = null; }
      if (xhr.status >= 200 && xhr.status < 300 && response?.ok) {
        window.location.assign("/aplicaciones?ok=" + encodeURIComponent(response.message));
        return;
      }
      submit.disabled = false;
      label.textContent = "Reintentar subida";
      progress.hidden = true;
      error.textContent = response?.message || "No se pudo subir el instalador.";
      error.hidden = false;
    });
    xhr.addEventListener("error", () => {
      submit.disabled = false;
      label.textContent = "Reintentar subida";
      progress.hidden = true;
      error.textContent = "Se perdió la conexión durante la subida. Puedes volver a intentarlo.";
      error.hidden = false;
    });
    xhr.send(new FormData(form));
  });
})();
