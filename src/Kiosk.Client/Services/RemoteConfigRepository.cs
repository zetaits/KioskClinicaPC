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
    /// <see cref="IConfigRepository"/> que lee el contenido COMPARTIDO del servidor y lo funde sobre la
    /// config LOCAL de la máquina. El servidor manda en marketing/tienda/slides/textos/imágenes; el precio,
    /// el estado y las especificaciones autodetectadas son por-máquina y NUNCA los pisa el servidor
    /// (ver <see cref="SharedContent"/>). Regla de oro del kiosko: si el servidor no responde (sin red,
    /// caído), NUNCA se queda en negro → usa el último KioskConfig.json cacheado en %LOCALAPPDATA%.
    ///
    /// Si no hay servidor configurado (<c>ServerUrl</c> vacío) se comporta exactamente como
    /// <see cref="JsonConfigRepository"/>.
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

            // Base = config local de la máquina (precio, estado, specs autodetectadas). Se conserva pase
            // lo que pase con el servidor.
            var localResult = await _local.LoadConfigAsync();

            try
            {
                string json = await _http.GetStringAsync(_baseUrl + "/api/config");

                // Valida y migra el esquema si el servidor va por detrás; si el JSON no es válido,
                // Migrate lanza y nos quedamos con lo local (mejor contenido viejo que pantalla rota).
                var serverConfig = ConfigMigrator.Migrate(json, out bool migrated) ?? new AppConfig();

                // Funde SOLO el contenido compartido del servidor sobre lo local; el precio/specs siguen
                // siendo de esta máquina.
                var merged = localResult.Config;
                SharedContent.ApplyServerToLocal(merged, serverConfig);

                // Refresca la caché local (ya fusionada) para el próximo arranque sin red.
                JsonStore.WriteAtomic(_cachePath, JsonConvert.SerializeObject(merged, Formatting.Indented));
                Log.Information("Contenido compartido cargado del servidor y fundido con lo local.");
                return new ConfigLoadResult(merged, migrated || localResult.Migrated, localResult.WasCorrupt);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Servidor de contenido no disponible; usando caché local.");
                return localResult;
            }
        }

        public Task<AppConfig> LoadLastHardwareAsync() => _local.LoadLastHardwareAsync();

        public void SaveConfig(AppConfig config) => _local.SaveConfig(config);

        public void SaveHardware(AppConfig hardware) => _local.SaveHardware(hardware);
    }
}
