# Asistente WPF para preparar equipos

`Setup-EquipoClinicaPC-1.3.0.exe` es una descarga privada del panel autenticado
`/instalador`. Su versión es independiente de la de Kiosk incluida. No se publica
en GitHub Releases ni como artifact público. Contiene credenciales limitadas de
aprovisionamiento; no contiene claves privadas de firma ni claves de publicación.

## Uso

1. Descargar desde `/instalador` y abrir el EXE. Aparece WPF antes de extraer los
   componentes grandes. Pack está activado y Kiosk desactivado inicialmente.
2. Elegir solo pack, solo Kiosk o ambos. Solo Kiosk omite catálogo y WinGet.
3. Para el pack, revisar nombres, versiones fijadas y selección inicial del panel.
   Un pack vacío muestra un aviso y permite volver a componentes.
4. Confirmar la selección y la opción de reanudación. Al pulsar **Instalar**,
   Windows solicita una elevación del trabajador. Se comprueba de nuevo la revisión
   y cada versión contra `/api/setup/v2/catalog` con `X-Setup-Key` y el encabezado
   `X-Setup-Catalog-Version: 2`. Si cambió, se vuelve a revisión antes de preparar
   WinGet o ejecutar instaladores. No se consulta el catálogo privado de ejecutables.
5. El orden es comprobación de todas las aplicaciones, Kiosk y pack. Inno actúa
   únicamente como motor silencioso de Kiosk. El frontend registra el autostart
   mediante `--register-autostart-only` bajo el usuario original, incluso si UAC
   usó otra cuenta administradora. No abre Kiosk automáticamente: la opción final
   está desmarcada. Los reinicios son manuales.

La interfaz permanece abierta al cancelar una instalación: espera al trabajador.
No mata instaladores nativos ni borra sus archivos. Un resultado ambiguo detiene
la cola y exige comprobación o reinicio antes de reintentar. Una app con fallo
ordinario permite intentar las siguientes. Solo se declara éxito si todos los
componentes elegidos están verificados y el autostart de Kiosk se ha confirmado
para el usuario original.

La ventana usa los colores y la tipografía Space Grotesk del panel, controles
propios y el icono de Clínica PC en el EXE y la barra de tareas. La barra superior
personalizada permite arrastrar, redimensionar, minimizar, maximizar/restaurar
y cerrar; el cierre conserva la espera segura del trabajador. Los controles
incluyen estados de foco por teclado, hover y deshabilitado. Las listas se
desplazan y los textos se ajustan al ancho disponible.

Estado: `%ProgramData%\ClinicaPC\Setup\last-run.json`, `kiosk-run.json` y
`logs/`. Los directorios `work/` son exclusivos de administradores y SYSTEM.
El trabajador conserva el bloqueo `run.lock` durante comprobación, Kiosk y pack;
`equipment.lock` coordina instancias del nuevo asistente. Los recursos solo se
extraen si se eligieron, a rutas internas, y se verifica SHA-256. WinGet y su
`Microsoft.Management.Deployment.winmd` quedan físicamente juntos, aislados del
frontend WPF. Se conservan el exportador `export-index` y los comandos antiguos
del helper para compatibilidad.

## Compilación local ficticia

Desde la raíz, con .NET 10 SDK e Inno Setup 6 disponibles:

```powershell
& .\build-equipment-setup.ps1
```

Detecta el SDK compatible siguiendo `global.json` y los candidatos del usuario
y de `%TEMP%\clinicapc-dotnet`. Genera `installer\Output\Setup-EquipoClinicaPC-1.3.0.exe`
y `.bundle.json`. Usa `https://setup.invalid` y claves ficticias aunque haya
credenciales reales en el entorno. No instala nada. El build verifica el EXE
final en modo diagnóstico, comprobando versión, compatibilidad y hashes.

Los artefactos internos son inmutables: si ya existe esa versión local, elegir
otra con `-Version 1.3.1` o retirar manualmente **solo** el build ficticio anterior.
`-KioskVersion 1.2.0` fija la versión incluida sin crear tags ni releases de Kiosk.
El build genera ese payload desde el código del checkout; no descarga una release
histórica por su número. Publicar siempre desde un commit que se haya probado.

