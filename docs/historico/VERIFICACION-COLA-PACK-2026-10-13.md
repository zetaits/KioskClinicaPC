# Verificación local de la cola concurrente del pack

Fecha: 13 de octubre de 2026.

Estado: cambio implementado y comprobado mediante build y pruebas locales.
No publicado ni activado. Se ha generado y ejecutado el asistente de prueba local
1.5.10, exclusivamente con pack. Tras reiniciar el PC, instaló y verificó cinco
aplicaciones; IZArc ya estaba instalada y verificada. El pack terminó en 151,565
segundos, con salida 0 y solapamiento real de instalación/verificación.
El MSI de LibreOffice dejó un aviso de reinicio que el resumen no refleja;
esa omisión se corrigió después en la [preparación de 1.5.11](VERIFICACION-ASISTENTE-1.5.11.md).
No se han sobrescrito los artefactos
existentes del asistente ni se ha reparado WinGet.

## Comportamiento implementado

- Ventana de cinco aplicaciones en curso, con admisión según el orden del pack.
- Operaciones COM concurrentes sobre el mismo `PackageManager`: WinGet mantiene
  hasta tres descargas y una instalación; una aplicación admitida puede instalarse
  antes de otra cuya descarga siga pendiente.
- Una verificación posterior activa, independiente de la instalación. Las
  verificaciones pendientes siguen ocupando plazas de la ventana.
- Un fallo de verificación tras éxito nativo confirmado permite continuar las
  restantes; mantiene el pack incompleto y conserva la protección de reintento.
- Cancelación, timeout nativo o resultado incierto cierran la admisión. Los
  instaladores conocidos como activos pueden terminar dentro de sus límites,
  sin matar procesos ni borrar recursos en uso.
- Estado guardado antes de llamar a WinGet; aplicación de resultados por un único
  coordinador y copias estables para persistencia, WPF y seguimiento por red.
- Campos aditivos `phase` y `nativeInstallSucceeded`, sin renumerar estados ni
  cambiar los protocolos de catálogo/componentes. Lectura conservadora de estados
  antiguos y respuesta tardía aislada del estado guardado.
- Progreso de bytes limitado a 250 ms por aplicación y transiciones inmediatas.
- Registros de intervalos observados y tiempos reales `PackExecution`/`PackTotal`.
  Las notificaciones COM pueden agrupar fases; sus duraciones no se suman para
  calcular el total.

Guías operativas: [Asistente](../ASISTENTE-EQUIPOS.md) y
[Pack WinGet](../PACK-WINGET.md).

## Validación automatizada

SDK: .NET 10 local; interoperabilidad COM fijada a WinGet 1.29.380.

| Comprobación | Resultado |
| --- | --- |
| Build Debug de `KioskClinicaPC.sln` | Correcto, cero errores y advertencias |
| Pruebas relevantes de `Kiosk.Server.Tests` | 151 superadas, cero omitidas |
| Pruebas de `Kiosk.EquipmentSetup.Tests` | 77 verificadas, cero omitidas |

El filtro del servidor incluye `Pack`, `SetupTimingLogTests`,
`EquipmentExecutionTests`, `DeploymentTests` y `DeploymentJournalTests`.

Las pruebas nuevas controlan las tareas mediante señales: demuestran que una
descarga posterior se instala primero, que la siguiente instalación empieza
antes de terminar la verificación anterior, que la ventana no admite una sexta
aplicación y que solo hay una verificación activa. También comprueban fracaso
de verificación, incertidumbre nativa, cancelación, fallo de persistencia,
respuestas tardías, limitación de progreso, intervalos, estados históricos y
presentación simultánea en WPF. Son pruebas de comportamiento; no constituyen
un benchmark de instalaciones reales.

La suite WPF tuvo 71 pruebas correctas y seis fallos de conexión a pipes dentro
del aislamiento. Las seis pruebas de `WorkerPipeTests` se repitieron con permisos
normales de Windows y pasaron. El protocolo y sus ACL no se modificaron.

La primera restauración NuGet falló por el acceso de red restringido. Se restauró
la solución desde la caché local, sin consulta de vulnerabilidades:

```powershell
& "C:\Users\zits\.dotnet\dotnet.exe" restore KioskClinicaPC.sln --source "C:\Users\zits\.nuget\packages" --ignore-failed-sources --disable-parallel --disable-build-servers -m:1 -p:NuGetAudit=false
& "C:\Users\zits\.dotnet\dotnet.exe" build KioskClinicaPC.sln --no-restore -m:1 --disable-build-servers -nologo -clp:ErrorsOnly
& "C:\Users\zits\.dotnet\dotnet.exe" test tests/Kiosk.Server.Tests/Kiosk.Server.Tests.csproj --no-restore -m:1 --disable-build-servers -nologo --filter "FullyQualifiedName~Pack|FullyQualifiedName~SetupTimingLogTests|FullyQualifiedName~EquipmentExecutionTests|FullyQualifiedName~DeploymentTests|FullyQualifiedName~DeploymentJournalTests"
& "C:\Users\zits\.dotnet\dotnet.exe" test tests/Kiosk.EquipmentSetup.Tests/Kiosk.EquipmentSetup.Tests.csproj --no-build --no-restore -m:1 --disable-build-servers -nologo
```

## Validación pendiente y publicación

1. En Windows 10 y 11, comparar la cola normal con
   `PackExecutionOptions(MaxInFlight: 1)` desde snapshots equivalentes. Usar
   el mismo catálogo, versiones resueltas y condiciones de red/caché, con al
   menos seis aplicaciones y tres ejecuciones por modalidad.
2. Comparar medianas de `PackExecution` y tiempos totales, y comprobar en los logs
   nativos el solapamiento, la reutilización de descargas y las dependencias.
   No se afirma ningún porcentaje de mejora con la evidencia local actual.
3. Validar pérdida de red, cancelación durante descarga/instalación/verificación,
   reinicio, reanudación y fallos de aplicaciones reales en esas VMs.
4. Generar y comprobar ambas ediciones Online/Completo de publicación. El EXE
   Completo de prueba local descrito más abajo no es una candidata publicada.
   Los EXE anteriores no contienen este cambio por un build local.
