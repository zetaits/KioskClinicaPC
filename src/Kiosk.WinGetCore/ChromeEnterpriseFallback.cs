using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Kiosk.InstallerCore;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

// Google's unversioned Enterprise MSI URL can advance before its WinGet manifest.
// This path is restricted to that exact hash-mismatch result and validates the new MSI independently.
internal static class ChromeEnterpriseFallback
{
    internal const int HashMismatch = unchecked((int)0x8A150011);
    private const string DownloadUrl = "https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi";
    private const long MaximumBytes = 512L * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    internal static bool Applies(string wingetId, Microsoft.Management.Deployment.InstallResultStatus status, int? extendedCode)
        => wingetId.Equals("Google.Chrome", StringComparison.OrdinalIgnoreCase) &&
           status == Microsoft.Management.Deployment.InstallResultStatus.DownloadError && extendedCode == HashMismatch;

    internal static async Task<PackNativeResult> Install(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct)
    {
        string file = log + ".chrome-enterprise.msi";
        bool nativeStarted = false;
        bool uncertain = false;
        try
        {
            progress(new(PackItemPhase.Downloading, "Descargando Chrome Enterprise desde Google…"));
            using var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is not { Scheme: "https" } finalUri ||
                !finalUri.Host.Equals("dl.google.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La descarga de Chrome salió del dominio oficial esperado.");
            if (response.Content.Headers.ContentLength is > MaximumBytes)
                throw new InvalidDataException("El MSI de Chrome supera el tamaño permitido.");
            await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            {
                byte[] buffer = new byte[1024 * 1024];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) != 0)
                {
                    total += count;
                    if (total > MaximumBytes) throw new InvalidDataException("El MSI de Chrome supera el tamaño permitido.");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    progress(new(PackItemPhase.Downloading, "Descargando Chrome Enterprise…", (ulong)total,
                        response.Content.Headers.ContentLength is long length ? (ulong)length : null));
                }
            }
            ct.ThrowIfCancellationRequested();
            progress(new(PackItemPhase.PostInstall, "Verificando firma y versión de Chrome…"));
            string version = Validate(file, item.Application.PinnedVersion);
            if (!string.IsNullOrEmpty(log))
            {
                try
                {
                    await using var downloaded = File.OpenRead(file);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, ct));
                    File.AppendAllText(log + ".chrome-fallback.jsonl", System.Text.Json.JsonSerializer.Serialize(new
                    { atUtc = DateTime.UtcNow, version, sha256 = hash, signer = "Google LLC" }) + Environment.NewLine);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { /* Diagnostics do not decide whether a validated installer can run. */ }
            }
            ct.ThrowIfCancellationRequested();
            progress(new(PackItemPhase.Installing, "Instalando Chrome Enterprise…"));
            var start = new ProcessStartInfo("msiexec.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { "/i", file, "/qn", "/norestart", "/l*v", log + ".chrome-msi.log" })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar el MSI de Chrome.");
            nativeStarted = true;
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                uncertain = true;
                return new(PackNativeDisposition.Uncertain, "Chrome puede seguir instalándose; revisa el equipo antes de reintentar.");
            }
            int exit = process.ExitCode;
            if (ct.IsCancellationRequested && exit is not (0 or 3010 or 1641))
                return new(PackNativeDisposition.Uncertain, "Se canceló mientras Chrome se instalaba; revisa el equipo antes de reintentar.");
            bool reboot = WinGetRebootStatus.Required(false, unchecked((uint)exit), WinGetRebootStatus.ReadSystemPending());
            return exit is 0 or 3010 or 1641
                ? new(PackNativeDisposition.Succeeded, "Chrome Enterprise instalado; comprobando versión y ámbito.", reboot)
                : new(PackNativeDisposition.Failed, $"El MSI oficial de Chrome terminó con código {exit}.", reboot);
        }
        catch (OperationCanceledException) when (!nativeStarted)
        { return new(PackNativeDisposition.Failed, "Se canceló la descarga de Chrome."); }
        catch (Exception ex) when (!nativeStarted)
        { return new(PackNativeDisposition.Failed, ex is InvalidDataException ? ex.Message : "No se pudo descargar o verificar el MSI oficial de Chrome."); }
        catch (Exception)
        {
            uncertain = true;
            return new(PackNativeDisposition.Uncertain, "Se perdió el resultado del MSI de Chrome; revisa el equipo antes de reintentar.");
        }
        finally
        {
            if (!uncertain)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal static string Validate(string file, string minimumVersion)
    {
        if (Authenticode.Verify(file) != SignatureResult.Valid)
            throw new InvalidDataException("La firma digital del MSI de Chrome no es válida.");
        // Authenticode verification above establishes trust; this legacy API extracts the MSI signer.
#pragma warning disable SYSLIB0057
        using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
        if (signer.GetNameInfo(X509NameType.SimpleName, false) != "Google LLC" ||
            !signer.Subject.Contains("O=Google LLC", StringComparison.Ordinal))
            throw new InvalidDataException("El MSI de Chrome no está firmado por Google LLC.");
        if (MsiProperty(file, "ProductName") != "Google Chrome")
            throw new InvalidDataException("El MSI descargado no es Google Chrome.");
        string version = MsiProperty(file, "ProductVersion");
        if (!Version.TryParse(version, out var actual) || !Version.TryParse(minimumVersion, out var minimum) || actual < minimum)
            throw new InvalidDataException("La versión del MSI de Chrome es anterior o no comparable con la requerida.");
        return version;
    }

    private static string MsiProperty(string file, string property)
    {
        int database = 0, view = 0, record = 0;
        try
        {
            if (MsiOpenDatabase(file, IntPtr.Zero, out database) != 0 || database == 0)
                throw new InvalidDataException("No se pudo abrir el MSI de Chrome.");
            if (MsiDatabaseOpenView(database, $"SELECT `Value` FROM `Property` WHERE `Property`='{property}'", out view) != 0 ||
                MsiViewExecute(view, 0) != 0 || MsiViewFetch(view, out record) != 0)
                throw new InvalidDataException("Faltan datos de identidad en el MSI de Chrome.");
            var value = new StringBuilder(256);
            int length = value.Capacity;
            if (MsiRecordGetString(record, 1, value, ref length) != 0)
                throw new InvalidDataException("No se pudo leer la identidad del MSI de Chrome.");
            return value.ToString();
        }
        finally
        {
            if (record != 0) MsiCloseHandle(record);
            if (view != 0) MsiCloseHandle(view);
            if (database != 0) MsiCloseHandle(database);
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiOpenDatabaseW")]
    private static extern uint MsiOpenDatabase(string path, IntPtr persist, out int database);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiDatabaseOpenViewW")]
    private static extern uint MsiDatabaseOpenView(int database, string query, out int view);
    [DllImport("msi.dll")]
    private static extern uint MsiViewExecute(int view, int record);
    [DllImport("msi.dll")]
    private static extern uint MsiViewFetch(int view, out int record);
    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiRecordGetStringW")]
    private static extern uint MsiRecordGetString(int record, uint field, StringBuilder value, ref int length);
    [DllImport("msi.dll")]
    private static extern uint MsiCloseHandle(int handle);
}
