# Retención de cinco versiones del asistente

Fecha: 13 de octubre de 2026.

Estado: implementado y validado localmente. Aplicación en la VPS pendiente de
desplegar el servidor; no se ejecutó una limpieza sobre los paquetes de producción.
Procedimiento: [guía del asistente](../ASISTENTE-EQUIPOS.md#publicación-activación-y-recuperación).

## Comportamiento

- Cinco versiones v3 en total, incluida la tarjeta principal. El historial
  muestra las otras cuatro cuando todas están disponibles.
- La activa y la anterior de recuperación ocupan plazas protegidas. Las restantes
  se eligen por número de versión descendente, no por orden alfabético. Si se
  mantiene activa una versión antigua, el conjunto conservado puede incluirla
  en lugar de una candidata más reciente.
- Limpieza al arrancar el servidor, después de importar o activar, en reintentos
  idénticos y cada hora para retirar archivos que estuvieran ocupados.
- Retirada de las carpetas con ambas ediciones y de componentes sin referencias
  en las publicaciones restantes. Los componentes compartidos se conservan.
- Marcas mínimas en `v3/retired/` para impedir reutilizar una versión retirada.
- Publicación, activación y limpieza utilizan el mismo semáforo. Un fallo de
  limpieza no cambia el resultado de una publicación/activación ya completada.
- Puntero ilegible: no se retiran versiones. Manifiesto restante ilegible:
  no se retiran componentes cuya ausencia de referencias no pueda demostrarse.
- Rutas acotadas al almacén v3, rechazo de enlaces y comprobación del árbol antes
  de borrar. Se conservan los almacenes de instalación por red y del formato anterior.

Las versiones retiradas dejan de descargarse y no se pueden restaurar desde el
historial. Un Online antiguo que aún necesite sus componentes debe sustituirse
por una versión conservada. La limpieza puede quedar pendiente ante archivos en
uso o permisos insuficientes; se registra y se reintenta, conservando las versiones
activa y anterior.

## Validación

- Suite completa del servidor en Release: **365 pruebas superadas**, ninguna
  fallida u omitida. Incluye 14 casos nuevos de retención.
- Comprobaciones de límite, orden numérico, activa/anterior antiguas, activación,
  inicio del servicio, reintento idéntico, importación fallida y cancelación.
- Componentes compartidos conservados y trabajadores sin referencias retirados.
- Puntero corrupto, manifiesto corrupto y archivos ajenos al almacén protegidos.
- Junction/symlink dentro de una versión antigua: destino y enlace conservados;
  no se sigue la ruta redirigida.
- Importaciones concurrentes y rechazo de reimportar números retirados.
- Prueba HTTP: descarga activa y componente activo disponibles, versión retirada
  y componente retirado devuelven 404, readiness permanece correcta.
- El resto de pruebas de metadatos, componentes, streaming, autenticación,
  antiforgery e integridad del servidor sigue pasando.

```powershell
& "C:\Users\zits\.dotnet\dotnet.exe" test tests/Kiosk.Server.Tests/Kiosk.Server.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -nologo -p:NuGetAudit=false
```

Las pruebas usan almacenes temporales ficticios. No prueban una eliminación sobre
el sistema de archivos de la VPS. El despliegue habitual arranca el servicio de
retención y aplica el límite también al historial que ya exista. Este cambio
pertenece al servidor; no requiere generar otra versión del asistente.
