using System;
using System.IO;
using System.Threading;

namespace KioskClinicaPC.Core
{
    /// <summary>Escritura de archivos resistente a fallos a mitad de escritura.</summary>
    public static class JsonStore
    {
        // Serializa todas las escrituras del proceso: varias rutas (SaveEdits, SaveProductImage,
        // LoadHardwareAndConfigAsync, SettingsWindow) pueden tocar el mismo archivo. Sin esto,
        // dos escritores con el mismo .tmp se pisan y File.Move lanza IOException.
        private static readonly object _gate = new object();

        /// <summary>
        /// Escribe de forma atómica: vuelca a un temporal único y lo mueve sobre el destino.
        /// Evita dejar el archivo truncado/corrupto si el proceso falla durante el volcado,
        /// y tolera escrituras concurrentes + bloqueos transitorios (antivirus/indexador).
        /// </summary>
        public static void WriteAtomic(string path, string content)
        {
            lock (_gate)
            {
                // Nombre temporal único por escritura: evita colisiones entre llamadas concurrentes.
                string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(tmp, content);

                    // File.Move puede fallar si el destino está bloqueado un instante (AV/indexador).
                    // Reintenta unas pocas veces antes de rendirse.
                    const int maxAttempts = 5;
                    for (int attempt = 1; ; attempt++)
                    {
                        try
                        {
                            File.Move(tmp, path, overwrite: true);
                            return;
                        }
                        catch (IOException) when (attempt < maxAttempts)
                        {
                            Thread.Sleep(50 * attempt);
                        }
                        catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                        {
                            Thread.Sleep(50 * attempt);
                        }
                    }
                }
                finally
                {
                    // No dejes temporales huérfanos si algo falló a mitad.
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* limpieza best-effort */ }
                }
            }
        }
    }
}
