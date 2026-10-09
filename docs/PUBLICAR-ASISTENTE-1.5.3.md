# Publicar el asistente 1.5.3 con Kiosk 1.2.1

Ejecutar estos comandos en PowerShell desde este PC. El orden es guardar y subir
el código, desplegar el servidor y generar el asistente con GitHub Actions.
La sesión SSH y las credenciales de GitHub se resuelven desde tu consola;
no hay que copiar la contraseña del panel ni extraer su hash manualmente.

## Guardar los cambios de esta tarea

La lista excluye `docs/PLAN-INSTALADOR-LIGERO.md`. Revisa los cambios antes del commit.

```powershell
Set-Location 'C:\Users\zits\Documents\Proyectos\KioskClinicaPC'
$files = @(
    '.github/workflows/equipment-setup.yml',
    'build-equipment-setup.ps1',
    'docs/ASISTENTE-EQUIPOS.md',
    'docs/PUBLICAR-ASISTENTE-1.5.3.md',
    'src/Kiosk.Client/App.xaml.cs',
    'src/Kiosk.Client/Core/Config/KioskSettings.cs',
    'src/Kiosk.Client/Kiosk.Client.csproj',
    'src/Kiosk.EquipmentCore/KioskPayload.cs',
    'src/Kiosk.EquipmentCore/Payload.cs',
    'src/Kiosk.EquipmentSetup/Coordinator.cs',
    'src/Kiosk.EquipmentSetup/MainWindow.xaml',
    'src/Kiosk.EquipmentSetup/MainWindow.xaml.cs',
    'src/Kiosk.EquipmentSetup/Program.cs',
    'src/Kiosk.Server/Program.cs',
    'src/Kiosk.Server/Services/PanelAuthStore.cs',
    'src/Kiosk.Shared/EquipmentDiagnostics.cs',
    'src/Kiosk.Shared/EquipmentSetup.cs',
    'src/Kiosk.Shared/PanelPasswordProvisioning.cs',
    'src/Kiosk.Shared/PasswordService.cs',
    'src/Kiosk.Shared/SetupComponents.cs',
    'tests/Kiosk.EquipmentSetup.Tests/PanelPasswordTests.cs',
    'tests/Kiosk.EquipmentSetup.Tests/PayloadTests.cs',
    'tests/Kiosk.EquipmentSetup.Tests/WizardTests.cs',
    'tests/Kiosk.Server.Tests/EquipmentDiagnosticsTests.cs',
    'tests/Kiosk.Server.Tests/SetupComponentCacheTests.cs',
    'tests/Kiosk.Server.Tests/SetupReleaseTests.cs',
    'tests/Kiosk.Tests/KioskPasswordPolicyTests.cs',
    'tests/Kiosk.Tests/PasswordServiceTests.cs'
)
git add -- $files
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron preparar los cambios.' }
git diff --cached --stat
git diff --cached
```

Después de revisar:

```powershell
git commit -m 'Mejora el diagnóstico y el primer arranque del asistente de equipos'
if ($LASTEXITCODE -ne 0) { throw 'El commit no terminó correctamente.' }
git push origin master
if ($LASTEXITCODE -ne 0) { throw 'No se subió el código; no ejecutes aún la Action.' }
```

## Desplegar y generar

```powershell
$ErrorActionPreference = 'Stop'
& .\deploy-server-vps.ps1

$ready = Invoke-WebRequest 'https://panel.clinicapc.es/health/ready' -UseBasicParsing
if (($ready.Content | ConvertFrom-Json).status -ne 'ok' -or
    $ready.Headers['X-Kiosk-Password-Provisioning'] -ne '1') {
    throw 'El servidor actualizado todavía no está disponible.'
}

gh workflow run equipment-setup.yml --ref master -f version=1.5.3 -f kiosk_version=1.2.1
if ($LASTEXITCODE -ne 0) { throw 'No se pudo iniciar Equipment Setup.' }
gh run list --workflow equipment-setup.yml --branch master --event workflow_dispatch --limit 3
gh run watch --exit-status
```

Si GitHub CLI pide autenticación, ejecutar `gh auth login` y repetir el comando
que haya fallado. La Action usa los secretos existentes del entorno `production`.
Obtiene el verificador del panel mediante `X-Release-Publish-Key`, genera las
ediciones Online y Completo y las publica como candidatas privadas. No activa la
versión ni crea una release pública de Kiosk. No ejecutar el workflow hasta que
el servidor actualizado esté disponible.

## Probar desde el panel

Entrar en `https://panel.clinicapc.es/instalador` y descargar la candidata 1.5.3.
Los EXE locales de prueba 1.5.1 y 1.5.2 no incorporan esta contraseña inicial.

En un perfil sin contraseña local, instalar Kiosk y pulsar **Finalizar y abrir Kiosk**.
El primer arranque ya acepta la contraseña que tenía el panel al generar el EXE.
Abrir Ajustes con `Ctrl+Shift+S` y verificarla; se puede cambiar desde Ajustes
indicando la contraseña actual. Una contraseña local existente se conserva.
Probar también Kiosk y pack juntos: Kiosk solo se abre después de terminar ambos
y pulsar Finalizar; la casilla está marcada por defecto.

El EXE conserva una instantánea del verificador. Cambiar posteriormente la
contraseña del panel no cambia la contraseña de equipos ya configurados ni la
de un asistente anterior. Para nuevas instalaciones con la nueva contraseña,
generar otra versión del asistente. Tras probar las dos ediciones, activar la
1.5.3 desde el panel si se quiere convertirla en la descarga activa.

Si la 1.5.3 ya existe, usar una versión nueva tanto en la Action como en las
pruebas: las publicaciones son inmutables.
