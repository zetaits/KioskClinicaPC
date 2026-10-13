# Notas históricas del asistente

Resumen de los cambios que estaban intercalados en la guía de uso. Describe
cambios del repositorio; no acredita que una versión esté publicada o activa.
Los procedimientos actuales están en [Asistente de equipos](../ASISTENTE-EQUIPOS.md).

## 1.5.9: preparación y comprobaciones

La candidata local reutiliza el módulo oficial cuando WinGet necesita reparación,
evita repetir las esperas de un fallo compartido del repositorio de manifiestos
y propaga la cancelación hasta COM y HTTP. Las comprobaciones trabajan sobre
copias para que una respuesta tardía no modifique el estado guardado. Añade
timings.jsonl por ejecución. Evidencia, builds ficticios y límites:
[verificación 1.5.9](VERIFICACION-ASISTENTE-1.5.9.md).

## 1.5.8: ámbito de lo instalado

Corrige la comparación de InstalledScope: COM devuelve System/User, mientras
los manifiestos YAML utilizan machine/user. La misma política verifica detección
previa, omisión de aplicaciones instaladas y resultado posterior. Mantiene el
rechazo de ámbitos/versiones desconocidos y de instalaciones por usuario.
Añade evidencia en *.verification.jsonl y diagnósticos de lectura.
[Caso comprobado y límites](VERIFICACION-ASISTENTE-1.5.8.md).

## 1.5.7: protección tras reinicio y límites de GitHub

Limpia una protección del arranque anterior antes de comprobar aplicaciones.
Un rechazo ordinario no conserva esa marca después de reiniciar, mientras una
instalación activa recién detectada y una operación incierta del mismo arranque
siguen deteniendo la cola. Las consultas respetan Retry-After y muestran un
motivo claro ante HTTP 429.

## 1.5.6: activación nativa de WinGet

Incluye Microsoft.Management.Deployment.dll junto al trabajador. Su ausencia
en 1.5.5 causaba COMException 80040154 antes de comprobar aplicaciones. La
compilación y la validación del ZIP pasan a exigir DLL y WinMD físicos.
El servidor conserva la lectura de publicaciones anteriores y exige la DLL
en publicaciones nuevas.

## 1.5.5: ventana de Kiosk

Incluye Kiosk 1.2.2 con la corrección del fallo al abrir la ventana principal
después de guardar el precio. Los colores de respaldo de los bindings usan
recursos estáticos válidos de WPF. La prueba carga ventana y plantillas sin
mostrarla ni activar protección de escritorio, red o detección real de hardware.
Se conservan precio y ajustes locales.

## 1.5.4: interfaz y cierre del canal

Las notas de esta versión recogen controles y tipografía del panel, barra de
ventana personalizada y estados de foco. Corrige un resultado parcial falso
al cerrar el canal después de una instalación correcta: el cierre normal no
invalida un resultado final cuyo código coincide con la salida del trabajador.
El asistente conserva recuperación del pack y selección de última compatible.

## 1.5.3: contraseña inicial del panel

Con Kiosk 1.2.1, los asistentes privados aprovisionados incorporan un verificador
de la contraseña del panel para perfiles sin contraseña local. Preservan
contraseñas existentes. Cambiar después la contraseña del panel no actualiza
los perfiles ni EXE anteriores. La apertura de Kiosk se ofrece al finalizar
los componentes elegidos. El [encargo de publicación](PUBLICAR-ASISTENTE-1.5.3.md)
conserva los pasos específicos de aquella tarea.

## 1.5.0: Online y Completo

Separa el trabajador sin WPF del frontend y permite descargar solo componentes
seleccionados en Online. Completo incorpora el mismo conjunto para USB; el pack
sigue necesitando internet. Introduce publicación privada esquema 3 con
componentes por hash, candidata, activación y recuperación.
[Tamaños, hashes y pruebas locales](VERIFICACION-INSTALADOR-1.5.0.md).
