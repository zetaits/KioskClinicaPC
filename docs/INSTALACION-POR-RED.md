# ClínicaPC · Instalación por red

La integración de código está disponible para preparar un piloto. **No se ha publicado una estación ni validado PXE/Windows en equipos reales.** La descarga permanece vacía hasta importar un paquete compatible que incluya la validación completa. No habilitar el uso habitual antes del piloto.

## Componentes

- `Kiosk.DeploymentManager`: ventana WPF normal con Equipos, Sistemas y controladores, Trabajos y Configuración. Cerrar la ventana deja trabajando al servicio. No activa protección de kiosko.
- `Kiosk.DeploymentService`: autoridad local, cola persistente, canalización con ACL, DPAPI, proxyDHCP UEFI, TFTP inicial, HTTP de arranque, HTTPS de trabajador y SMB de solo lectura.
- `Kiosk.DeploymentPostInstall`: ejecutable autónomo bajo la cuenta creada. Limpia el inicio automático y archivos de respuestas antes de aplicaciones. Verifica build, edición exacta, usuario y componentes; Kiosk se registra sin abrirse.
- `Kiosk.EquipmentCore`: los adaptadores de recursos, pack, bloqueo y Kiosk del asistente anterior. El asistente mantiene recursos y modos propios.
- `Kiosk.DeploymentCore` y contratos v1 en Shared: validación, almacenamiento atómico, cola, archivo de respuestas y registro durable de progreso.

## Preparar el laboratorio y la estación

1. Usar un PC Windows 11 x64 administrativo y una red de laboratorio representativa de la tienda. El router sigue siendo DHCP. Reservar la IP de la estación en el router para que los destinos mantengan su dirección de callback después de un reinicio.
2. Instalar ADK y complemento WinPE **10.1.26100.9457** y revisar las actualizaciones oficiales aplicables. Usar iPXE firmado oficial para x86_64 Secure Boot, su shim y wimboot firmado. Crear un manifiesto basado en `deployment/boot-inputs.example.json` con versiones y hashes reales. El ejemplo incompleto se rechaza: no fija binarios inventados ni descarga `latest` automáticamente.
3. Como administrador, ejecutar `deployment/build-winpe.ps1 -InputManifest <manifiesto> -Output <carpeta nueva>`. Incluye WMI, NetFX, Scripting, PowerShell, StorageWMI, SecureStartup, DismCmdlets, Setup/Setup-Client, español y scripts. Las actualizaciones especificadas también deben superar SHA-256.
4. Crear un paquete de laboratorio con `build-deployment.ps1 -Version 0.2.0 -BootDirectory <carpeta WinPE>`, incorporando `-WorkerZip`, `-KioskInstaller` y `-KioskVersion` para probar pack/Kiosk. Usar `worker.zip` generado con el asistente 1.4.0 o posterior; se comprueba `pack-worker.json` con catálogo v3 al construir y antes de autorizar Windows Setup. Requiere Inno Setup 6. Ninguna clave permanente de panel, flota o Setup se incorpora a la estación ni al trabajador posterior.
5. Instalar el paquete e indicar la cuenta Windows autorizada del encargado. El instalador crea el servicio `ClinicaPCDeployment`. Administradores y esa cuenta pueden utilizar la canalización local. Una actualización/desinstalación exige desactivar la estación y que no existan trabajos activos o incidentes destructivos sin revisar.
6. Vincular desde Configuración usando un código de `/despliegue` de un solo uso, válido diez minutos. Elegir interfaz Ethernet y carpeta local vacía dedicada; capacidad inicial tres, rango uno a ocho. Guardar la contraseña local (8–128 caracteres).
7. Importar una ISO oficial Windows 11 Home/Pro x64 24H2+ español desde Sistemas y controladores. Se monta, se comprueba Windows Setup firmado por Microsoft y las ediciones, se extrae, se vuelve a comprobar la ISO y se registra integridad de cada archivo. El SHA-256 identifica el contenido importado; la procedencia oficial de la ISO debe comprobarse al obtenerla.
8. Con PXE desactivado y sin trabajos activos, importar controladores INF con catálogos firmados cuando sean necesarios. Se incorporan a WinPE y se conserva un ZIP por hash para su instalación offline en Windows. No se permiten ejecutables de controlador ni `ForceUnsigned`.
9. Activar PXE después de la comprobación de archivos, interfaz y puertos. Se requieren UDP 67/69/4011 y TCP 445/8089/8449. Las reglas creadas se limitan a IP/interfaz/subred. Un puerto ocupado bloquea la activación. Comprobar además en la red real que otro servidor PXE no responde: la comprobación de puertos locales no detecta servicios en otros ordenadores.

