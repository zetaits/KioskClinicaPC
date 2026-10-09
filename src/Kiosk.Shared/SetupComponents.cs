using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace KioskClinicaPC.Equipment;

public sealed record SetupComponent(string Kind, string Version, long SizeBytes, string Sha256)
{
    public bool Compatible => Kind is "kiosk" or "worker" && System.Version.TryParse(Version, out _) &&
        SizeBytes is > 0 and <= 512L * 1024 * 1024 && Regex.IsMatch(Sha256 ?? "", "^[a-f0-9]{64}$");
}
public sealed class ComponentPreparationException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Hash-addressed files. The caller creates an administrator-owned cache before use.</summary>
public sealed class SetupComponentCache(string root, HttpClient http, EquipmentConfiguration configuration,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeSpan? idleTimeout = null, TimeSpan? componentTimeout = null)
{
    public const long MaxBytes = 1024L * 1024 * 1024;
    public static void SafePath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw EquipmentDiagnostics.IOError("Ruta de componentes redirigida.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public static async Task<bool> Verify(string path, SetupComponent component, CancellationToken ct)
    {
        SafePath(path);
        if (!File.Exists(path) || new FileInfo(path).Length != component.SizeBytes) return false;
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(component.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    public async Task CopyTo(SetupComponent component, string target, Action<int> progress, CancellationToken ct)
    {
        if (!component.Compatible) throw EquipmentDiagnostics.InvalidData("Descriptor incompatible.");
        SafePath(root); Directory.CreateDirectory(root);
        string path = Path.Combine(root, component.Sha256);
        foreach (string item in new[] { path, path + ".part", path + ".etag", path + ".lock", target }) SafePath(item);
        // This lock also protects partial metadata and prevents cleanup while files are copied for an operation.
        await using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!await Verify(path, component, ct))
        {
            string quotaPath = Path.Combine(root, "quota.lock"); SafePath(quotaPath);
            await using var quota = new FileStream(quotaPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            File.Delete(path);
            Cleanup(component.SizeBytes, component.Sha256);
            await Download(component, path, progress, ct);
        }
        ct.ThrowIfCancellationRequested();
        await using (var input = File.OpenRead(path))
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await input.CopyToAsync(output, ct);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        progress(100);
    }
    private void Cleanup(long required, string keep)
    {
        // Never enumerate/delete state or logs. Only our canonical hash-addressed cache is eligible.
        var files = Directory.EnumerateFiles(root).Select(p => new FileInfo(p)).ToList();
        long total = files.Where(f => !f.Name.StartsWith(keep, StringComparison.Ordinal) && Regex.IsMatch(f.Name, "^[a-f0-9]{64}(\\.part)?$")).Sum(f => f.Length);
        foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (!Regex.IsMatch(file.Name, "^[a-f0-9]{64}(\\.part)?$") || file.Name.StartsWith(keep, StringComparison.Ordinal)) continue;
            bool partial = file.Name.EndsWith(".part", StringComparison.Ordinal);
            if (total + required <= MaxBytes && (!partial || file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7))) continue;
            string hash = file.Name[..64];
            try
            {
                SafePath(file.FullName); SafePath(Path.Combine(root, hash + ".lock"));
                using var lease = new FileStream(Path.Combine(root, hash + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                long length = file.Length; File.Delete(file.FullName); total -= length;
                if (partial) { SafePath(Path.Combine(root, hash + ".etag")); File.Delete(Path.Combine(root, hash + ".etag")); }
            }
            catch (IOException) { } // Active operation: leave its files alone.
        }
        if (total + required > MaxBytes) throw EquipmentDiagnostics.IOError("La caché está ocupada por otras operaciones.");
    }
    private async Task Download(SetupComponent component, string path, Action<int> progress, CancellationToken ct)
    {
        if (!Uri.TryCreate(configuration.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute, out var server) ||
            (server.Scheme != "https" && !(server.Scheme == "http" && server.IsLoopback)) ||
            server.UserInfo.Length != 0 || server.Query.Length != 0 || server.Fragment.Length != 0 || string.IsNullOrWhiteSpace(configuration.SetupKey))
            throw EquipmentDiagnostics.InvalidData("Servidor de componentes incompatible.");
        string partial = path + ".part", metadata = path + ".etag";
        string expectedTag = "\"" + component.Sha256 + "\"";
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        overall.CancelAfter(componentTimeout ?? TimeSpan.FromMinutes(30));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            overall.Token.ThrowIfCancellationRequested();
            try
            {
                long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                string? tag = File.Exists(metadata) ? await File.ReadAllTextAsync(metadata, overall.Token) : null;
                if (offset >= component.SizeBytes || tag != expectedTag) { File.Delete(partial); File.Delete(metadata); offset = 0; }
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, $"api/setup/v3/components/{component.Kind}/{component.Sha256}"));
                request.Headers.Add("X-Setup-Key", configuration.SetupKey);
                if (offset > 0) { request.Headers.Range = new RangeHeaderValue(offset, null); request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(tag!)); }
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
                idle.CancelAfter(idleTimeout ?? TimeSpan.FromMinutes(2));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    throw new HttpRequestException("Fallo transitorio.", null, response.StatusCode);
                if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
                    throw EquipmentDiagnostics.InvalidData($"El servidor rechazó el componente ({(int)response.StatusCode}).",
                        new HttpRequestException("Componente rechazado.", null, response.StatusCode));
                if (response.Headers.ETag?.ToString() != expectedTag)
                { File.Delete(partial); File.Delete(metadata); throw EquipmentDiagnostics.InvalidData("ETag del componente incompatible."); }
                if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                else if (offset == 0 || response.Content.Headers.ContentRange is not { Unit: "bytes" } range ||
                    range.From != offset || range.To != component.SizeBytes - 1 || range.Length != component.SizeBytes)
                { File.Delete(partial); File.Delete(metadata); throw EquipmentDiagnostics.InvalidData("Respuesta Range incompatible."); }
                if (response.Content.Headers.ContentLength is { } length && length != component.SizeBytes - offset)
                    throw EquipmentDiagnostics.InvalidData("Tamaño de descarga incorrecto.");
                await File.WriteAllTextAsync(metadata, expectedTag, overall.Token);
                await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 81920, true))
                await using (var input = await response.Content.ReadAsStreamAsync(idle.Token))
                {
                    byte[] buffer = new byte[81920]; long done = offset; int read, lastPercent = -1;
                    while (true)
                    {
                        idle.CancelAfter(idleTimeout ?? TimeSpan.FromMinutes(2));
                        read = await input.ReadAsync(buffer, idle.Token);
                        if (read == 0) break;
                        done += read;
                        if (done > component.SizeBytes) throw EquipmentDiagnostics.InvalidData("El componente excede su tamaño.");
                        await output.WriteAsync(buffer.AsMemory(0, read), idle.Token);
                        int percent = (int)(100 * done / component.SizeBytes);
                        if (percent != lastPercent) { lastPercent = percent; progress(percent); }
                    }
                    if (done != component.SizeBytes) throw EquipmentDiagnostics.IOError("Descarga interrumpida.");
                }
                if (!await Verify(partial, component, overall.Token))
                { File.Delete(partial); File.Delete(metadata); throw EquipmentDiagnostics.InvalidData("SHA-256 del componente incorrecto."); }
                File.Move(partial, path); File.Delete(metadata); return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || ex is OperationCanceledException && !overall.IsCancellationRequested)
            {
                if (attempt == 2) throw EquipmentDiagnostics.IOError("Descarga fallida tras tres intentos.", ex);
                await (delay ?? Task.Delay)(TimeSpan.FromSeconds(attempt + 1), overall.Token);
            }
            catch (InvalidDataException) { File.Delete(partial); File.Delete(metadata); throw; }
        }
    }
}
