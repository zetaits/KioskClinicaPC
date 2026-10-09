# Asistente WPF para preparar equipos

La corrección preparada para 1.5.6 incluye en el trabajador la DLL nativa
`Microsoft.Management.Deployment.dll`. Su ausencia en 1.5.5 provocaba
`COMException 80040154` antes de comprobar las aplicaciones, aunque WinGet
estuviera instalado. La compilación y la validación del ZIP ahora exigen esa DLL
además del WinMD. Es necesario generar y publicar un asistente nuevo para aplicar
la corrección a las descargas del panel.

El comando `KioskSetupHelper.exe --diagnose-winget-json` comprueba la activación
de la API y sus opciones sin reparar WinGet, actualizar el catálogo ni instalar
aplicaciones. Debe ejecutarse desde la carpeta completa del trabajador, conservando
la DLL nativa y el WinMD junto al EXE. Devuelve código 0 y `wingetActivated: true`
si puede activar WinGet 1.29.380 o superior; en caso contrario muestra tipo y HRESULT.

La candidata 1.5.5 incluye Kiosk 1.2.2 y corrige el error al abrir la ventana
principal después de guardar el precio: los colores de respaldo de los bindings
usan recursos estáticos válidos para WPF. Los colores enlazados al componente
siguen actualizándose normalmente. El precio y los ajustes locales se conservan.
La prueba de carga de la ventana y sus plantillas se ejecuta sin mostrarla ni
activar protecciones, servicios de red o detección real de hardware.

`Setup-EquipoClinicaPC-1.5.4.exe` es una descarga privada del panel autenticado
`/instalador`. Su versión es independiente de la de Kiosk incluida. No se publica
en GitHub Releases ni como artifact público. Contiene credenciales limitadas de
aprovisionamiento; no contiene claves privadas de firma ni claves de publicación.

La edición **Online** descarga únicamente los componentes seleccionados. La edición
**Completo para USB**, `Setup-EquipoClinicaPC-1.5.4-Completo.exe`, incluye los mismos
componentes y permite instalar solo Kiosk sin conexión. El pack de aplicaciones
necesita internet en ambas. WPF y .NET viajan en el EXE: no hay que instalar .NET.

## Uso

1. Descargar desde `/instalador` y abrir el EXE. Aparece WPF antes de extraer los
   componentes grandes. Pack está activado y Kiosk desactivado inicialmente.
2. Elegir solo pack, solo Kiosk o ambos. Solo Kiosk omite catálogo y WinGet.
3. Para el pack, revisar nombres, política de última versión compatible y selección inicial del panel.
   Un pack vacío muestra un aviso y permite volver a componentes.
4. Confirmar la selección y la opción de reanudación. Al pulsar **Instalar**,
   Windows solicita una elevación del trabajador. Se comprueba de nuevo la revisión
   y los identificadores contra `/api/setup/v3/catalog` con `X-Setup-Key` y el encabezado
   `X-Setup-Catalog-Version: 3`. Si cambió la definición, se vuelve a revisión antes de preparar
   WinGet o ejecutar instaladores. No se consulta el catálogo privado de ejecutables.
   La opción **Instalar las aplicaciones disponibles aunque alguna falle la comprobación**
   permite omitir las que fallen y continuar con las demás. Está marcada inicialmente.
   Si falla la comprobación y hay aplicaciones disponibles, el resultado también ofrece
   **Instalar aplicaciones disponibles**, que repite la comprobación antes de continuar.
5. Se preparan y verifican primero todos los componentes seleccionados. Después, el orden es comprobación de todas las aplicaciones, Kiosk y pack. Inno actúa
   únicamente como motor silencioso de Kiosk. El frontend registra el autostart
   mediante `--register-autostart-only` bajo el usuario original, incluso si UAC
   usó otra cuenta administradora. Al finalizar correctamente, **Abrir Kiosk al finalizar**
   aparece marcado y el botón muestra **Finalizar y abrir Kiosk**. Se puede desmarcar
   para cerrar sin abrirlo. Si se eligieron ambos componentes, esta opción solo aparece
   cuando termina también el pack y sale el trabajador. No se ofrece ante cancelaciones,
   resultados incompletos o reinicios pendientes. Los reinicios son manuales.