Documentación oficial: [iPXE Secure Boot](https://ipxe.org/secboot), [ADK](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-install), [actualizaciones ADK](https://learn.microsoft.com/en-us/windows-hardware/get-started/adk-servicing), [componentes WinPE](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/winpe-add-packages--optional-components-reference?view=windows-11), [opciones de Windows Setup](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/windows-setup-command-line-options?view=windows-11). iPXE es un proyecto independiente: https://ipxe.org.

## Instalar un lote

Encender el destino y elegir arranque de red UEFI por Ethernet con Secure Boot. El trabajador comunica hardware y muestra un identificador corto. El escaneo ICMP de la aplicación se limita a la interfaz/subred elegida (hasta 1024 direcciones); una respuesta solo significa «Detectado en red» y no identifica tipo de dispositivo ni ausencia de sistema operativo.

Crear/editar un perfil en `/despliegue`: imagen y edición, España, usuario inicial `Usuario`, aplicaciones con última versión compatible y Kiosk opcional desmarcado. Cada destino actualiza WinGet y resuelve su propia versión; un lote puede terminar con versiones diferentes. «Solo Windows» existe inicialmente sin imagen y debe configurarse. El panel permite cambiar usuarios pendientes y observar instalaciones; no inicia instalaciones.

Al actualizar el servidor, los perfiles antiguos con aplicaciones reciben una revisión
nueva con `ApplicationDefinition`; se conserva copia `deployment-v1.json.before-v3.bak`.
Los trabajos ya confirmados conservan perfil y versiones concretas; nunca se reescriben
durante la migración. Las estaciones antiguas pueden comunicar esos trabajos, pero
no confirmar perfiles nuevos con esta política. La estación nueva negocia
`X-Deployment-Component-Policy: 2` y exige servidor compatible.

La preparación continúa automáticamente con las aplicaciones disponibles. Los fallos
ordinarios quedan pendientes y no bloquean Kiosk. Si falta alguna verificación, el
trabajo pasa a «Requiere atención». Un instalador activo o incierto detiene la cola.
El panel, la estación y el diagnóstico conservan versión resuelta, versión instalada
y resultado por aplicación. El seguimiento durable también conserva estos datos sin
conexión; no se admite completar un perfil nuevo sin sus resultados verificados.

Seleccionar equipos listos en la aplicación. Con un solo disco interno identificable se preselecciona; con varios se debe elegir. La revisión muestra modelo, serie, disco, edición, usuario y componentes y exige aceptar el borrado de todas las particiones del disco elegido. USB no es candidato.

La estación vuelve a consultar perfiles/opciones después de verificar la imagen. La cola congela cada trabajo; el servidor también debe aceptar esa revisión antes de que pueda reclamarse. Un cambio concurrente deja el inicio bloqueado para revisión. Windows Setup conserva sus comprobaciones y usa `/NoReboot`; los recursos posteriores se descargan y verifican antes de Setup. El disco se compara nuevamente en WinPE justo antes de ejecutar Setup.

## Estado y recuperación

- Una caída de VPS no detiene trabajos aceptados; el seguimiento local y los eventos posteriores se conservan para reenviar. Un lote nuevo necesita conexión.
- Cerrar la ventana no detiene trabajos. Un trabajo en cola puede cancelarse. Un trabajo activo no se interrumpe desde la ventana durante Setup o instaladores nativos.
- Si la estación se reinicia, PXE queda desactivado y los trabajos activos pasan a «Requiere atención». Revisar físicamente el destino. No se repite una autorización destructiva ya entregada, incluso si se perdió la respuesta.
- Para recuperar aplicaciones, comprobar Windows, cuenta e instaladores, autorizar la reanudación desde Trabajos y ejecutar en el destino `C:\ProgramData\ClinicaPC\DeploymentJob\Kiosk.DeploymentPostInstall.exe --resume` elevado bajo el usuario creado. Se verifican Windows y limpieza de credenciales otra vez; nunca se ejecuta Setup desde esta recuperación.
- Una incidencia resuelta sin éxito puede cerrarse tras verificar físicamente que no quedan instaladores activos. Se conserva el trabajo y su marca de borrado. Otra instalación requiere una sesión PXE nueva y otra confirmación completa.
- «Reinicio necesario» no reinicia el destino automáticamente. Kiosk no se abre al finalizar; su autostart actúa en el siguiente inicio de sesión.
- El diagnóstico exporta estados, revisiones de eventos e integridad. Excluye configuración, tokens, blobs DPAPI y archivos de respuestas.

Estado local: `%ProgramData%\ClinicaPC\Deployment`. No copiarlo a otro ordenador: DPAPI depende de esta máquina. Conservar sus archivos durante recuperación o desinstalación. Datos en VPS: `data/deployment-v1.json`, con códigos y credenciales de estación hasheados; paquetes privados: `setups/deployment`. La autenticación de estación usa `X-Deployment-Credential`, independiente de flota, Setup y publicación. No admite credenciales en la query del hub.

## Publicación y verificaciones pendientes

El workflow **Deployment Station (private)** necesita un runner Windows del laboratorio con etiqueta `clinicapc-deployment`, Inno Setup y un directorio que contenga `boot/`, `worker.zip`, `kiosk.exe` y `validation.json`. No crea una release pública de GitHub. El servidor compatible debe estar desplegado primero: `/health/ready` mantiene `{"status":"ok"}` y anuncia `X-Deployment-Protocol: 1` y `X-Deployment-Component-Policy: 2`.

Completar `deployment/validation.example.json` con evidencias del commit exacto y hashes de arranque/recursos usados. La publicación se bloquea mientras falte cualquiera de estas comprobaciones:

- PXE en red real y Home/Pro UEFI con Secure Boot.
- Tres instalaciones simultáneas y dos modelos físicos.
- Varios discos, dejando intacto el disco no elegido.
- Pérdida de VPS/red local y reinicio de estación en varias fases.
- Solo Windows, pack y pack con Kiosk.
- Escritorio, cuenta, limpieza de autologon, componentes y seguimiento final.
- Pack con aplicaciones no disponibles y continuación automática; reanudación que
  conserva las verificadas; versión congelada entre comprobación e instalación;
  bloqueo con estado nativo incierto y conservación de trabajos antiguos confirmados.

En el entorno de desarrollo se compilan proyectos, se ejecutan pruebas automatizadas
y se comprueba el asistente empaquetado en modo diagnóstico con configuración ficticia.
Faltan ADK/WinPE, ISO, binarios oficiales de arranque fijados y acceso a las
VM/equipos/red de la tienda. No se han ejecutado borrados, activado servicios/firewall/SMB
ni publicado o desplegado estos cambios. Los resultados automatizados no sustituyen
esa validación real.
