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

La sustitución de la ventana de renovación por la contraseña vigente del panel
tiene una [verificación local del 15 de octubre de 2026](historico/VERIFICACION-CONTRASENA-PANEL-2026-10-15.md).
El servidor y los kioskos siguen pendientes de publicación.

El desfase entre la URL sin versión de Chrome Enterprise y su manifiesto WinGet tiene
una [corrección local documentada](historico/VERIFICACION-CHROME-PACK-2026-10-14.md).
La instalación integral y publicación de un nuevo asistente están pendientes;
el EXE 1.5.12 no incorpora esta corrección.

La retención del historial del asistente conserva cinco versiones en total,
con prioridad para la activa y la anterior de recuperación. Tiene un
[informe local](historico/VERIFICACION-RETENCION-ASISTENTE-2026-10-13.md).
Se aplica al desplegar el servidor actualizado; no modifica los EXE ya publicados.

La carga progresiva de `/instalador` tiene un
[informe local del 13 de octubre de 2026](historico/VERIFICACION-CARGA-INSTALADOR-2026-10-13.md).
Incluye pruebas del catálogo, streaming e interacción de componentes, y un paquete
Linux Release preparado con 328 pruebas superadas en Release. La revisión
visual en navegador y la comprobación tras publicar en la VPS están pendientes.

La cola concurrente del pack tiene un
[informe local del 13 de octubre de 2026](historico/VERIFICACION-COLA-PACK-2026-10-13.md):
151 pruebas relevantes del servidor y 77 del asistente verificadas, con compilación
de la solución. El asistente local 1.5.10 se generó y ejecutó solo con Pack;
tras reiniciar el PC, instaló cinco aplicaciones y verificó las seis en unos
2 minutos y 32 segundos, con solapamiento de instalación y verificación.
El aviso de reinicio omitido por el resumen quedó corregido en la
[preparación de 1.5.11](historico/VERIFICACION-ASISTENTE-1.5.11.md): 621 pruebas
Release superadas, ambos EXE locales diagnosticados y auditoría real de solo
lectura con seis verificadas y reinicio pendiente detectado. Los EXE locales son
de validación ficticia; el workflow genera los de producción con sus credenciales.
La candidata de producción 1.5.11 se importó al panel el 13 de octubre con Kiosk
1.2.2. La prueba de instalación limpia y la activación siguen sin confirmar;
el benchmark en Windows 10/11 está pendiente.

La verificación de la candidata
[1.5.9](historico/VERIFICACION-ASISTENTE-1.5.9.md) recoge builds ficticios, pruebas
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