Desde el asistente 1.5.3 con Kiosk 1.2.1, los perfiles sin contraseña local usan
automáticamente la contraseña del panel vigente al generar el EXE. El asistente
privado contiene su verificador PBKDF2 y lo instala como `KioskPanelPassword.json`;
no contiene la contraseña en claro. Kiosk puede validarla sin conexión y omite
el diálogo inicial. Las contraseñas locales existentes se conservan, incluso
las de perfiles anteriores. Se puede cambiar la contraseña del equipo desde
Ajustes, indicando la actual, sin cambiar la del panel. Cambiar la contraseña
del panel posteriormente no actualiza perfiles ni asistentes ya generados.

El verificador se obtiene durante la publicación desde
`GET /api/releases/setup/kiosk-password`, exclusivamente con
`X-Release-Publish-Key` y sin caché. Ni la clave API pública, ni `X-Setup-Key`,
ni la sesión administrativa habilitan esa ruta. No se incluye en el instalador
público de Kiosk ni en el manifiesto de publicación. El diagnóstico del asistente
solo muestra `panelPasswordProvisioned`, nunca el verificador. Si no se encuentra
un aprovisionamiento válido, el cliente conserva el diálogo de contraseña local.
El pack solo no instala el archivo. Los logs de errores detallan fase, porcentaje,
causa interna y código de error, sin copiar mensajes que puedan contener claves.

Los comandos para desplegar y generar esta versión están en
[PUBLICAR-ASISTENTE-1.5.3.md](PUBLICAR-ASISTENTE-1.5.3.md).

La interfaz permanece abierta al cancelar una instalación: espera al trabajador.
No mata instaladores nativos ni borra sus archivos. Un resultado ambiguo detiene
la cola y exige comprobación o reinicio antes de reintentar. Una app con fallo
ordinario permite intentar las siguientes. Solo se declara éxito si todos los
componentes elegidos están verificados y el autostart de Kiosk se ha confirmado
para el usuario original.

Las aplicaciones omitidas conservan su error y el resultado es parcial (código 2).
La opción no permite continuar si hay un instalador activo o de estado incierto.
Si todas fallan la comprobación ordinaria, no se instalan aplicaciones, pero Kiosk
seleccionado puede instalarse. El modo desatendido también continúa con las disponibles.

Cada ejecución actualiza el origen oficial WinGet y ordena sus versiones con el
comparador de WinGet dentro del canal predeterminado. Elige la más reciente que
tenga instalador aplicable, silencioso, sin autenticación y para todo el equipo.
Comprueba el manifiesto oficial; conserva hash obligatorio, acuerdos y dependencias.
La versión del índice del panel es orientativa y nunca fija la instalación nueva.
Un fallo de red no permite usar silenciosamente el catálogo local como actualizado
ni descender a versiones antiguas. Una discrepancia entre catálogo y manifiesto
permite como máximo una actualización adicional por ejecución. No se repiten
instaladores ni se cambia la versión resuelta después de la comprobación previa.

El resultado muestra versión resuelta, versión instalada cuando puede verificarse
y motivo de error. Al reanudar se vuelve a verificar lo instalado, conservando su
versión resuelta; solo los pendientes vuelven a resolver la última compatible.
Una versión superior ya instalada para todo el equipo no se degrada.

El servidor migra automáticamente la configuración a `pack-definition-v3.json`,
preservando IDs, orden, selección y revisión, con copia `pack-applications.json.before-v3.bak`.
El índice diario actualiza una proyección concreta `/api/setup/v2/catalog` para los
asistentes antiguos sin cambiar la revisión v3. Estos conservan su política estricta;
para obtener toda la recuperación automática es necesario descargar el EXE 1.5.4 nuevo.

