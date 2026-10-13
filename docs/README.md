# Documentación de KioskClinicaPC

Las guías de esta carpeta describen el código del repositorio. La versión
publicada y la activa se consultan en el panel; un build local o un informe de
pruebas no demuestra que esos cambios estén desplegados.

## Guías vigentes

| Necesidad | Documento principal |
| --- | --- |
| Configurar y administrar el servidor, sus API y datos | [Servidor](SERVIDOR.md) |
| Actualizar la VPS y recuperar un despliegue fallido | [Actualizar el panel](ACTUALIZAR-PANEL-VPS.txt) |
| Gestionar las aplicaciones del pack en el panel | [Pack WinGet](PACK-WINGET.md) |
| Usar, compilar, publicar y diagnosticar el asistente | [Asistente de equipos](ASISTENTE-EQUIPOS.md) |
| Preparar el piloto de instalación Windows por red | [Instalación por red](INSTALACION-POR-RED.md) |
| Mantener la página que abre el QR y genera el PDF | [Ficha PDF](FICHA-PDF.md) |
| Compilar y trabajar en el repositorio | [AGENTS.md](../AGENTS.md) y [README del proyecto](../README.md) |

La instalación remota de MSI/EXE en kioscos registrados se gestiona en
`/ordenadores/instalaciones`. El pack WinGet para preparar equipos se gestiona
en `/aplicaciones`; son funciones independientes.

## Estado de las verificaciones

La última verificación local del asistente documentada es
[1.5.9](historico/VERIFICACION-ASISTENTE-1.5.9.md): builds ficticios, pruebas
automatizadas y auditoría de solo lectura. La instalación completa en VMs
limpias queda pendiente. No se afirma aquí que esa candidata esté publicada.

La instalación por red conserva su estado de piloto pendiente en su
[guía](INSTALACION-POR-RED.md). La publicación exige las evidencias de validación
que especifica ese procedimiento.

Los planes y resultados de versiones anteriores están en el
[índice histórico](historico/README.md). Sus versiones, comandos y rutas se
conservan como contexto de aquellas tareas.

## Mantenimiento

- Al cambiar un comportamiento, actualizar su guía principal en el mismo cambio.
- Enlazar el procedimiento principal desde otras guías, en lugar de copiarlo.
- Guardar resultados y notas de una versión en `historico/`, con fecha, alcance,
  pruebas realizadas y pendientes. Las guías explican el comportamiento actual.
- Usar una versión elegida para los ejemplos de publicación; los valores por
  defecto pertenecen a los scripts y proyectos de compilación.
- Comprobar enlaces y referencias después de mover documentos.

## Archivos web en esta carpeta

`index.html`, `app.js`, `styles.css`, `legacy-redirect.js`, `logo.png` y `vendor/`
son recursos de la ficha QR, publicados por `Kiosk.Server` en `/ficha/` y usados
por la compatibilidad de GitHub Pages. Sus rutas están referenciadas por la
compilación. La reorganización de manuales no requiere mover estos recursos.
