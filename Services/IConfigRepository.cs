using System.Threading.Tasks;
using KioskClinicaPC.Core;

namespace KioskClinicaPC.Services
{
    /// <summary>Resultado de cargar la configuración: el objeto, si hubo migración de esquema y si el
    /// archivo estaba dañado (en cuyo caso ya se respaldó y se devuelven valores por defecto).</summary>
    public readonly record struct ConfigLoadResult(AppConfig Config, bool Migrated, bool WasCorrupt);

    /// <summary>
    /// Persistencia de la configuración del kiosco (KioskConfig.json) y del último hardware detectado
    /// (KioskHardware.json). Aísla el acceso a disco, la (de)serialización, la migración de esquema y el
    /// respaldo de archivos dañados, para que el ViewModel no toque <c>File</c>/<c>JsonConvert</c> y sea testeable.
    /// </summary>
    public interface IConfigRepository
    {
        /// <summary>Lee y migra KioskConfig.json. Si no existe, devuelve config por defecto. Si está
        /// dañado, lo respalda (.corrupt) y devuelve config por defecto con <c>WasCorrupt = true</c>.</summary>
        Task<ConfigLoadResult> LoadConfigAsync();

        /// <summary>Lee el último hardware detectado. Nunca lanza: si falta o no se puede leer, devuelve uno vacío.</summary>
        Task<AppConfig> LoadLastHardwareAsync();

        /// <summary>Persiste la configuración de forma atómica. Puede lanzar (el llamador decide cómo tratarlo).</summary>
        void SaveConfig(AppConfig config);

        /// <summary>Persiste el hardware detectado de forma atómica.</summary>
        void SaveHardware(AppConfig hardware);
    }
}