5. Publicar y activar únicamente después de aceptar esas evidencias, siguiendo
   [el procedimiento vigente](../ASISTENTE-EQUIPOS.md#publicación-activación-y-recuperación).
6. El aviso de reinicio omitido quedó corregido en
   [1.5.11](VERIFICACION-ASISTENTE-1.5.11.md), comprobando también el estado de
   Windows y guardando evidencia del resultado nativo. El resultado histórico
   de esta prueba no se modifica.

Los cambios previos ajenos a esta tarea se conservaron. Durante esta validación
local no se desplegó el panel ni se creó un commit; no se modificaron secretos
ni activos web de la ficha QR.

## Prueba en este PC: 13 de octubre, 20:18 CEST

El usuario solicitó ejecutar el pack y medirlo en este PC. Se generó, sin
publicación, `installer/Output/Setup-EquipoClinicaPC-1.5.10-PruebaLocal.exe`:
265.755.819 bytes. Su diagnóstico confirma asistente y trabajador 1.5.10,
edición Completo, manifiesto compatible y recursos verificados.

La configuración limitada del panel se recuperó privadamente del EXE de prueba
local 1.5.2 ya existente. El catálogo v3 respondió 200, revisión 26, con seis
aplicaciones. No se consultaron archivos `.env`, no se mostraron claves y la copia
de `config.json` usada para compilar se retiró después. El componente Kiosk
conservado del artefacto original es 1.2.0: esta prueba debe usarse solo con Pack,
dejando Kiosk desmarcado. No sustituye los instaladores distribuidos por el panel.

Antes de ejecutar, la auditoría de solo lectura tardó 2,352 segundos y confirmó
que `last-run.json` no cambió. Detectó Chrome pendiente de verificación del mismo
arranque; la API no mostraba instalación activa ni versión verificable.

Se ejecutó elevado mediante UAC con:

```text
/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack /RESUME
```

La comprobación previa elevada conservó esa protección y detuvo la ejecución
antes de invocar nuevas instalaciones. El resultado fue salida 1 y exige un
reinicio manual antes de reintentar. IZArc ya estaba verificada; las otras cuatro
aplicaciones superaron su comprobación previa y quedaron pendientes.

| Medición | Segundos |
| --- | ---: |
| Ejecución medida después de la elevación | 2,825 |
| Activación de WinGet | 0,097 |
| Actualización del catálogo | 0,180 |
| Consultas de comprobación previa | 1,125 |
| `PackTotal`, incluida comprobación previa | 1,137 |
| Operaciones nativas nuevas | 0 |

Estas medidas corresponden a una ejecución bloqueada. No demuestran todavía
el solapamiento de descargas/instalaciones reales ni una mejora de rendimiento.
En ese momento, la siguiente prueba requería reiniciar manualmente el PC y
ejecutar el mismo asistente solo con Pack. No se borró ni se eludió la protección
de Chrome. La ejecución posterior se documenta a continuación.

Evidencia local en `installer/Output/local-test-1.5.10/`: `execution.json`,
`measurement.json`, `timings.jsonl`, diagnóstico del asistente, auditoría y copias
del estado anterior/posterior. El SHA-256 del EXE está en su archivo `.sha256`.

## Prueba correcta tras reinicio: 13 de octubre, 20:41–20:43 CEST

El usuario reinició el PC a las 20:34 y ejecutó el mismo asistente local 1.5.10.
El log del asistente registra preparación desde las 20:41:06,936 y salida 0 a
las 20:43:39,788. `last-run.json` contiene `complete: true`, seis aplicaciones
verificadas y ninguna protección de reintento pendiente. Las seis comprobaciones
finales confirman instalación para todo el equipo (`System`).

| Medición | Segundos |
| --- | ---: |
| Preparación y ejecución registradas por el asistente | 152,852 |
| `PackTotal`, incluida comprobación previa | 151,565 |
| `PackExecution`, desde admisión hasta última verificación | 148,819 |
| Consultas de comprobación previa | 2,733 |
| Activación de WinGet | 0,121 |
| Actualización del catálogo | 0,186 |

El tiempo del asistente excluye la interacción previa y la elevación. Los tiempos
por aplicación se solapan; no deben sumarse para obtener el total.

| Aplicación, en orden de instalación | Versión instalada | Instalación nativa observada (s) | Verificación (s) |
| --- | --- | ---: | ---: |
| Google Chrome | 155.0.8059.40 | 13,173 | 0,213 |
| VLC media player | 3.0.24.0 | 3,863 | 0,251 |
| Adobe Acrobat Reader (64-bit) | 26.002.21996 | 47,452 | 0,194 |
| K-Lite Mega Codec Pack | 20.0.5 | 15,686 | 0,188 |
| LibreOffice | 26.8.1.1 | 41,987 | 0,181 |
| IZArc, ya instalada | 4.6 | — | Comprobada antes de admitir la cola |

WinGet compara VLC 3.0.24.0 como igual a la versión requerida 3.0.24; no es una
discrepancia de versión. Las cinco verificaciones posteriores se resolvieron en
su primer intento.

Evidencia de concurrencia en los intervalos UTC:

- Las cinco operaciones WinGet se admitieron aproximadamente a las 18:41:11.
  Chrome se instaló primero, seguido de VLC, Adobe, K-Lite y LibreOffice: la cola
  pudo aprovechar una aplicación preparada antes de otras anteriores del catálogo.
- Mientras Chrome estaba en `NativeInstall` (18:41:11,810–18:41:24,983), Adobe,
  LibreOffice y VLC notificaban fase `Download`. Esas notificaciones demuestran
  el solapamiento observado, pero sus intervalos pueden incluir espera por el
  instalador nativo; no miden exclusivamente transferencia de red.
- VLC comenzó su instalación a las 18:41:25,158 mientras Chrome se verificaba
  hasta las 18:41:25,283: solapamiento de 124,154 ms.
- K-Lite comenzó a las 18:42:41,561 mientras Adobe se verificaba hasta las
  18:42:41,658: solapamiento de 96,659 ms.
- Los intervalos observados mantienen una sola instalación nativa y una sola
  verificación activas. No hay resultados fallidos, cancelados o inciertos en
  `timings.jsonl`.

Los logs nativos contienen avisos internos que no hicieron fallar la instalación:
VLC identifica como inocuo el aviso de una carpeta de accesos directos existente;
Adobe permite continuar tras acciones de integración opcional, como la búsqueda
de Lotus Notes. Los MSI confirman instalación correcta y las verificaciones
posteriores confirman versión y ámbito.

En esta prueba hubo una discrepancia: el MSI de LibreOffice registra a las 20:43:36,390
que se requiere un reinicio y que se ha pospuesto. El estado del pack y el resultado
del asistente guardan `rebootRequired: false`. La instalación está verificada, pero
conviene completar ese reinicio manual cuando sea oportuno. Esta revisión no
modificó el resultado guardado ni reinició el equipo.

Se conservaron copias separadas de esta ejecución en
`installer/Output/local-test-1.5.10/successful-run-20261013-184107/`:
`last-run.json`, `equipment.log`, `pack-logs/` y `measurement.json`, con tiempos
y solapamientos calculados. El origen es
`C:\ProgramData\ClinicaPC\Setup\logs\pack-20261013-184107-871` y el log del
asistente `equipment-1e6f9a9406b34b99bc92e621c8e2930a.log`.

Esta prueba confirma funcionamiento y concurrencia reales en este PC. No hay una
ejecución secuencial desde un estado equivalente con la que calcular un porcentaje
de reducción del tiempo. El benchmark en VMs y la publicación siguen pendientes.
