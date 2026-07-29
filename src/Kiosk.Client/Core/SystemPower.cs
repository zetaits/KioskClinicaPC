using System;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Acciones de energía del sistema: reiniciar/apagar el equipo y relanzar la propia app. Centralizado
    /// aquí para reusarlo desde los ajustes locales del kiosko y desde las órdenes remotas del panel
    /// (<see cref="Services.FleetClient"/>). Nunca lanza: registra el error y sigue (una orden fallida no
    /// debe tumbar el kiosko).
    /// </summary>
    public static class SystemPower
    {
        /// <summary>Reinicia el equipo (shutdown /r /t 0).</summary>
        public static void Reboot() => RunShutdown("/r /t 0");

        /// <summary>Apaga el equipo (shutdown /s /t 0).</summary>
        public static void Shutdown() => RunShutdown("/s /t 0");

        private static void RunShutdown(string args)
        {
            try
            {
                // Ruta completa (no "shutdown" por PATH): evita que un shutdown.exe plantado en el
                // directorio de trabajo secuestre la orden.
                string shutdownExe = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
                Process.Start(new ProcessStartInfo(shutdownExe, args) { CreateNoWindow = true, UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo ejecutar shutdown {Args}.", args);
            }
        }

        /// <summary>
        /// Relanza la aplicación: arranca un proceso auxiliar (PowerShell oculto) que ESPERA a que este
        /// proceso termine —liberando el mutex de instancia única y restaurando el escritorio en OnExit—
        /// y solo entonces vuelve a lanzar el .exe. El llamante debe cerrar la app justo después
        /// (p.ej. <c>Application.Current.Shutdown()</c>).
        /// </summary>
        public static void RelaunchApp()
        {
            try
            {
                string? exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) { Log.Error("Relanzar: no se pudo resolver la ruta del ejecutable."); return; }
                int pid = Environment.ProcessId;
                string psArgs = $"-NoProfile -WindowStyle Hidden -Command " +
                                $"\"Wait-Process -Id {pid} -ErrorAction SilentlyContinue; Start-Process '{exe}'\"";
                Process.Start(new ProcessStartInfo("powershell.exe", psArgs) { CreateNoWindow = true, UseShellExecute = false });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo programar el relanzamiento de la app.");
            }
        }
    }
}
