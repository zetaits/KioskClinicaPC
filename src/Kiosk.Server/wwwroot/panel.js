(() => {
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
