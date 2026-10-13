# Pack WinGet para preparar equipos

Esta guía describe la gestión del pack actual en el panel. La instalación,
diagnóstico y publicación se explican en el [asistente de equipos](ASISTENTE-EQUIPOS.md).

## Uso cotidiano

1. Abrir `/aplicaciones`. La lista **Aplicaciones del pack** muestra identificador
   WinGet, orden y selección inicial. Los cambios se guardan automáticamente.
2. Usar **Añadir aplicaciones al pack** para buscar en el índice oficial. Se puede
   añadir una aplicación elegible; aparece en la misma lista y queda seleccionada
   por defecto.
3. Ajustar selección y orden. Los números menores entran primero en la cola;
   WinGet puede instalar antes una aplicación cuya descarga ya haya terminado.
   **Quitar del pack** no desinstala nada en los equipos.
4. En el PC de destino, descargar el asistente desde `/instalador`, elegir
   componentes y revisar la selección antes de instalar.

El panel muestra **Última versión compatible al instalar**. La versión observada
en el índice es orientativa; cada equipo consulta WinGet al comenzar y resuelve
su versión compatible. No hay que fijar versiones manualmente ni pulsar
Actualizar versiones del pack para los asistentes actuales.

Si una aplicación desaparece o deja de ser elegible en el índice, se conserva
en la definición y se comprueba de nuevo en el equipo. Las aplicaciones no
disponibles pueden quedar pendientes mientras se instalan las comprobadas.

## Definición y política de instalación

El asistente usa `/api/setup/v3/catalog` con `X-Setup-Key` y exige
`X-Setup-Catalog-Version: 3`. Al confirmar valida de nuevo la revisión y los IDs.
La versión resuelta queda congelada después de la comprobación previa. WinGet
solapa las descargas y mantiene una instalación activa; el asistente puede
verificar la anterior mientras se instala otra. Un fallo de verificación posterior
a un éxito nativo confirmado deja esa aplicación pendiente y permite continuar
las restantes. El pack solo termina correctamente si todas quedan verificadas.

Solo admite paquetes del origen oficial `winget` con instalador compatible,
silencioso y para todo el equipo. Se rechazan instaladores por usuario,
MSStore, MSIX, portable, autenticación interactiva y ámbitos/versiones instaladas
desconocidos. No se infieren argumentos ejecutando EXE arbitrarios.

Una versión igual o superior ya instalada para todo el equipo se omite. Un fallo
de red no autoriza descender silenciosamente a versiones antiguas. La instalación
parcial no permite continuar con un instalador activo o de resultado incierto.
Los límites, preparación de WinGet y reanudación están en la
[política del asistente](ASISTENTE-EQUIPOS.md#aplicaciones-y-reanudación).

El pack por sí solo no registra equipos ni envía estados o logs al panel.
Si se instala Kiosk, su cliente y servicios mantienen el funcionamiento habitual
de la flota.

## Índice oficial y preparación del servidor

Configurar `Kiosk__InitialSetupKey` en el servidor y el mismo secreto de 64 hex
como `KIOSK_INITIAL_SETUP_KEY` en el entorno `production` de GitHub. Las credenciales
de publicación son independientes; su configuración se documenta en
[Servidor](SERVIDOR.md#configuración).

El workflow **Official WinGet catalogue** se ejecuta diariamente y admite ejecución
manual para la primera importación. Windows exporta mediante la API COM y
contrasta los manifiestos oficiales; el servidor recibe el JSON y conserva el
último índice válido si la exportación/importación falla.

Ante un fallo, revisar **Export structured catalogue** y el artifact
`winget-index-diagnostics`: stdout, stderr y fases con HRESULT. Ejecutar **Run workflow**
sobre el código actualizado; **Re-run jobs** reutiliza el código de la ejecución
anterior. Una exportación incompleta no debe sustituir el índice anterior.

El exportador aísla errores definitivos por paquete y reintenta hasta tres veces
los errores transitorios. Autenticación, incompatibilidad COM y cancelación
detienen la exportación. Rechaza más del 1% o 100 fallos de lectura y exige al
menos 1000 entradas válidas, 100 elegibles y ausencia de IDs duplicados.

Para distribuir el asistente, seguir únicamente el
[procedimiento de publicación](ASISTENTE-EQUIPOS.md#publicación-activación-y-recuperación).
Actualizar la VPS no reconstruye ni activa su descarga.

## Persistencia y compatibilidad

En `DataDir`, `pack-definition-v3.json` conserva identificadores, orden, selección y
revisión. `winget-index.json` guarda el índice del panel. `pack-applications.json`
es una proyección con versiones concretas para los asistentes v2 antiguos.

La migración conserva IDs, orden y selección y crea
`pack-applications.json.before-v3.bak`. Una renovación del índice puede cambiar
la proyección v2 sin cambiar la revisión de la definición v3. Los asistentes
antiguos conservan su política de versiones fijas.

Los MSI/EXE privados y los trabajos remotos se gestionan en
`/ordenadores/instalaciones`, desde **Ordenadores → Instaladores de la flota**.
No forman parte del pack WinGet. Los datos anteriores se conservan y los enlaces
antiguos `/aplicaciones?device=...` redirigen a esa página.

El [inventario local de binarios](../instaladores/README.md) es una comprobación
histórica, no la definición del pack. Las publicaciones del asistente se guardan
bajo `SetupDir`; su estructura está en [Servidor](SERVIDOR.md#datos-en-disco).
