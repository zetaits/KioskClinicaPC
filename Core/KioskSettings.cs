using System;
using System.IO;
using Newtonsoft.Json;
using Serilog;

namespace KioskClinicaPC.Core
{
    /// <summary>Ajustes de comportamiento del kiosko (separados del contenido en KioskConfig.json).</summary>
    public class KioskSettings
    {
        public const string DefaultPassword = "clinicapc2025";

        public string? PasswordHash { get; set; }
        public int InactivitySeconds { get; set; } = 90;
        public int AutoScanSeconds { get; set; } = 18;
        public double SlideIntervalSeconds { get; set; } = 5.2;

        /// <summary>Animación de entrada a la pantalla Main tras el escaneo.
        /// Valores: "Iris", "ZoomThrough" o "Cycle" (alterna las dos en cada entrada).</summary>
        public string MainEntranceStyle { get; set; } = "Cycle";

        /// <summary>Nivel de efectos gráficos. "Auto" (por defecto) degrada blurs/partículas solo en
        /// equipos con render por software o GPU sin aceleración completa; "High" fuerza calidad máxima;
        /// "Low" fuerza modo ligero en cualquier equipo. Ver <see cref="GraphicsQuality"/>.</summary>
        public string GraphicsMode { get; set; } = "Auto";

        public static KioskSettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var s = JsonConvert.DeserializeObject<KioskSettings>(File.ReadAllText(path));
                    if (s != null) return s;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al cargar KioskSettings.");
            }
            return new KioskSettings();
        }

        public void Save(string path)
        {
            try
            {
                JsonStore.WriteAtomic(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al guardar KioskSettings.");
            }
        }

        /// <summary>Garantiza que exista un hash de contraseña; siembra el por defecto si falta.</summary>
        public bool EnsurePasswordSeeded()
        {
            if (string.IsNullOrEmpty(PasswordHash))
            {
                PasswordHash = PasswordService.Hash(DefaultPassword);
                return true;
            }
            return false;
        }
    }
}
