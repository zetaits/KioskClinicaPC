using System;
using System.Net.Http;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using KioskClinicaPC.Core.Config;
using Newtonsoft.Json;
using Serilog;

namespace KioskClinicaPC.Services
{
    /// <summary>
    /// <see cref="IConfigRepository"/> que lee la configuración del servidor de contenido y mantiene una
    /// copia local como caché/fallback. Regla de oro del kiosko: si el servidor no responde (sin red,
    /// caído), NUNCA se queda en negro → cae al último KioskConfig.json cacheado en %LOCALAPPDATA%.
    ///
    /// El hardware detectado y el guardado de config son locales (el hardware es por-máquina; la edición
    /// remota la hará el panel de administración en Fase 3). Si no hay servidor configurado
    /// (<c>ServerUrl</c> vacío) se comporta exactamente como <see cref="JsonConfigRepository"/>.
    /// </summary>
    public sealed class RemoteConfigRepository : IConfigRepository
    {
        private readonly JsonConfigRepository _local; // caché + fallback + hardware
        private readonly HttpClient _http;
        private readonly string? _baseUrl;
        private readonly string _cachePath;

        /// <summary>Ctor de producción: construye el HttpClient a partir de la URL y la API key.</summary>
        public RemoteConfigRepository(string? baseUrl, string? apiKey, string cachePath, string hardwarePath)
            : this(BuildClient(apiKey), baseUrl, cachePath, hardwarePath) { }

        /// <summary>Ctor con HttpClient inyectado (para pruebas con un handler simulado).</summary>
        public RemoteConfigRepository(HttpClient http, string? baseUrl, string cachePath, string hardwarePath)
        {
            _http = http;
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');
            _cachePath = cachePath;
            _local = new JsonConfigRepository(cachePath, hardwarePath);
        }

        private static HttpClient BuildClient(string? apiKey)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (!string.IsNullOrWhiteSpace(apiKey))
                http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            return http;
        }

        public async Task<ConfigLoadResult> LoadConfigAsync()
        {
            // Sin servidor configurado → modo local puro (comportamiento previo al rework).
            if (_baseUrl == null) return await _local.LoadConfigAsync();

            try
            {
                string json = await _http.GetStringAsync(_baseUrl + "/api/config");

                // Valida y migra el esquema si el servidor va por detrás; si el JSON no es válido,
                // Migrate lanza y caemos a la caché local (mejor contenido viejo que pantalla rota).
                var config = ConfigMigrator.Migrate(json, out bool migrated) ?? new AppConfig();

                // Refresca la caché local para el próximo arranque sin red.
                JsonStore.WriteAtomic(_cachePath, JsonConvert.SerializeObject(config, Formatting.Indented));
                Log.Information("Config cargada del servidor y cacheada localmente.");
                return new ConfigLoadResult(config, migrated, false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Servidor de config no disponible; usando caché local.");
                return await _local.LoadConfigAsync();
            }
        }

        public Task<AppConfig> LoadLastHardwareAsync() => _local.LoadLastHardwareAsync();

        public void SaveConfig(AppConfig config) => _local.SaveConfig(config);

        public void SaveHardware(AppConfig hardware) => _local.SaveHardware(hardware);
    }
}