La ventana usa los colores y la tipografía Space Grotesk del panel, controles
propios y el icono de Clínica PC en el EXE y la barra de tareas. La barra superior
personalizada permite arrastrar, redimensionar, minimizar, maximizar/restaurar
y cerrar; el cierre conserva la espera segura del trabajador. Los controles
incluyen estados de foco por teclado, hover y deshabilitado. Las listas se
desplazan y los textos se ajustan al ancho disponible.

Estado: `%ProgramData%\ClinicaPC\Setup\last-run.json`, `kiosk-run.json` y
`logs/`. Los directorios `work/` son exclusivos de administradores y SYSTEM.
Los errores de la ventana y del registro de autostart se guardan además en
`%LOCALAPPDATA%\KioskClinicaPC\Setup\logs\assistant-AAAAMMDD.log`, sin copiar
mensajes de excepción que puedan contener credenciales. El asistente 1.5.4
corrige un resultado parcial falso al cerrar el canal después de una instalación
correcta: cada mensaje se vacía mientras el canal está conectado y su cierre
normal no invalida el resultado ya recibido. Se siguen exigiendo el resultado
final y un código de salida del trabajador coincidente.
El trabajador conserva el bloqueo `run.lock` durante comprobación, Kiosk y pack;
`equipment.lock` coordina instancias del nuevo asistente. Los recursos solo se
extraen si se eligieron, a rutas internas, y se verifica SHA-256. WinGet y su
`Microsoft.Management.Deployment.winmd` quedan físicamente juntos, aislados del
frontend WPF. Se conservan el exportador `export-index` y los comandos antiguos
del helper para compatibilidad.

El trabajador se publica desde `Kiosk.PackWorker`, sin WPF, con nombre físico
`KioskSetupHelper.exe`, DLL, runtimeconfig y WinMD. `Kiosk.WinGetCore` comparte
motor, bootstrap, protocolo, persistencia y exportación con el helper WPF antiguo.
El origen local `Payload.UseDirectory` utilizado por instalación por red se conserva
y prepara sus archivos locales sin depender de las descargas de componentes de la VPS.

La caché `%ProgramData%\ClinicaPC\Setup\cache` solo permite escritura a
administradores/SYSTEM y rechaza reparse points en archivos y antecesores. Cada uso
verifica tamaño y SHA-256. Las descargas se escriben en `.part`; solo se promueven
tras validar. Se reanudan con el ETag esperado y Range correcto; una respuesta 200
reinicia el parcial. Un hash incorrecto elimina el parcial. Hay tres intentos para
fallos transitorios, dos minutos sin progreso y treinta minutos por componente.
Se muestra progreso y se permite cancelar también desde la consola silenciosa.
La limpieza limita la caché a 1 GiB, retira primero archivos antiguos disponibles y
parciales abandonados de más de siete días; no toca estados, registros ni archivos
bloqueados por operaciones activas.

## Compilación local ficticia

Desde la raíz, con .NET 10 SDK e Inno Setup 6 disponibles:

```powershell
& .\build-equipment-setup.ps1
```

Detecta el SDK compatible siguiendo `global.json` y los candidatos del usuario
y de `%TEMP%\clinicapc-dotnet`. Genera `installer\Output\Setup-EquipoClinicaPC-1.5.4.exe`
, `Setup-EquipoClinicaPC-1.5.4-Completo.exe` y `.bundle.json` esquema 3. Usa `https://setup.invalid` y claves ficticias aunque haya
credenciales reales en el entorno. No incorpora el verificador del panel: el
primer arranque mantiene el diálogo de contraseña local en este build ficticio.
No instala nada. El build verifica el EXE
final en modo diagnóstico, comprobando versión, compatibilidad y hashes.

