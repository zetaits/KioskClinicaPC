using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace KioskClinicaPC.Services
{
    /// <summary>
    /// Auto-update Caso A: consulta el último release público en GitHub, y si hay una versión
    /// mayor que la instalada, descarga el instalador a %ProgramData%\KioskClinicaPC\updates,
    /// verifica su SHA256 y deja un "apply.flag". La tarea programada SYSTEM (creada por el
    /// instalador) lo aplica de madrugada en silencio + reinicio; el autostart relanza la app ya
    /// actualizada. Aquí NUNCA se ejecuta el instalador: la app corre como asInvoker y el setup
    /// requiere admin → se delega en la tarea SYSTEM para evitar el prompt UAC.
    /// </summary>
    public static class UpdateService
    {
        private const string Owner = "zetaits";
        private const string Repo = "KioskClinicaPC";
        private const string LatestReleaseApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

        // Carpeta compartida (machine-wide): la app (usuario kiosko) escribe aquí y la tarea SYSTEM
        // lee de aquí. El instalador la crea con ACL Users=Modify.
        public static readonly string UpdatesFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "KioskClinicaPC", "updates");
        public static readonly string PendingSetupPath = Path.Combine(UpdatesFolder, "Setup.exe");
        public static readonly string ApplyFlagPath = Path.Combine(UpdatesFolder, "apply.flag");

        private static readonly HttpClient Http = CreateClient();

        public enum UpdateOutcome
        {
            /// <summary>Ya está en la última versión.</summary>
            UpToDate,
            /// <summary>Versión nueva descargada y verificada; quedará aplicada por la tarea SYSTEM.</summary>
            Staged,
            /// <summary>No se pudo comprobar/descargar (sin red, API caída, hash inválido...).</summary>
            Failed
        }

        public readonly record struct UpdateResult(UpdateOutcome Outcome, string? LatestVersion, string? Error);

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // GitHub exige User-Agent; sin él devuelve 403.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KioskClinicaPC-Updater");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        /// <summary>Versión instalada (del assembly, sembrada por &lt;Version&gt; del csproj).</summary>
        public static Version CurrentVersion =>
            Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

        /// <summary>
        /// Comprueba, descarga y deja lista la actualización. Es no-bloqueante para el kiosko:
        /// cualquier fallo de red se traga y devuelve Failed (la app sigue corriendo igual).
        /// </summary>
        public static async Task<UpdateResult> CheckAndStageAsync()
        {
            try
            {
                // 1. Último release.
                string json = await Http.GetStringAsync(LatestReleaseApi);
                var release = JObject.Parse(json);
                string? tag = release.Value<string>("tag_name");
                if (string.IsNullOrWhiteSpace(tag))
                {
                    Log.Warning("Auto-update: release sin tag_name.");
                    return new UpdateResult(UpdateOutcome.Failed, null, "Respuesta sin tag_name.");
                }

                Version latest = ParseTag(tag);
                if (latest <= CurrentVersion)
                {
                    Log.Information("Auto-update: ya en la última versión ({Current}).", CurrentVersion);
                    return new UpdateResult(UpdateOutcome.UpToDate, tag, null);
                }

                Log.Information("Auto-update: versión nueva {Latest} (instalada {Current}).", latest, CurrentVersion);

                // 2. Localiza los assets: el Setup .exe y su .sha256.
                var assets = release.Value<JArray>("assets") ?? new JArray();
                string? setupUrl = AssetUrl(assets, a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                string? shaUrl = AssetUrl(assets, a => a.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase));
                if (setupUrl == null || shaUrl == null)
                {
                    Log.Warning("Auto-update: faltan assets (setup={Setup}, sha={Sha}).", setupUrl != null, shaUrl != null);
                    return new UpdateResult(UpdateOutcome.Failed, tag, "Release sin Setup.exe o .sha256.");
                }

                // 3. Descarga el checksum esperado (primer token = hash hex).
                string shaContent = await Http.GetStringAsync(shaUrl);
                string expectedHash = shaContent.Trim().Split(new[] { ' ', '\t', '\r', '\n', '*' },
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (expectedHash.Length != 64)
                {
                    Log.Warning("Auto-update: .sha256 con formato inesperado.");
                    return new UpdateResult(UpdateOutcome.Failed, tag, "Checksum con formato inválido.");
                }

                // 4. Descarga el instalador a un .tmp y verifica el hash antes de promoverlo.
                Directory.CreateDirectory(UpdatesFolder);
                string tmp = PendingSetupPath + ".tmp";
                await DownloadToFileAsync(setupUrl, tmp);

                string actualHash = ComputeSha256(tmp);
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Error("Auto-update: SHA256 no coincide (esperado {Exp}, real {Act}). Se descarta.", expectedHash, actualHash);
                    TryDelete(tmp);
                    return new UpdateResult(UpdateOutcome.Failed, tag, "El instalador descargado no supera la verificación SHA256.");
                }

                // 5. Promueve y deja el flag para la tarea SYSTEM.
                if (File.Exists(PendingSetupPath)) File.Delete(PendingSetupPath);
                File.Move(tmp, PendingSetupPath);
                File.WriteAllText(ApplyFlagPath, tag);
                Log.Information("Auto-update: {Tag} descargado y verificado en {Path}. Pendiente de aplicar.", tag, PendingSetupPath);
                return new UpdateResult(UpdateOutcome.Staged, tag, null);
            }
            catch (Exception ex)
            {
                // Sin internet / API caída / disco: no es fatal, el kiosko sigue.
                Log.Warning(ex, "Auto-update: comprobación fallida (se ignora).");
                return new UpdateResult(UpdateOutcome.Failed, null, ex.Message);
            }
        }

        private static string? AssetUrl(JArray assets, Func<string, bool> nameMatches)
        {
            foreach (var a in assets)
            {
                string? name = a.Value<string>("name");
                string? url = a.Value<string>("browser_download_url");
                if (name != null && url != null && nameMatches(name)) return url;
            }
            return null;
        }

        private static async Task DownloadToFileAsync(string url, string destPath)
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await src.CopyToAsync(dst);
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }

        /// <summary>Convierte un tag tipo "v1.2.0" / "1.2" en Version normalizada (sin -1 en build/revision).</summary>
        private static Version ParseTag(string tag)
        {
            string clean = tag.Trim().TrimStart('v', 'V');
            return Version.TryParse(clean, out var v) ? Normalize(v) : new Version(0, 0, 0);
        }

        private static Version Normalize(Version v) =>
            new Version(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
