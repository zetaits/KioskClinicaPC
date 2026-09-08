using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KioskClinicaPC.Core.Sync;
using Serilog;

namespace KioskClinicaPC.Services
{
    public interface IAssetSyncService
    {
        Task<bool> SyncAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Sincroniza el manifiesto de imágenes del servidor hacia una caché remota separada. Cada fichero se
    /// verifica por tamaño/SHA-256 y se reemplaza atómicamente; un fallo conserva la última caché válida.
    /// </summary>
    public sealed class AssetSyncService : IAssetSyncService, IDisposable
    {
        private static readonly HashSet<string> Categories =
            new(StringComparer.Ordinal) { "Brands", "SpecImages", "ThemeAssets" };

        private readonly HttpClient? _http;
        private readonly string? _baseUrl;
        private readonly string _cacheRoot;
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        public AssetSyncService(string? serverUrl, string? apiKey, string cacheRoot)
            : this(BuildClient(apiKey), serverUrl, cacheRoot) { }

        public AssetSyncService(HttpClient http, string? serverUrl, string cacheRoot)
        {
            _http = string.IsNullOrWhiteSpace(serverUrl) ? null : http;
            _baseUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl.TrimEnd('/');
            _cacheRoot = cacheRoot;
        }

        public async Task<bool> SyncAsync(CancellationToken ct = default)
        {
            if (_http == null || _baseUrl == null) return false;
            await _syncGate.WaitAsync(ct);
            try
            {
                AssetManifest? manifest = await _http.GetFromJsonAsync<AssetManifest>(
                    _baseUrl + "/api/assets/manifest", ct);
                if (manifest == null) return false;

                bool changed = false;
                foreach (string category in Categories)
                {
                    string categoryDir = Path.Combine(_cacheRoot, category);
                    Directory.CreateDirectory(categoryDir);
                    var expected = manifest.Files
                        .Where(x => x.Category == category && IsSafeFileName(x.FileName))
                        .ToDictionary(x => x.FileName, StringComparer.OrdinalIgnoreCase);

                    foreach (AssetManifestItem item in expected.Values)
                    {
                        string destination = Path.Combine(categoryDir, item.FileName);
                        if (File.Exists(destination) &&
                            new FileInfo(destination).Length == item.SizeBytes &&
                            string.Equals(Hash(destination), item.Sha256, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            string url = _baseUrl + "/api/assets/" +
                                Uri.EscapeDataString(category) + "/" + Uri.EscapeDataString(item.FileName);
                            using HttpResponseMessage response = await _http.GetAsync(
                                url, HttpCompletionOption.ResponseHeadersRead, ct);
                            response.EnsureSuccessStatusCode();
                            await using (Stream input = await response.Content.ReadAsStreamAsync(ct))
                            await using (var output = new FileStream(
                                temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                                await input.CopyToAsync(output, ct);

                            var downloaded = new FileInfo(temp);
                            if (downloaded.Length != item.SizeBytes ||
                                !string.Equals(Hash(temp), item.Sha256, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException($"La imagen {item.FileName} no coincide con el manifiesto.");
                            File.Move(temp, destination, overwrite: true);
                            changed = true;
                        }
                        finally
                        {
                            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                        }
                    }

                    foreach (string local in Directory.EnumerateFiles(categoryDir))
                    {
                        if (!expected.ContainsKey(Path.GetFileName(local)))
                        {
                            File.Delete(local);
                            changed = true;
                        }
                    }
                }

                if (changed) Log.Information("Biblioteca de imágenes sincronizada (versión {Version}).", manifest.Version);
                return changed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "No se pudo sincronizar la biblioteca de imágenes; se conserva la caché.");
                return false;
            }
            finally
            {
                _syncGate.Release();
            }
        }

        private static HttpClient BuildClient(string? apiKey)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            if (!string.IsNullOrWhiteSpace(apiKey)) http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            return http;
        }

        private static bool IsSafeFileName(string fileName) =>
            !string.IsNullOrWhiteSpace(fileName) &&
            Path.GetFileName(fileName) == fileName &&
            string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase);

        private static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        public void Dispose()
        {
            _syncGate.Dispose();
            _http?.Dispose();
        }
    }
}