Los artefactos internos son inmutables: si ya existe esa versión local, elegir
otra con `-Version 1.5.5` o retirar manualmente **solo** el build ficticio anterior.
`-KioskVersion 1.2.1` fija la versión incluida sin crear tags ni releases de Kiosk.
El build genera ese payload desde el código del checkout; no descarga una release
histórica por su número. Publicar siempre desde un commit que se haya probado.

Diagnóstico sin instalación:

```powershell
& .\installer\Output\Setup-EquipoClinicaPC-1.5.4.exe --diagnose
# Para capturar JSON, ejecutar --diagnose-json con stdout redirigido.
```

El diagnóstico muestra únicamente versiones, commit, compatibilidad y estado
de recursos; no muestra claves ni configuración incrustada. Devuelve 0 si todo
es compatible. Online comprueba manifiesto/configuración y ausencia de binarios incrustados, sin descargarlos ni afirmar sus hashes verificados. Completo verifica realmente SHA-256 y compatibilidad de ambos recursos.

## Modo completamente silencioso

Exige una consola **ya elevada**, bajo un usuario interactivo, nunca SYSTEM.
No abre WPF ni solicita UAC adicional. No lanza la pantalla fullscreen de Kiosk.

```powershell
& .\Setup-EquipoClinicaPC-1.5.4.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack
& .\Setup-EquipoClinicaPC-1.5.4.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=kiosk
& .\Setup-EquipoClinicaPC-1.5.4.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack,kiosk /RESUME
```

Sin `/COMPONENTS`, usa solo pack; selecciona las apps marcadas inicialmente en
el panel. `/RESUME` conserva el estado anterior, pero todas las apps se comprueban
otra vez. Una aplicación verificada no se reinstala y Kiosk correctamente
instalado se omite. No se aceptan URLs, comandos, rutas ni argumentos de fabricante.

| Código | Significado |
| --- | --- |
| 0 | Todos los componentes verificados |
| 1 | Fallo antes de instalar, permisos insuficientes o incompatibilidad |
| 2 | Incompleto, cancelado o sin verificación final |
| 64 | Argumentos no válidos |

## Publicación candidata, activación y recuperación

1. Probar y guardar el código en un commit. Desplegar primero el servidor mediante
   `deploy-server-vps.ps1` según `ACTUALIZAR-PANEL-VPS.txt`. `/health/ready` debe devolver
   200, `{"status":"ok"}`, `X-Setup-Catalog-Version: 3` y `X-Setup-Component-Protocol: 1`.
   El despliegue del servidor no reconstruye ni activa asistentes.
2. Ejecutar el workflow **Equipment Setup**, versión `1.5.4`, con Kiosk `1.2.1`, desde el commit probado.
   `/health/ready` debe anunciar también `X-Kiosk-Password-Provisioning: 1`.
   Conserva los secretos de `production` y la separación de `X-Release-Publish-Key`
   para publicar y `X-Setup-Key` para descargar. No publicar estos EXE como assets
   públicos: contienen aprovisionamiento limitado. No necesitan la clave privada de firma.
3. El build construye Kiosk y el trabajador una vez, y genera dos EXE autónomos y
   comprimidos del mismo conjunto. Importa con `POST /api/releases/setup/v3` un
   manifiesto esquema 3, campos multipart `manifest`, `online`, `complete`, `kiosk`
   y `worker`. El límite agregado sigue siendo 1 GiB más margen multipart. Valida
   tamaños, hashes, nombres, protocolo y ZIP en disco. Un reintento con los mismos
   archivos y manifiesto es idempotente; cambiar contenido exige otra versión.
4. La publicación queda como **candidata**. `/instalador` destaca las ediciones Online
   y Completo para USB de la versión más reciente, con enlaces fijados a esa versión
   incluso antes de activarla. Descargar ambas y probar los equipos piloto. El asistente
   antiguo 1.4.0 no aparece en la página. Ambas ediciones usan las mismas versiones
   fijadas; no resuelven componentes a «latest».