Diagnóstico sin instalación:

```powershell
& .\installer\Output\Setup-EquipoClinicaPC-1.3.0.exe --diagnose
# Para capturar JSON, ejecutar --diagnose-json con stdout redirigido.
```

El diagnóstico muestra únicamente versiones, commit, compatibilidad y estado
de recursos; no muestra claves ni configuración incrustada. Devuelve 0 si todo
es compatible y supera los hashes; 1 si falta un recurso o hay incompatibilidad.

## Modo completamente silencioso

Exige una consola **ya elevada**, bajo un usuario interactivo, nunca SYSTEM.
No abre WPF ni solicita UAC adicional. No lanza la pantalla fullscreen de Kiosk.

```powershell
& .\Setup-EquipoClinicaPC-1.3.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack
& .\Setup-EquipoClinicaPC-1.3.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=kiosk
& .\Setup-EquipoClinicaPC-1.3.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack,kiosk /RESUME
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

## Publicación independiente y orden de entrega

1. Implementar, probar y subir el commit. No ejecutar publicación o despliegue
   automáticamente al subirlo.
2. Desplegar el servidor desde la raíz: `& .\deploy-server-vps.ps1`. Comprobar
   `https://panel.clinicapc.es/health/ready`. Admite manifiestos v2 con
   `InstallerKind=equipment-wpf`, `CatalogApiVersion=2`, `SourceCommit` y versiones
   del asistente, trabajador y Kiosk, además de nombre, tamaño, hash y servidor.
   Los artefactos antiguos permanecen guardados, pero no se ofrecen como fallback.
3. En GitHub Actions, abrir **Equipment Setup**, **Run workflow**, rama `master`,
   versión `1.3.0`. Usa el entorno `production` y los secrets existentes
   `KIOSK_SERVER_API_KEY`, `KIOSK_INITIAL_SETUP_KEY`, `KIOSK_RELEASE_PUBLISH_KEY`
   y la clave pública de updates. No requiere la clave privada de firma. Prueba,
   empaqueta, diagnostica y publica exclusivamente con `POST /api/releases/setup`.
4. Descargar **ese nuevo EXE desde el panel**, comprobar versión y SHA-256 contra
   el manifiesto del almacén privado y probar en VMs limpias antes de distribuirlo.

Actualizar la VPS cambia el panel, **no reconstruye el EXE descargable**.
El workflow público **Release Kiosk** conserva su publicación de Kiosk y ya no
genera el asistente interno. Equipment Setup no importa updates de Kiosk, no
activa versiones, no crea tags/releases públicas y no asigna trabajos a la flota.
La organización `/aplicaciones`, `/instalador` y `/ordenadores/instalaciones`
se conserva.

## Verificación y aceptación

```powershell
. .\tools\resolve-dotnet.ps1
$sdk = Resolve-KioskDotnet
& $sdk test .\KioskClinicaPC.sln -c Release -nologo
```

Las pruebas cubren WPF con servicios y procesos simulados, selección/preselección,
catálogos vacíos, auth, timeout/reintentos, incompatibilidad sin fallback,
cambio de revisión/versiones, orden de componentes, cancelación/cierre del
trabajador, reanudación, estados ambiguos, hashes y manifiestos v2. Las suites
existentes siguen comprobando exportación WinGet y APIs de la flota.

**Pendiente de aceptación real en VMs Windows 10 22H2 y Windows 11 x64:**

- Abrir el EXE del panel sin .NET/WinGet; comprobar WPF inmediato y apps del panel.
- Confirmar una sola solicitud UAC y ninguna ventana de Inno ni decisión posterior.
- Probar pack-only sin servicios, tareas, autostart o perfil Kiosk.
- Probar solo Kiosk y ambos, con UAC bajo otra cuenta; verificar HKCU original.
- Probar red interrumpida, instalador lento, cancelación, reanudación y reinicio manual.
- Probar versión instalada superior, por usuario, retirada y resultado sin verificar.
- Ejecutar el modo totalmente silencioso desde consola elevada interactiva.

La compilación y los tests no validan instaladores reales. No ejecutar estas
instalaciones ni el cliente fullscreen en el escritorio de desarrollo.
