# Verificación de la contraseña del panel en Kiosk · 15 de octubre de 2026

## Alcance

Se eliminó la ventana de creación y renovación de contraseña del arranque.
Los kioskos con servidor validan el acceso a Ajustes contra la contraseña vigente
del panel y guardan el verificador tras un acierto para uso sin conexión. El
servidor expone `POST /api/kiosk/password/verify` con la API key del kiosko,
límite de intentos y respuesta sin caché. La sección de cambio de contraseña
local queda oculta en los perfiles conectados.

## Validación local

- Compilación completa en Release: correcta, sin errores.
- Pruebas del cliente: 184/184 correctas.
- Pruebas del servidor: 362/362 correctas.
- Pruebas del asistente: 76/82 correctas. Las seis variantes de `WorkerPipeTests`
  fallaron también al ejecutarse aisladas en este entorno Windows: los procesos
  auxiliares no conectaron a la tubería o agotaron el tiempo de espera. Los
  archivos del canal del asistente no forman parte de este cambio.
- CI de GitHub para `269f606`: la primera ejecución tuvo un único timeout en
  `WorkerPipeTests`; la repetición terminó con compilación y pruebas completas
  correctas. El resultado final de CI es verde.
- Interfaz WPF: no se abrió manualmente; su arranque activa la protección de
  kiosko incluso en Debug.

## Publicación

El push a GitHub no despliega el servidor ni el cliente. La VPS y los kioskos
siguen pendientes de actualización. Para poner en servicio este cambio, publicar
primero el servidor y verificar `/health/ready`; después distribuir una versión
del cliente que contenga la nueva validación. El paquete preparado localmente no
demuestra que la VPS esté actualizada.