5. Tras aprobar las pruebas de VM/piloto, pulsar **Activar versión 1.5.4**.
   Requiere cookie administrativa y antiforgery. Se verifican de nuevo los dos EXE
   y los dos componentes antes de sustituir atómicamente `setups/v3/active.json`.
   La importación fallida nunca modifica ese puntero.
6. Recuperación: restaurar otra versión validada de las nuevas ediciones desde
   **Otras versiones y recuperación**, que aparece cuando hay más de una publicación.
   Se conservan publicaciones, almacén antiguo y todos los componentes referenciados.
   Descargar una candidata no cambia la versión activa ni asigna trabajos a la flota.

`/panel/setup/download` sirve online de la versión activa; `?edition=complete` sirve
USB de esa misma versión. `?version=1.5.4&edition=online` permite probar candidatas;
`?version=legacy` permite recuperar el asistente anterior. La API antigua de
publicación `/api/releases/setup` sigue operativa para manifiestos esquema 2.

Los componentes publicados son inmutables y se descargan desde
`GET /api/setup/v3/components/{kind}/{sha256}` (`kind`: `kiosk` o `worker`) con
`X-Setup-Key`, ETag fuerte basado en SHA-256 y Range. No se exponen archivos
huérfanos de importaciones interrumpidas. El protocolo de componentes 1 es
independiente del catálogo de aplicaciones 3.

## Verificación y aceptación

```powershell
. .\tools\resolve-dotnet.ps1
$sdk = Resolve-KioskDotnet
& $sdk test .\KioskClinicaPC.sln -c Release -nologo
```

Las pruebas cubren WPF con servicios y procesos simulados, selección/preselección,
catálogos vacíos, auth, timeout/reintentos, incompatibilidad sin fallback,
cambio de revisión/versiones, orden de componentes, cancelación/cierre del
trabajador, reanudación, estados ambiguos, migraciones idempotentes, selección de
última compatible, actualización acotada, hashes y compatibilidad de trabajadores.
Las suites
existentes siguen comprobando exportación WinGet y APIs de la flota.

**Pendiente de aceptación real en VMs Windows 10 22H2 y Windows 11 x64:**

- Abrir el EXE del panel sin .NET/WinGet; comprobar WPF inmediato y apps del panel.
- Confirmar una sola solicitud UAC y ninguna ventana de Inno ni decisión posterior.
- Probar pack-only sin servicios, tareas, autostart o perfil Kiosk.
- Probar solo Kiosk y ambos, con UAC bajo otra cuenta; verificar HKCU original.
- Probar red interrumpida, instalador lento, cancelación, reanudación y reinicio manual.
- Probar versión instalada superior, por usuario, retirada y resultado sin verificar.
- Retirar una versión entre comprobación e instalación: mantenerla congelada en esa
  ejecución y resolver de nuevo únicamente los pendientes al reanudar.
- Forzar un fallo ordinario en una app: verificar las demás y Kiosk, resultado parcial
  y código 2; forzar todas indisponibles: comprobar que Kiosk independiente continúa.
- Ejecutar el modo totalmente silencioso desde consola elevada interactiva.
- Online: probar solo Kiosk, solo pack y ambos, comprobar que no se solicitan componentes
  desmarcados; interrumpir una descarga y comprobar reanudación con ETag/Range.
- Completo: desconectar la red, desmarcar pack e instalar solo Kiosk; volver a conectar
  y probar pack. Confirmar que el pack no se presenta como un repositorio offline.
- Windows sin .NET y sin WinGet: comprobar el frontend autónomo y el bootstrap oficial.
- Usar UAC con otra cuenta, reiniciar y reanudar; comprobar registro HKCU del usuario original.
- Publicar en servidor de laboratorio con claves ficticias, probar ambas candidatas,
  activar, reiniciar el servidor y restaurar 1.4.0; verificar que ninguna prueba cambia
  el panel de producción. No ejecutar estos comandos de instalación en el PC de desarrollo.

La compilación y los tests no validan instaladores reales. No ejecutar estas
instalaciones ni el cliente fullscreen en el escritorio de desarrollo.
