# Verificación local de la carga progresiva de Instalador

Fecha: 13 de octubre de 2026.

**Estado de implementación:** completada en el repositorio local.
**Estado de validación:** compilación y pruebas automatizadas locales superadas;
revisión visual y navegación real en navegador pendientes.
**Estado de publicación:** paquete Linux Release preparado y auditado; no
desplegado en la VPS durante esta tarea.

## Cambio implementado

El catálogo de la pantalla lee metadatos y atributos de archivos. Las operaciones
de publicación, activación y descarga conservan su verificación completa. Los DTO
de presentación son distintos de los modelos usados para servir paquetes.

La página utiliza streaming y estados persistidos entre prerenderizado e
interacción. Equipos e instalación por red cargan independientemente, con errores
y reintentos propios. El historial se consulta al primer clic en su encabezado y
se reutiliza durante la visita. Se cancelan las consultas al destruir componentes.

La descarga de instalación por red acepta una versión explícita para evitar
sustituir la publicación mostrada. Las llamadas sin versión mantienen el contrato
anterior. No hay migraciones de datos ni cambios en los paquetes publicados.

Procedimiento vigente: [guía del asistente](../ASISTENTE-EQUIPOS.md).

## Validación automatizada

- Build de `src/Kiosk.Server/Kiosk.Server.csproj` con el SDK local .NET 10.
- Suite completa `tests/Kiosk.Server.Tests` en Debug y Release: **328 pruebas
  superadas en cada configuración**, ninguna
  fallida ni omitida. Incluye 22 casos nuevos de metadatos, streaming, componentes
  y selección explícita de versión en la descarga por red.
- Prueba HTTP con consultas controladas: la estructura llega mientras ambas
  consultas están pendientes, la sección de equipos se actualiza antes de terminar
  la de red y el historial no se consulta durante la carga inicial.
- Pruebas de componentes con bUnit 2.11.3, dependencia exclusiva del proyecto de
  pruebas: clics repetidos, historial diferido, estados, reintentos, orden final de
  secciones y cancelación al destruir el componente.
- Pruebas de lectura ligera con binarios bloqueados para lectura y manifiestos
  antiguos bloqueados. La consulta inicial puede completarse sin abrirlos.
- Corrupción con tamaño conservado: visible en el catálogo ligero, rechazada por
  verificación y activación. La descarga por red de una versión dañada se rechaza;
  la descarga sin versión conserva la búsqueda de la última válida.
- Puntero activo dañado o sin publicación, JSON inválido, tamaños incorrectos,
  archivos ausentes, nombre con traversal y cancelación.
- Regresión de autenticación, antiforgery, candidatas, ediciones, compatibilidad
  histórica, readiness, descargas parciales y marcador de descarga.

## Medición local

Comparación de la consulta anterior (`ActiveVersion`, `List` y `Latest` de red)
con las dos consultas nuevas. Fixture ficticio de cuatro publicaciones, con
16 MiB Online, 64 MiB Completo y un componente Kiosk de 16 MiB por publicación.
La ruta anterior recorre aproximadamente 384 MiB por consulta. Los archivos
compartidos se almacenan una sola vez; no se ejecuta ningún instalador.

Ambas rutas se precalentaron y se alternaron diez mediciones en el mismo equipo:

| Consulta | Mediana | Mínimo | Máximo |
| --- | ---: | ---: | ---: |
| Anterior, con validación completa | 281,89 ms | 277,83 ms | 377,17 ms |
| Nueva, con metadatos | 4,22 ms | 2,96 ms | 56,85 ms |

Esta medición compara el trabajo del servidor con datos ficticios. No mide el
tiempo hasta el primer botón en un navegador ni demuestra el rendimiento de la
VPS. El fixture y su ejecutor se generaron como artefactos temporales bajo `obj/`.

## Pendiente antes de dar por validada la publicación

La sesión de control de navegador no expuso ningún navegador disponible; el
intento de abrir Chrome devolvió `Browser is not available: chrome`. Por ello no
se afirma revisión visual en escritorio o móvil, interacción con `<details>` en
un navegador real ni validación de la hidratación y la navegación interna reales.

Al disponer de navegador: comprobar pantalla cargada, esqueleto con conexión lenta,
historial, reintento, navegación interna, recarga directa, ausencia de saltos
molestos y preferencias de movimiento reducido. Medir el tiempo hasta la primera
estructura visible y hasta el primer botón disponible con los mismos datos.

Tras una publicación autorizada por el procedimiento habitual: comprobar
`/health/ready`, apertura autenticada y llegada progresiva del contenido a través
de Caddy. El procedimiento de publicación está en
[Actualizar el panel](../ACTUALIZAR-PANEL-VPS.txt).

## Paquete preparado para despliegue

Generado con `& .\deploy-server-vps.ps1 -PackageOnly`, sin conectar a la VPS.

- Release: `20261013172612-d83bddcf8e0d5199`.
- Archivo local: `src/Kiosk.Server/bin/Release/vps-releases/kiosk-server-20261013172612-d83bddcf8e0d5199.tar.gz`.
- Tamaño: 169,02 MiB; 192 entradas auditadas.
- SHA-256 del paquete: `609bc3cc96bb0c7933a73420a49b2291c7fc9a8a7d7b2125d4b189d1fddf747b`.
- SHA-256 del servidor: `d83bddcf8e0d5199de2c487fe9ef2aa67a5af6857d055af2d1dc674aeec2685f`.

La auditoría confirmó el manifiesto y hash de la DLL, la presencia del servicio
nuevo y estilos del esqueleto, los recursos de la ficha QR y las rutas de estado
bajo `/var/lib/kiosk-server`. No contiene directorios de estado, enlaces, archivos
`.env`, dependencias de pruebas ni credenciales del panel/API en `appsettings.json`.
El manifiesto registra `sourceDirty=true` porque se generó con los cambios locales
sin commit; durante esa preparación no se creó un commit ni se publicó el paquete.

Para desplegar desde la raíz del repositorio, usar el procedimiento vigente:
`& .\deploy-server-vps.ps1`, seguido de la comprobación de `/health/ready`.
El comando normal realiza el preflight remoto y genera una release nueva antes
de subirla; su identificador y hash de paquete pueden diferir de los de esta
preparación. Las comprobaciones de navegador y proxy anteriores siguen pendientes.
