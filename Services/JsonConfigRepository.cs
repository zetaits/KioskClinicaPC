using System;
using System.IO;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using Newtonsoft.Json;
using Serilog;

namespace KioskClinicaPC.Services
{
    /// <summary>Implementación de <see cref="IConfigRepository"/> sobre JSON en disco (Newtonsoft +
    /// <see cref="JsonStore"/> para escritura atómica). Las rutas se inyectan para poder testear contra
    /// un directorio temporal; el constructor por defecto usa las de <see cref="App"/>.</summary>
    public sealed class JsonConfigRepository : IConfigRepository
    {
        private readonly string _configPath;
        private readonly string _hardwarePath;

        public JsonConfigRepository() : this(App.ConfigFilePath, App.HardwareFilePath) { }

        public JsonConfigRepository(string configPath, string hardwarePath)
        {
            _configPath = configPath;
            _hardwarePath = hardwarePath;
        }

        public async Task<ConfigLoadResult> LoadConfigAsync()
        {
            if (!File.Exists(_configPath)) return new ConfigLoadResult(new AppConfig(), false, false);

            try
            {
                // Migra el esquema si hace falta (campos renombrados/movidos entre versiones) en vez de
                // descartarlos en silencio. ConfigMigrator/JObject.Parse lanza solo si el JSON está corrupto.
                var config = ConfigMigrator.Migrate(await File.ReadAllTextAsync(_configPath), out bool migrated) ?? new AppConfig();
                if (migrated) Log.Information("KioskConfig.json migrado al esquema v{Version}.", AppConfig.CurrentSchemaVersion);
                return new ConfigLoadResult(config, migrated, false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "KioskConfig.json dañado; se respalda y se continúa con valores por defecto.");
                BackupCorrupt(_configPath);
                return new ConfigLoadResult(new AppConfig(), false, true);
            }
        }

        public async Task<AppConfig> LoadLastHardwareAsync()
        {
            try
            {
                if (File.Exists(_hardwarePath))
                    return JsonConvert.DeserializeObject<AppConfig>(await File.ReadAllTextAsync(_hardwarePath)) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "KioskHardware.json no se pudo leer; se re-detecta.");
            }
            return new AppConfig();
        }

        public void SaveConfig(AppConfig config)
            => JsonStore.WriteAtomic(_configPath, JsonConvert.SerializeObject(config, Formatting.Indented));

        public void SaveHardware(AppConfig hardware)
            => JsonStore.WriteAtomic(_hardwarePath, JsonConvert.SerializeObject(hardware, Formatting.Indented));

        /// <summary>Mueve un archivo dañado a una copia con sello de tiempo para no perder los datos.</summary>
        private static void BackupCorrupt(string path)
        {
            try
            {
                // Sello a segundos + sufijo único: dos corruptos en el mismo segundo no deben
                // sobrescribir el backup anterior (overwrite:true los perdería).
                string backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                if (File.Exists(backup))
                    backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
                File.Move(path, backup, overwrite: false);
                Log.Information("Archivo dañado respaldado en {Backup}.", backup);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo respaldar el archivo dañado {Path}.", path);
            }
        }
    }
}
