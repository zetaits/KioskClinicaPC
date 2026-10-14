# Preparación del asistente 1.5.11

Fecha: 13 de octubre de 2026. Estado de la preparación local: corrección
implementada y validada; ediciones locales de validación generadas.
Actualización del 14 de octubre: candidata de producción publicada en el panel;
activación y prueba de instalación limpia aún sin confirmar.

Esta versión prepara la cola concurrente probada en
[la ejecución real del pack](VERIFICACION-COLA-PACK-2026-10-13.md) y corrige el aviso
de reinicio omitido en aquel resultado. Kiosk incluido en el build local: 1.2.2.

## Corrección y evidencia

El adaptador anterior transmitía solamente `InstallResult.RebootRequired`. El MSI
de LibreOffice registró reinicio aplazado, mientras el resultado guardado quedó
con `rebootRequired: false`. Windows conservaba operaciones pendientes de archivos.
La implementación nueva combina la señal COM, códigos 3010/1641 y las marcas de
reinicio de Windows. Comprueba Windows antes de cada preflight y después de cada
resultado nativo, incluyendo el caso en que todo el pack ya esté instalado.

La lectura utiliza HKLM de 64 bits: `Component Based Servicing\RebootPending`,
`WindowsUpdate\Auto Update\RebootRequired` y los valores
`PendingFileRenameOperations`/`PendingFileRenameOperations2` de Session Manager.
No modifica el Registro ni muestra las rutas pendientes. El aviso puede proceder
de una instalación anterior; no cambia el resultado de verificación ni detiene
la cola. El reinicio sigue siendo manual.

Microsoft documenta que 3010 y 1641 son resultados de éxito con reinicio, y que
`MsiSystemRebootPending` detecta operaciones de cambio de nombre pendientes.
El código WinGet admite códigos de éxito adicionales que pueden omitir su rama
de señalización de reinicio. No se afirma que esa fuera la causa exacta del
resultado COM anterior, porque no se había guardado completo.
Fuentes: [códigos MSI](https://learn.microsoft.com/en-us/windows/win32/msi/error-codes),
[propiedad MSI](https://learn.microsoft.com/en-us/windows/win32/msi/msisystemrebootpending)
y [flujo oficial WinGet](https://github.com/microsoft/winget-cli/blob/master/src/AppInstallerCLICore/Workflows/InstallFlow.cpp).

Los nuevos `*.native-result.jsonl` guardan estado, códigos numéricos y señales
de reinicio para poder distinguir esos casos en futuras ejecuciones. No contienen
datos HTTP, claves ni mensajes de excepción.

La auditoría de solo lectura del trabajador empaquetado 1.5.11 en este PC confirmó:

- Salida 0, `readOnly: true`, comprobación previa correcta y seis verificadas.
- `complete: true` y `rebootRequired: true`.
- Resumen: «Todas las aplicaciones verificadas. Solo queda reiniciar manualmente.»
- SHA-256 del `last-run.json` original sin cambios antes/después. No se reinstaló
  ninguna aplicación ni se reinició el equipo.

## Validación

| Comprobación | Resultado |
| --- | --- |
| Pruebas de toda la solución en Release | 621 superadas, cero fallos y omitidas |
| Desglose | 191 cliente, 351 servidor, 79 asistente |
| Filtro del motor, cola y preparación en Debug | 163 superadas |
| Pantalla final y sesión del trabajador en Debug | 31 superadas |
| Build de Kiosk 1.2.2, trabajador y ambas ediciones | Correcto |
| Diagnóstico Online | Compatible, sin binarios opcionales |
| Diagnóstico Completo | Compatible, hashes de ambos recursos verificados |
| Sintaxis del script PowerShell | Correcta |

Las pruebas nuevas cubren reinicio aunque COM no lo señale, códigos nativos,
marcas de Windows, eliminación de archivos con destino vacío, valores ausentes
y persistencia del aviso después de verificar sin detener las demás aplicaciones.
La pantalla final muestra reinicio pendiente con cero aplicaciones pendientes
o fallidas y no ofrece abrir Kiosk.

Comando de validación Release, con permisos normales de Windows para los pipes:

```powershell
& "C:\Users\zits\.dotnet\dotnet.exe" test KioskClinicaPC.sln -c Release --no-restore -m:1 --disable-build-servers -nologo -p:NuGetAudit=false
```

## Artefactos locales y publicación

Se compiló desde una copia aislada en
`installer/Output/candidate-1.5.11/workspace/`, conservando los EXE y salidas
anteriores. NuGet se restauró desde la caché local. Los binarios son de validación:
contienen `https://setup.invalid`, claves ficticias y ningún verificador de
contraseña del panel. No deben distribuirse como instaladores de producción.

| Archivo | Bytes | SHA-256 |
| --- | ---: | --- |
| Online | 64.706.931 | `234a594dba592df5e225f071426675727f8b672e62ce0c08b3d951e889d57299` |
| Completo | 265.875.268 | `28f9225254158b4917f29accf57cef7f51f2bce69a99456d49f67a261489f64e` |
| Trabajador ZIP | 43.030.972 | `87ad22b61485cc7b96b7f558190657205b5fcb2f3aca853b43cad4e9343876ff` |
| Kiosk EXE | 157.748.535 | `d8b2f3d648f2cafc1d962fbbf0b1f2718c8d9651a046b44896218cc26ca86209` |

La carpeta `installer/Output/candidate-1.5.11/` conserva build, pruebas, auditoría,
diagnósticos, inventario de fuentes y copia de los archivos en `artifacts/`.
El `sourceCommit` del manifiesto local identifica la base del checkout;
en el momento de ese build la corrección estaba en el árbol de trabajo,
no en ese commit. El inventario identifica el contenido utilizado por el build.

El builder y **Equipment Setup** quedan preparados con 1.5.11 como versión
predeterminada. El builder genera los `.sha256` de ambas ediciones y admite
`-PrepareRelease` para generar archivos con aprovisionamiento real sin importación.
Se comprobó que las credenciales necesarias y la variable de clave pública están
configuradas en GitHub `production`; no se leyeron sus valores. En el momento de
la preparación local, la última ejecución consultada había publicado 1.5.9.

El workflow **Equipment Setup** se ejecutó desde `a65264c` con
`version=1.5.11` y Kiosk 1.2.2. La [ejecución 38459039467](https://github.com/zetaits/KioskClinicaPC/actions/runs/38459039467)
terminó correctamente el 13 de octubre a las 20:03 UTC: generó ambas ediciones,
las diagnosticó e importó una candidata privada en el panel. El build de producción
exige un verificador válido de la contraseña del panel y comprueba que los EXE lo
incluyan. Esta evidencia no confirma que la candidata esté activa ni sustituye la
prueba de primer arranque en un perfil limpio.
Procedimiento: [publicación del asistente](../ASISTENTE-EQUIPOS.md#publicación-activación-y-recuperación).

El 14 de octubre se repitieron las pruebas Release del árbol de trabajo: 191 del
cliente, 355 del servidor y 79 del asistente, 625 en total. Las seis pruebas del
asistente que usan tuberías y procesos hijos fallaron dentro del aislamiento del
entorno; al repetir la batería del asistente fuera de él, pasaron las 79. Esta
verificación no instala Kiosk ni prueba el aprovisionamiento en un perfil limpio.

La preparación local no prueba una instalación limpia de ambas ediciones en
Windows 10/11 ni aporta un porcentaje de aceleración frente a una ejecución
secuencial equivalente. La prueba real de concurrencia de 1.5.10 y estas
comprobaciones de 1.5.11 se conservan como evidencias distintas.
