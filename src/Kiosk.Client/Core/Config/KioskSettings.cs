using System;
using System.IO;
using Newtonsoft.Json;
using Serilog;

namespace KioskClinicaPC.Core.Config
{
    /// <summary>Ajustes de comportamiento del kiosko (separados del contenido en KioskConfig.json).</summary>
    public class KioskSettings
    {
        public const int CurrentPasswordPolicyVersion = 1;
        public const int MinimumPasswordLength = 12;

        public string? PasswordHash { get; set; }
        /// <summary>Versión de la política de contraseña aceptada por el encargado. Los perfiles
        /// anteriores a la política actual deben renovar su clave una sola vez.</summary>
        public int PasswordPolicyVersion { get; set; }
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

        /// <summary>URL base del servidor de contenido (p.ej. "https://kiosko.mitienda.com"). Vacío/null =
        /// modo local puro: el kiosko usa solo su KioskConfig.json (comportamiento previo al rework).
        /// La rellena el instalador del cliente. Ver <see cref="Services.RemoteConfigRepository"/>.</summary>
        public string? ServerUrl { get; set; }

        /// <summary>Clave que el cliente envía en la cabecera X-Api-Key para leer del servidor.
        /// Vacío = no se envía cabecera (servidor en modo abierto, solo para pruebas).</summary>
        public string? ServerApiKey { get; set; }

        /// <summary>Id estable del equipo en la flota (GUID). Se autogenera en el primer arranque y no
        /// cambia; identifica el kiosko ante el panel aunque se renombre o cambie de IP. Ver
        /// <see cref="EnsureDeviceIdentitySeeded"/>.</summary>
        public string? DeviceId { get; set; }

        /// <summary>Nombre visible del equipo en el panel de flota. Por defecto el hostname; editable en
        /// los ajustes del kiosko y renombrable desde el panel (no toca el hostname real de Windows).</summary>
        public string? DeviceName { get; set; }

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

        /// <summary>
        /// Aplica la URL y la clave sembradas por el instalador si el perfil aún no tiene servidor.
        /// El Setup deja el fichero junto al exe para no depender de qué cuenta aceptó el UAC;
        /// el kiosko lo lee como el usuario interactivo correcto. Conserva los demás ajustes.
        /// </summary>
        public bool ApplyProvisioningIfMissingServer(string provisioningPath)
        {
            if (!string.IsNullOrWhiteSpace(ServerUrl) || !string.IsNullOrWhiteSpace(ServerApiKey) ||
                !File.Exists(provisioningPath)) return false;

            try
            {
                var provisioning = JsonConvert.DeserializeObject<ServerProvisioning>(File.ReadAllText(provisioningPath));
                string? url = provisioning?.ServerUrl?.Trim().TrimEnd('/');
                string? key = provisioning?.ServerApiKey?.Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(key))
                {
                    Log.Warning("El aprovisionamiento del servidor no es válido; se omite.");
                    return false;
                }

                ServerUrl = url;
                ServerApiKey = key;
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo leer el aprovisionamiento del servidor.");
                return false;
            }
        }

        private sealed class ServerProvisioning
        {
            public string? ServerUrl { get; set; }
            public string? ServerApiKey { get; set; }
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

        /// <summary>Indica si el primer arranque o una actualización debe pedir una contraseña segura.</summary>
        public bool RequiresPasswordSetup()
            => string.IsNullOrWhiteSpace(PasswordHash) || PasswordPolicyVersion < CurrentPasswordPolicyVersion;

        /// <summary>
        /// Establece o renueva la contraseña. Si ya existe un hash exige la clave actual, de modo que una
        /// actualización no permita a otra persona apropiarse de los ajustes del kiosko.
        /// </summary>
        public bool TrySetPassword(string? currentPassword, string? newPassword, string? confirmation,
            out string error)
        {
            if (!string.IsNullOrWhiteSpace(PasswordHash) &&
                !PasswordService.Verify(currentPassword ?? string.Empty, PasswordHash))
            {
                error = "La contraseña actual no es correcta.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < MinimumPasswordLength)
            {
                error = $"La nueva contraseña debe tener al menos {MinimumPasswordLength} caracteres.";
                return false;
            }

            if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
            {
                error = "La nueva contraseña y su confirmación no coinciden.";
                return false;
            }

            PasswordHash = PasswordService.Hash(newPassword);
            PasswordPolicyVersion = CurrentPasswordPolicyVersion;
            error = string.Empty;
            return true;
        }

        /// <summary>Garantiza Id y nombre de flota: genera un GUID estable la primera vez y usa el hostname
        /// como nombre por defecto. Devuelve true si sembró algo (el llamante debe guardar).</summary>
        public bool EnsureDeviceIdentitySeeded()
        {
            bool changed = false;
            if (string.IsNullOrWhiteSpace(DeviceId))
            {
                DeviceId = Guid.NewGuid().ToString("N");
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(DeviceName))
            {
                DeviceName = Environment.MachineName;
                changed = true;
            }
            return changed;
        }
    }
}
