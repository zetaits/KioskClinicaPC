using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace KioskClinicaPC.Core.Platform
{
    /// <summary>
    /// Abre el teclado en pantalla de Windows. Necesario en kioscos táctiles sin teclado físico:
    /// sin esto, nadie puede escribir la contraseña de administración para entrar a Ajustes o salir.
    /// Es estrictamente aditivo: si no logra abrirlo, el flujo con teclado físico sigue funcionando.
    /// </summary>
    public static class TouchKeyboard
    {
        public static void Show()
        {
            try
            {
                // Teclado táctil moderno (el que aparece automáticamente en tablets).
                string tabTip = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                    "microsoft shared", "ink", "TabTip.exe");
                if (File.Exists(tabTip))
                {
                    Process.Start(new ProcessStartInfo(tabTip) { UseShellExecute = true });
                    return;
                }

                // Respaldo: teclado en pantalla clásico (accesibilidad).
                Process.Start(new ProcessStartInfo("osk.exe") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se pudo abrir el teclado en pantalla.");
            }
        }
    }
}
